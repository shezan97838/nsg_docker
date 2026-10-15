using System.ComponentModel.DataAnnotations;
using System.Reflection;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>Pins the numeric values and labels of the PIS enums; the numbers are persisted as ints and must never change.</summary>
public class PisEnumTests
{
    [Fact]
    public void PersonnelCategory_has_exact_names_and_values()
    {
        Assert.Equal(
            new Dictionary<string, int> { ["GEN"] = 1, ["SC"] = 2, ["ST"] = 3, ["OBC"] = 4 },
            Enum.GetNames<PersonnelCategory>().ToDictionary(n => n, n => (int)Enum.Parse<PersonnelCategory>(n)));
    }

    [Fact]
    public void BloodGroupType_has_exact_names_and_values()
    {
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["APositive"] = 1, ["ANegative"] = 2, ["BPositive"] = 3, ["BNegative"] = 4,
                ["ABPositive"] = 5, ["ABNegative"] = 6, ["OPositive"] = 7, ["ONegative"] = 8
            },
            Enum.GetNames<BloodGroupType>().ToDictionary(n => n, n => (int)Enum.Parse<BloodGroupType>(n)));
    }

    [Theory]
    [InlineData(BloodGroupType.APositive, "A+")]
    [InlineData(BloodGroupType.ANegative, "A-")]
    [InlineData(BloodGroupType.BPositive, "B+")]
    [InlineData(BloodGroupType.BNegative, "B-")]
    [InlineData(BloodGroupType.ABPositive, "AB+")]
    [InlineData(BloodGroupType.ABNegative, "AB-")]
    [InlineData(BloodGroupType.OPositive, "O+")]
    [InlineData(BloodGroupType.ONegative, "O-")]
    public void BloodGroupType_display_names_match_the_conventional_labels(BloodGroupType value, string expected)
    {
        DisplayAttribute? display = typeof(BloodGroupType).GetField(value.ToString())!.GetCustomAttribute<DisplayAttribute>();

        Assert.NotNull(display);
        Assert.Equal(expected, display!.Name);
    }

    [Fact]
    public void BloodGroupType_display_names_are_unique()
    {
        string?[] names = Enum.GetNames<BloodGroupType>()
            .Select(n => typeof(BloodGroupType).GetField(n)!.GetCustomAttribute<DisplayAttribute>()?.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
    }
}
