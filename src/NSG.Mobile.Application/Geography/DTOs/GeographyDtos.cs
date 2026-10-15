using System.Text.Json.Serialization;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Geography.DTOs;

/// <summary>State or Union Territory returned by the API.</summary>
public class StateResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdministrativeUnitType AdministrativeType { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAtIst { get; set; }
    public DateTime? UpdatedAtIst { get; set; }
}

/// <summary>Request to create a State/Union Territory.</summary>
public class CreateStateRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdministrativeUnitType AdministrativeType { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Request to update a State/Union Territory.</summary>
public class UpdateStateRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AdministrativeUnitType AdministrativeType { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>City/town returned by the API.</summary>
public class CityResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int StateId { get; set; }
    public string StateName { get; set; } = string.Empty;
    public string? Code { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TownClassification? Classification { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAtIst { get; set; }
    public DateTime? UpdatedAtIst { get; set; }
}

/// <summary>Request to create a city/town.</summary>
public class CreateCityRequestDto
{
    public string Name { get; set; } = string.Empty;
    public int StateId { get; set; }
    public string? Code { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TownClassification? Classification { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Request to update a city/town.</summary>
public class UpdateCityRequestDto
{
    public string Name { get; set; } = string.Empty;
    public int StateId { get; set; }
    public string? Code { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TownClassification? Classification { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>Query parameters for listing states/UTs.</summary>
public class StateListQuery
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public AdministrativeUnitType? AdministrativeType { get; set; }
    public bool? IsActive { get; set; }

    /// <summary>One of: name, code, createdAt. Defaults to name.</summary>
    public string? SortBy { get; set; }

    /// <summary>"asc" (default) or "desc".</summary>
    public string? SortDirection { get; set; }
}

/// <summary>Query parameters for listing cities/towns.</summary>
public class CityListQuery
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public int? StateId { get; set; }
    public TownClassification? Classification { get; set; }
    public bool? IsActive { get; set; }

    /// <summary>One of: name, createdAt. Defaults to name.</summary>
    public string? SortBy { get; set; }

    /// <summary>"asc" (default) or "desc".</summary>
    public string? SortDirection { get; set; }
}
