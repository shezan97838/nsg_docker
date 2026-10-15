using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// Enforces docs/DATABASE-STANDARDS.md on the EF model without needing a database: every table, column, index, key and
/// constraint is lowercase snake_case, so the schema behaves the same on Linux (case-sensitive table names) and Windows.
/// The complete expected schema is pinned in <see cref="ExpectedDatabaseSchema"/>.
/// </summary>
public class DatabaseNamingRuleTests
{
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant);

    private static DbContextOptions<AppDbContext> Options() => new DbContextOptionsBuilder<AppDbContext>()
        .UseMySql("Server=unused;Database=unused;Uid=unused;Pwd=unused;", new MySqlServerVersion(new Version(8, 0, 36)))
        .Options;

    private static IModel BuildModel()
    {
        using AppDbContext context = new(Options());
        return context.GetService<IDesignTimeModel>().Model;   // the design-time model carries the relational names and types
    }

    private static IEnumerable<IEntityType> Entities(IModel model) => model.GetEntityTypes().Where(e => e.GetTableName() != null);

    private static StoreObjectIdentifier Store(IEntityType e) => StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema());

    // ── The naming function ──

    [Theory]
    [InlineData("UsersPIS", "users_pis")]
    [InlineData("NSGTrade", "nsg_trade")]
    [InlineData("PDTradeName", "pd_trade_name")]
    [InlineData("NsgTradeId", "nsg_trade_id")]
    [InlineData("CreatedAtIst", "created_at_ist")]
    [InlineData("MobileAppActivationStatus", "mobile_app_activation_status")]
    [InlineData("IX_UsersPIS_NsgNumber", "ix_users_pis_nsg_number")]
    [InlineData("SrNo", "sr_no")]
    [InlineData("Id", "id")]
    [InlineData("rank_hierarchy", "rank_hierarchy")]
    public void ToSnakeCase_converts_pascal_case_and_acronyms(string input, string expected)
        => Assert.Equal(expected, DatabaseNamingStandard.ToSnakeCase(input));

    [Fact]
    public void An_entity_without_an_explicit_snake_case_table_name_stops_model_building()
    {
        using ImplicitTableContext context = new();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => context.GetService<IDesignTimeModel>().Model);

        Assert.Contains("ToTable", ex.ToString());
    }

    private sealed class ImplicitTableContext : DbContext
    {
        public ImplicitTableContext() : base(new DbContextOptionsBuilder<ImplicitTableContext>()
            .UseMySql("Server=unused;Database=unused;Uid=unused;Pwd=unused;", new MySqlServerVersion(new Version(8, 0, 36))).Options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Probe>();   // no ToTable: would be mapped to "Probes"
            DatabaseNamingStandard.Apply(modelBuilder);
        }

        private sealed class Probe { public int Id { get; set; } }
    }

    // ── The actual schema ──

    [Fact]
    public void Every_table_column_index_key_and_foreign_key_is_lowercase_snake_case_without_reserved_words()
    {
        List<string> violations = new();

        void Check(string kind, string name)
        {
            if (!SnakeCase.IsMatch(name)) violations.Add($"{kind} '{name}' is not lowercase snake_case within 64 characters");
            if (MySqlReservedWords.All.Contains(name)) violations.Add($"{kind} '{name}' is a MySQL 8.0 reserved word");
        }

        foreach (IEntityType entity in Entities(BuildModel()))
        {
            StoreObjectIdentifier store = Store(entity);
            string table = entity.GetTableName()!;
            Check("table", table);

            foreach (IProperty property in entity.GetProperties())
                Check($"column {table}.", property.GetColumnName(store) ?? "(unnamed)");

            foreach (IIndex index in entity.GetIndexes())
                Check($"index on {table}", index.GetDatabaseName(store) ?? "(unnamed)");

            foreach (IForeignKey fk in entity.GetForeignKeys())
                Check($"foreign key on {table}", fk.GetConstraintName(store, StoreObjectIdentifier.Table(fk.PrincipalEntityType.GetTableName()!, null))!);

            foreach (ICheckConstraint check in entity.GetCheckConstraints())
                Check($"check constraint on {table}", check.Name ?? "(unnamed)");
        }

        Assert.True(violations.Count == 0, "Naming violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_mapped_schema_is_exactly_the_pinned_inventory()
    {
        Dictionary<string, string[]> actual = Entities(BuildModel()).ToDictionary(
            e => e.GetTableName()!,
            e => e.GetProperties().Select(p => $"{p.GetColumnName(Store(e))}|{p.GetColumnType()}|{(p.IsNullable ? "NULL" : "NOT NULL")}").ToArray());

        Assert.Equal(ExpectedDatabaseSchema.Tables.Keys.OrderBy(k => k), actual.Keys.OrderBy(k => k));
        foreach ((string table, string[] expected) in ExpectedDatabaseSchema.Tables)
            Assert.True(expected.OrderBy(c => c, StringComparer.Ordinal).SequenceEqual(actual[table].OrderBy(c => c, StringComparer.Ordinal)),   // column order is not part of the contract
                $"Schema drift in table '{table}':{Environment.NewLine}expected: {string.Join(", ", expected.OrderBy(c => c))}{Environment.NewLine}actual:   {string.Join(", ", actual[table].OrderBy(c => c))}");
    }

    [Fact]
    public void Foreign_key_columns_have_exactly_the_type_of_the_key_they_reference_and_never_cascade()
    {
        List<string> problems = new();

        foreach (IEntityType entity in Entities(BuildModel()))
            foreach (IForeignKey fk in entity.GetForeignKeys())
            {
                for (int i = 0; i < fk.Properties.Count; i++)
                {
                    string dependent = fk.Properties[i].GetColumnType();
                    string principal = fk.PrincipalKey.Properties[i].GetColumnType();
                    if (dependent != principal)
                        problems.Add($"{entity.GetTableName()}.{fk.Properties[i].GetColumnName(Store(entity))}: {dependent} references {principal}");
                }

                if (fk.DeleteBehavior != DeleteBehavior.Restrict)
                    problems.Add($"{entity.GetTableName()}: foreign key {fk.GetConstraintName()} uses {fk.DeleteBehavior} instead of Restrict");
            }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Check_constraints_only_reference_existing_snake_case_columns_of_their_own_table()
    {
        List<string> problems = new();

        foreach (IEntityType entity in Entities(BuildModel()))
        {
            HashSet<string> columns = entity.GetProperties().Select(p => p.GetColumnName(Store(entity))!).ToHashSet();
            foreach (ICheckConstraint check in entity.GetCheckConstraints())
                foreach (Match identifier in Regex.Matches(check.Sql, "`([^`]+)`"))
                    if (!columns.Contains(identifier.Groups[1].Value))
                        problems.Add($"{entity.GetTableName()}.{check.Name} references unknown column '{identifier.Groups[1].Value}'");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_complete_create_script_contains_no_identifier_with_an_uppercase_letter()
    {
        // The Linux-targeted check available without a Linux server: every identifier the model would send to MySQL is
        // lowercase, so lower_case_table_names (0 on Linux, 1 on Windows) cannot change what the SQL means.
        using AppDbContext context = new(Options());
        string script = context.Database.GenerateCreateScript();

        string[] offenders = Regex.Matches(script, "`([^`]+)`").Select(m => m.Groups[1].Value).Where(id => id != id.ToLowerInvariant()).Distinct().ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Naming_of_the_two_trade_tables_and_the_reserved_word_rank_column()
    {
        IModel model = BuildModel();
        string[] tables = Entities(model).Select(e => e.GetTableName()!).ToArray();
        IEntityType users = Entities(model).Single(e => e.GetTableName() == "users");

        Assert.Contains("nsg_trade", tables);
        Assert.Contains("pd_trade", tables);
        Assert.Equal("rank_name", users.FindProperty("Rank")!.GetColumnName(Store(users)));   // "rank" is reserved in MySQL 8
    }

    [Fact]
    public void Every_datetime_column_says_ist_and_every_date_only_column_is_a_date()
    {
        List<string> problems = new();

        foreach (IEntityType entity in Entities(BuildModel()))
            foreach (IProperty p in entity.GetProperties())
            {
                string column = p.GetColumnName(Store(entity))!;
                Type clr = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
                if (clr == typeof(DateTime) && !(column.EndsWith("_ist") && p.GetColumnType() == "datetime(6)"))
                    problems.Add($"{entity.GetTableName()}.{column}: DateTime must be datetime(6) and end in _ist");
                if (clr == typeof(DateOnly) && p.GetColumnType() != "date")
                    problems.Add($"{entity.GetTableName()}.{column}: DateOnly must be a DATE column");
            }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
