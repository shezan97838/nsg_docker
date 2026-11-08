using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

/// <summary>
/// Salary slip upload data access. The month lock is a MySQL named lock (<c>GET_LOCK</c>): it belongs to one database session, so a
/// dedicated <see cref="MySqlConnection"/> is opened for it and the scope's own <see cref="AppDbContext"/> runs on that same open connection.
/// </summary>
public class SalarySlipRepository : ISalarySlipRepository
{
    private readonly AppDbContext _context;
    private readonly ILoggerFactory _loggerFactory;

    public SalarySlipRepository(AppDbContext context, ILoggerFactory loggerFactory)
    {
        _context = context;
        _loggerFactory = loggerFactory;
    }

    public async Task<ISalarySlipMonthScope?> TryAcquireMonthScopeAsync(DateOnly salaryMonth, TimeSpan timeout, CancellationToken cancellationToken)
    {
        string connectionString = _context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The database connection string is not configured.");

        MySqlConnection connection = new(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);

            string lockName = MonthScope.LockName(salaryMonth);
            await using (MySqlCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT GET_LOCK(@name, @timeout)";
                command.Parameters.AddWithValue("@name", lockName);
                command.Parameters.AddWithValue("@timeout", (int)Math.Max(0, Math.Ceiling(timeout.TotalSeconds)));
                object? answer = await command.ExecuteScalarAsync(cancellationToken);

                // 1 = acquired, 0 = timed out, NULL = error.
                if (answer is null or DBNull || Convert.ToInt32(answer) != 1)
                {
                    await connection.DisposeAsync();
                    return null;
                }
            }

            // From here on the session owns the lock: it is released by MonthScope.DisposeAsync, or by the server if the session ends.
            DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
                .UseMySql(connection, ServerVersion.AutoDetect(connection))   // no retry strategy: the scope runs its own transaction on this connection
                .Options;
            return new MonthScope(connection, new AppDbContext(options), salaryMonth, _loggerFactory.CreateLogger<MonthScope>());
        }
        catch
        {
            // Disposing a connection that holds a lock ends the session, which releases the lock.
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class MonthScope : ISalarySlipMonthScope
    {
        private readonly MySqlConnection _connection;
        private readonly AppDbContext _db;
        private readonly DateOnly _month;
        private readonly ILogger _logger;

        public MonthScope(MySqlConnection connection, AppDbContext db, DateOnly month, ILogger logger)
        {
            _connection = connection;
            _db = db;
            _month = month;
            _logger = logger;
        }

        /// <summary>At most 64 characters, as MySQL requires.</summary>
        public static string LockName(DateOnly month) => $"nsg:salary_slips:{month:yyyy-MM}";

        public async Task<long> CreateBatchAsync(SalarySlipUploadBatch batch, CancellationToken cancellationToken)
        {
            _db.SalarySlipUploadBatches.Add(batch);
            await _db.SaveChangesAsync(cancellationToken);
            long id = batch.Id;
            _db.ChangeTracker.Clear();
            return id;
        }

        public async Task<IReadOnlyList<string>> GetExistingAccountNumbersAsync(CancellationToken cancellationToken)
            => await _db.SalarySlips.AsNoTracking().Where(s => s.SalaryMonth == _month).Select(s => s.AccountNumberEncrypted).ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<PisAccountReference>> GetPisAccountsAsync(CancellationToken cancellationToken)
            => await _db.UsersPis.AsNoTracking().Where(p => p.SalaryAccountNumberEncrypted != null)
                .Select(p => new PisAccountReference(p.Id, p.SalaryAccountNumberEncrypted!)).ToListAsync(cancellationToken);

        public async Task SaveSlipsAndCompleteBatchAsync(SalarySlipUploadBatch batch, IReadOnlyList<SalarySlip> slips, CancellationToken cancellationToken)
        {
            _db.ChangeTracker.Clear();
            try
            {
                await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                _db.SalarySlipUploadBatches.Update(batch);
                foreach (SalarySlip slip in slips)
                {
                    slip.Id = 0;
                    slip.OriginalBatchId = batch.Id;
                    _db.SalarySlips.Add(slip);
                }

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        public async Task UpdateBatchAsync(SalarySlipUploadBatch batch, CancellationToken cancellationToken)
        {
            _db.ChangeTracker.Clear();
            try
            {
                _db.SalarySlipUploadBatches.Update(batch);
                await _db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        public async Task<int> CountSlipsOfBatchAsync(long batchId, CancellationToken cancellationToken)
            => await _db.SalarySlips.AsNoTracking().CountAsync(s => s.OriginalBatchId == batchId, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            bool released = false;
            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    await using MySqlCommand command = _connection.CreateCommand();
                    command.CommandText = "SELECT RELEASE_LOCK(@name)";
                    command.Parameters.AddWithValue("@name", LockName(_month));
                    object? answer = await command.ExecuteScalarAsync(CancellationToken.None);   // never cancelled: the lock must not outlive the upload
                    released = answer is not (null or DBNull) && Convert.ToInt32(answer) == 1;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The salary slip month lock could not be released explicitly");
            }

            await _db.DisposeAsync();

            if (!released)
            {
                // Make sure a pooled session cannot keep the lock: closing the pool's sessions ends them, which frees it.
                try { await MySqlConnection.ClearPoolAsync(_connection); } catch { /* the connection is going away anyway */ }
            }

            await _connection.DisposeAsync();
        }
    }
}
