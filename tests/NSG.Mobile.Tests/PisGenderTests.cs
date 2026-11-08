using System.Text.Json;
using FluentValidation.Results;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Application.Pis.Validators;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Tests;

/// <summary>No-database tests for the PIS gender field: it is free text (no predefined list) that is required on create.</summary>
public class PisGenderTests
{
    private static CreateUserPisRequestDto Create(string? gender) => new() { NsgNumber = "N-1", ServiceNumber = "S-1", MobileNumber = "9876543210", Gender = gender };
    private static UpdateUserPisRequestDto Update(string? gender) => new() { ServiceNumber = "S-1", MobileNumber = "9876543210", IsActive = true, Gender = gender };

    [Theory]
    [InlineData("Male")]
    [InlineData("female")]
    [InlineData("M")]
    [InlineData("as the workbook writes it")]
    public void Create_accepts_any_gender_text(string gender)
        => Assert.True(new CreateUserPisRequestValidator().Validate(Create(gender)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_still_requires_a_gender(string? gender)
    {
        ValidationResult result = new CreateUserPisRequestValidator().Validate(Create(gender));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateUserPisRequestDto.Gender));
    }

    [Fact]
    public void Gender_is_limited_to_the_column_length_of_30()
    {
        Assert.True(new UpdateUserPisRequestValidator().Validate(Update(new string('x', 30))).IsValid);
        Assert.Contains(new UpdateUserPisRequestValidator().Validate(Update(new string('x', 31))).Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.Gender));
    }

    [Fact]
    public void Update_allows_an_omitted_gender()
        => Assert.True(new UpdateUserPisRequestValidator().Validate(Update(null)).IsValid);

    [Fact]
    public void ApplyTo_stores_the_trimmed_text_as_sent_and_never_clears_gender_when_omitted()
    {
        UserPis entity = new() { Gender = "Male" };

        Update(null).ApplyTo(entity);
        Assert.Equal("Male", entity.Gender);

        Update("   ").ApplyTo(entity);
        Assert.Equal("Male", entity.Gender);

        Update("  fEmAlE ").ApplyTo(entity);
        Assert.Equal("fEmAlE", entity.Gender);   // not re-spelled
    }

    [Fact]
    public void Responses_return_the_stored_text_unchanged()
    {
        UserPis stored = new() { Id = 1, NsgNumber = "N-1", ServiceNumber = "S-1", Gender = "M" };
        UserPis legacy = new() { Id = 2, NsgNumber = "N-2", ServiceNumber = "S-2" };

        Assert.Equal("M", stored.ToListItem().Gender);
        Assert.Equal("M", stored.ToResponse(_ => null).Gender);
        Assert.Null(legacy.ToListItem().Gender);
        Assert.Null(legacy.ToResponse(_ => null).Gender);
    }

    [Fact]
    public void Gender_is_plain_json_text_in_requests_and_responses()
    {
        JsonSerializerOptions web = new(JsonSerializerDefaults.Web);
        UserPis stored = new() { Id = 1, NsgNumber = "N-1", ServiceNumber = "S-1", Gender = "Other" };

        Assert.Contains("\"gender\":\"Other\"", JsonSerializer.Serialize(stored.ToResponse(_ => null), web));
        Assert.Equal("Female", JsonSerializer.Deserialize<CreateUserPisRequestDto>("{\"gender\":\"Female\"}", web)!.Gender);
    }
}
