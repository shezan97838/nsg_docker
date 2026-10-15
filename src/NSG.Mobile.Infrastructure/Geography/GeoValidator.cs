using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Geography;

/// <summary>Post-import database validation report.</summary>
public class GeoValidationReport
{
    public int StateCount { get; set; }
    public int UnionTerritoryCount { get; set; }
    public int CityCount { get; set; }
    public int OrphanCities { get; set; }
    public int DuplicateStateCodes { get; set; }
    public int DuplicateCitySourceKeys { get; set; }
    public List<string> Problems { get; } = new();

    public bool IsValid => Problems.Count == 0;

    public override string ToString()
        => $"States={StateCount} UnionTerritories={UnionTerritoryCount} Cities={CityCount} OrphanCities={OrphanCities} " +
           $"DuplicateStateCodes={DuplicateStateCodes} DuplicateCitySourceKeys={DuplicateCitySourceKeys}\n" +
           (IsValid ? "VALID" : "INVALID:\n  - " + string.Join("\n  - ", Problems));
}

/// <summary>Verifies state/UT coverage and referential integrity of the geography tables.</summary>
public class GeoValidator
{
    private readonly AppDbContext _context;

    public GeoValidator(AppDbContext context) => _context = context;

    public async Task<GeoValidationReport> ValidateAsync(int expectedStates, int expectedUnionTerritories, CancellationToken cancellationToken = default)
    {
        GeoValidationReport report = new()
        {
            StateCount = await _context.States.CountAsync(s => s.AdministrativeType == AdministrativeUnitType.State, cancellationToken),
            UnionTerritoryCount = await _context.States.CountAsync(s => s.AdministrativeType == AdministrativeUnitType.UnionTerritory, cancellationToken),
            CityCount = await _context.Cities.CountAsync(cancellationToken),
            OrphanCities = await _context.Cities.CountAsync(c => !_context.States.Any(s => s.Id == c.StateId), cancellationToken),
            DuplicateStateCodes = await _context.States.Where(s => s.Code != null).GroupBy(s => s.Code).CountAsync(g => g.Count() > 1, cancellationToken),
            DuplicateCitySourceKeys = await _context.Cities.Where(c => c.SourceKey != null).GroupBy(c => new { c.SourceSystem, c.SourceKey }).CountAsync(g => g.Count() > 1, cancellationToken)
        };

        if (report.StateCount != expectedStates) report.Problems.Add($"Expected {expectedStates} states but found {report.StateCount}.");
        if (report.UnionTerritoryCount != expectedUnionTerritories) report.Problems.Add($"Expected {expectedUnionTerritories} union territories but found {report.UnionTerritoryCount}.");
        if (report.OrphanCities > 0) report.Problems.Add($"{report.OrphanCities} cities reference a missing state.");
        if (report.DuplicateStateCodes > 0) report.Problems.Add($"{report.DuplicateStateCodes} duplicated state codes.");
        if (report.DuplicateCitySourceKeys > 0) report.Problems.Add($"{report.DuplicateCitySourceKeys} duplicated city source keys.");

        return report;
    }
}
