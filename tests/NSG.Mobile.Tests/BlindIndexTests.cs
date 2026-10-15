using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Security;

namespace NSG.Mobile.Tests;

/// <summary>Unit tests for the HMAC blind index used to enforce PAN and salary-account uniqueness.</summary>
public class BlindIndexServiceTests
{
    private static HmacBlindIndexService Create(byte[]? key = null)
        => new(Options.Create(new BlindIndexOptions { Key = Convert.ToBase64String(key ?? RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public void Hash_is_deterministic_64_lowercase_hex_and_not_the_plaintext()
    {
        HmacBlindIndexService service = Create();

        string a = service.Compute("p", "AGIPR3625F");
        string b = service.Compute("p", "AGIPR3625F");

        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{64}$", a);
        Assert.DoesNotContain("AGIPR3625F", a, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(a, service.Compute("p", "AGIPR3625G"));
    }

    [Fact]
    public void Hash_depends_on_both_the_key_and_the_purpose()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);

        Assert.NotEqual(Create(key).Compute("pan", "123456789"), Create(key).Compute("account", "123456789"));
        Assert.NotEqual(Create().Compute("pan", "123456789"), Create().Compute("pan", "123456789")); // different random keys
        Assert.Equal(Create(key).Compute("pan", "x"), Create(key).Compute("pan", "x"));
    }

    [Fact]
    public void Purpose_prefix_cannot_be_forged_by_shifting_characters_into_the_value()
    {
        HmacBlindIndexService service = Create();
        Assert.NotEqual(service.Compute("ab", "c"), service.Compute("a", "bc"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("QUJD")]   // valid base64 but only 3 bytes
    public void Weak_or_malformed_keys_are_rejected(string key)
        => Assert.Throws<InvalidOperationException>(() => new HmacBlindIndexService(Options.Create(new BlindIndexOptions { Key = key })));

    [Fact]
    public void Pan_hash_ignores_case_and_surrounding_spaces_and_missing_values_have_no_hash()
    {
        HmacBlindIndexService service = Create();

        string? canonical = service.ForPan("AGIPR3625F");

        Assert.NotNull(canonical);
        Assert.Equal(canonical, service.ForPan("  agipr3625f "));
        Assert.NotEqual(canonical, service.ForPan("AGIPR3625G"));
        Assert.Null(service.ForPan(null));
        Assert.Null(service.ForPan("   "));
    }

    [Fact]
    public void Salary_account_hash_is_trimmed_and_independent_of_the_pan_hash()
    {
        HmacBlindIndexService service = Create();

        Assert.Equal(service.ForSalaryAccountNumber("30927989329"), service.ForSalaryAccountNumber(" 30927989329 "));
        Assert.NotEqual(service.ForSalaryAccountNumber("30927989329"), service.ForSalaryAccountNumber("30927989330"));
        Assert.Null(service.ForSalaryAccountNumber(null));
        Assert.NotEqual(service.ForPan("AGIPR3625F"), service.ForSalaryAccountNumber("AGIPR3625F")); // purpose separation
    }
}

/// <summary>PAN and salary-account uniqueness enforced by MySQL through the blind-index columns.</summary>
[Collection("mysql")]
public class PisUniquenessTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HmacBlindIndexService _hash = new(Options.Create(new BlindIndexOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    public PisUniquenessTests(DatabaseFixture fixture) => _factory = fixture.Factory;

    public async Task InitializeAsync() => await _factory.ClearGeographyAsync(); // also empties UsersPIS

    public Task DisposeAsync() => Task.CompletedTask;

    private UserPis Person(string nsg, string? pan = null, string? account = null) => new()
    {
        NsgNumber = nsg,
        ServiceNumber = "S",
        CreatedAtIst = IndianStandardTime.Now,
        PanNumberHash = _hash.ForPan(pan),
        SalaryAccountNumberHash = _hash.ForSalaryAccountNumber(account)
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
        Assert.DoesNotContain("AGIPR3625F", mysql.Message); // the error never carries the plaintext
    }

    [Fact]
    public async Task Duplicate_pan_is_rejected_even_when_typed_in_a_different_case()
    {
        await SaveAsync(Person("P-1", pan: "AGIPR3625F"));

        await AssertDuplicateKeyAsync(Person("P-2", pan: "agipr3625f"), "ix_users_pis_pan_number_hash");
    }

    [Fact]
    public async Task Duplicate_salary_account_number_is_rejected()
    {
        await SaveAsync(Person("A-1", account: "30927989329"));

        await AssertDuplicateKeyAsync(Person("A-2", account: " 30927989329"), "ix_users_pis_salary_account_number_hash");
    }

    [Fact]
    public async Task Different_values_and_missing_values_are_allowed_to_coexist()
    {
        await SaveAsync(Person("D-1", pan: "AGIPR3625F", account: "30927989329"));
        await SaveAsync(Person("D-2", pan: "AGIPR3625G", account: "30927989330"));
        await SaveAsync(Person("D-3"));   // no PAN, no account
        await SaveAsync(Person("D-4"));   // NULLs may repeat

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(4, await ctx.UsersPis.CountAsync());
    }

    [Fact]
    public async Task The_same_digits_in_pan_and_account_hashes_never_collide_across_columns()
    {
        // Same text in both fields hashes differently thanks to purpose separation, so neither blocks the other.
        await SaveAsync(Person("X-1", pan: "AGIPR3625F"));
        await SaveAsync(Person("X-2", account: "123456789"));
        Assert.NotEqual(_hash.ForPan("AGIPR3625F"), _hash.ForSalaryAccountNumber("AGIPR3625F"));
    }

    [Theory]
    [InlineData("AGIPR3625F")]                                                    // plaintext, not a hash
    [InlineData("0123456789abcdef")]                                              // too short
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // 64 chars, not hex
    public async Task Only_64_character_hex_values_are_accepted_in_the_hash_columns(string value)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        UserPis p = new() { NsgNumber = "H-" + value.Length, ServiceNumber = "S", CreatedAtIst = IndianStandardTime.Now, PanNumberHash = value };
        ctx.UsersPis.Add(p);

        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("ck_users_pis_hashes_format", ex.InnerException!.Message);
    }
}
