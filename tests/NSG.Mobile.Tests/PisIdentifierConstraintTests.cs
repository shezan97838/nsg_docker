using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// users_pis after the text-model refactor: NSG number and mobile number stay unique (mobile is mandatory); the encrypted
/// identifiers carry no hash columns and no unique index. Needs NSG_TEST_MYSQL.
/// </summary>
[Collection("mysql")]
public class PisIdentifierConstraintTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;

    public PisIdentifierConstraintTests(DatabaseFixture fixture) => _factory = fixture.Factory;

    public async Task InitializeAsync() => await _factory.ClearGeographyAsync(); // also empties UsersPIS

    public Task DisposeAsync() => Task.CompletedTask;

    private static UserPis Person(string nsg, string mobile, string? pan = null, string? account = null) => new()
    {
        NsgNumber = nsg,
        MobileNumber = mobile,
        CreatedAtIst = IndianStandardTime.Now,
        PanNumberEncrypted = pan,
        SalaryAccountNumberEncrypted = account
    };

    private async Task SaveAsync(UserPis p)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(p);
        await ctx.SaveChangesAsync();
    }

    private async Task AssertDuplicateKeyAsync(UserPis p, string expectedIndex)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(p);
        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        MySqlException mysql = Assert.IsType<MySqlException>(ex.InnerException);
        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, mysql.ErrorCode);
        Assert.Contains(expectedIndex, mysql.Message);
    }

    [Fact]
    public async Task Duplicate_nsg_number_and_duplicate_mobile_number_are_rejected()
    {
        await SaveAsync(Person("U-1", "9000000001"));

        await AssertDuplicateKeyAsync(Person("U-1", "9000000002"), "ix_users_pis_nsg_number");
        await AssertDuplicateKeyAsync(Person("U-2", "9000000001"), "ix_users_pis_mobile_number");
    }

    [Fact]
    public async Task The_same_encrypted_identifier_may_be_stored_for_different_people()
    {
        await SaveAsync(Person("E-1", "9000000011", pan: "CIPHERTEXT-A", account: "CIPHERTEXT-B"));
        await SaveAsync(Person("E-2", "9000000012", pan: "CIPHERTEXT-A", account: "CIPHERTEXT-B"));   // no unique index on them

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(2, await ctx.UsersPis.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("98765abcde")]
    public async Task A_blank_or_malformed_mobile_number_is_rejected_by_the_check_constraint(string mobile)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(Person("M-" + mobile.Length, mobile));

        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("ck_users_pis_mobile_number_format", ex.InnerException!.Message);
    }
}
