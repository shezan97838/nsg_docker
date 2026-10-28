using System.Text.Json;
using FluentValidation.Results;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Application.Pis.Validators;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>No-database tests for the PIS gender field: enum values, validation, mapping and JSON shape.</summary>
public class PisGenderTests
{
    private static CreateUserPisRequestDto Create(Gender? gender) => new() { NsgNumber = "N-1", ServiceNumber = "S-1", Gender = gender };
    private static UpdateUserPisRequestDto Update(Gender? gender) => new() { ServiceNumber = "S-1", IsActive = true, Gender = gender };

    [Fact]
    public void Gender_has_exactly_the_three_approved_values()
        => Assert.Equal(
            new Dictionary<string, int> { ["Male"] = 1, ["Female"] = 2, ["Other"] = 3 },
            Enum.GetNames<Gender>().ToDictionary(n => n, n => (int)Enum.Parse<Gender>(n)));

    [Theory]
    [InlineData(Gender.Male)]
    [InlineData(Gender.Female)]
    [InlineData(Gender.Other)]
    public void Create_accepts_each_valid_gender(Gender gender)
        => Assert.True(new CreateUserPisRequestValidator().Validate(Create(gender)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(99)]
    public void Create_rejects_missing_or_undefined_gender(int? value)
    {
        ValidationResult result = new CreateUserPisRequestValidator().Validate(Create(value == null ? null : (Gender)value.Value));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateUserPisRequestDto.Gender));
    }

    [Fact]
    public void Update_allows_an_omitted_gender_but_rejects_an_undefined_one()
    {
        Assert.True(new UpdateUserPisRequestValidator().Validate(Update(null)).IsValid);
        Assert.True(new UpdateUserPisRequestValidator().Validate(Update(Gender.Other)).IsValid);
        Assert.Contains(new UpdateUserPisRequestValidator().Validate(Update((Gender)9)).Errors, e => e.PropertyName == nameof(UpdateUserPisRequestDto.Gender));
    }

    [Fact]
    public void ApplyTo_sets_gender_when_supplied_and_never_clears_it_when_omitted()
    {
        UserPis entity = new() { Gender = Gender.Male };

        Update(null).ApplyTo(entity);
        Assert.Equal(Gender.Male, entity.Gender);

        Update(Gender.Female).ApplyTo(entity);
        Assert.Equal(Gender.Female, entity.Gender);
    }

    [Fact]
    public void Responses_expose_gender_including_null_for_legacy_records()
    {
        UserPis stored = new() { Id = 1, NsgNumber = "N-1", ServiceNumber = "S-1", Gender = Gender.Female };
        UserPis legacy = new() { Id = 2, NsgNumber = "N-2", ServiceNumber = "S-2" };

        Assert.Equal(Gender.Female, stored.ToListItem().Gender);
        Assert.Equal(Gender.Female, stored.ToResponse(_ => null).Gender);
        Assert.Null(legacy.ToListItem().Gender);
        Assert.Null(legacy.ToResponse(_ => null).Gender);
    }

    [Fact]
    public void Gender_is_written_as_a_string_and_read_from_a_string_like_the_other_enums()
    {
        UserPis stored = new() { Id = 1, NsgNumber = "N-1", ServiceNumber = "S-1", Gender = Gender.Other };
        string json = JsonSerializer.Serialize(stored.ToResponse(_ => null), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"gender\":\"Other\"", json);
        Assert.Equal(Gender.Female, JsonSerializer.Deserialize<CreateUserPisRequestDto>("{\"gender\":\"Female\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Gender);
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<CreateUserPisRequestDto>("{\"gender\":\"Robot\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
