using System.Text;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Geography;

/// <summary>Outcome counters and per-row messages for one imported file.</summary>
public class GeoImportEntityReport
{
    public int Read { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Rejected { get; set; }
    public int Failed { get; set; }
    public List<string> Messages { get; } = new();
}

/// <summary>Result of a geography import run.</summary>
public class GeoImportReport
{
    public GeoImportEntityReport States { get; } = new();
    public GeoImportEntityReport Cities { get; } = new();
    public bool DryRun { get; set; }
    public bool Committed { get; set; }
    public string? FatalError { get; set; }

    public bool Succeeded => Committed || (DryRun && FatalError == null);

    public override string ToString()
    {
        StringBuilder sb = new();
        sb.AppendLine($"Mode: {(DryRun ? "DRY-RUN (rolled back)" : "COMMIT")}   Committed: {Committed}");
        AppendEntity(sb, "States/UTs", States);
        AppendEntity(sb, "Cities", Cities);
        if (FatalError != null) sb.AppendLine($"FATAL: {FatalError}");
        return sb.ToString();
    }

    private static void AppendEntity(StringBuilder sb, string label, GeoImportEntityReport r)
    {
        sb.AppendLine($"{label}: read={r.Read} inserted={r.Inserted} updated={r.Updated} skipped(unchanged)={r.Skipped} rejected={r.Rejected} failed={r.Failed}");
        foreach (string m in r.Messages) sb.AppendLine($"  - {m}");
    }
}

/// <summary>
/// Repeatable, idempotent importer for States/UTs and cities from versioned CSV files.
/// Upsert only — never deletes. All changes are applied in a single transaction.
/// </summary>
public class GeoImporter
{
    public static readonly string[] StateColumns = { "Code", "Name", "AdministrativeType" };
    public static readonly string[] CityColumns = { "StateCode", "Name", "SourceSystem", "SourceKey" };

    private readonly AppDbContext _context;

    public GeoImporter(AppDbContext context) => _context = context;

    /// <param name="statesCsv">Path to states CSV (required columns: Code, Name, AdministrativeType).</param>
    /// <param name="citiesCsv">Optional path to cities CSV (required: StateCode, Name, SourceSystem, SourceKey; optional: Code, Classification).</param>
    /// <param name="dryRun">Run everything, then roll back.</param>
    /// <param name="abortOnReject">If true (default) any rejected row aborts the whole import with no changes.</param>
    public async Task<GeoImportReport> ImportAsync(
        string statesCsv, string? citiesCsv, bool dryRun, bool abortOnReject = true, CancellationToken cancellationToken = default)
    {
        GeoImportReport report = new() { DryRun = dryRun };

        List<Dictionary<string, string>> stateRows;
        List<Dictionary<string, string>>? cityRows = null;
        try
        {
            stateRows = CsvFile.Read(statesCsv, StateColumns);
            if (citiesCsv != null) cityRows = CsvFile.Read(citiesCsv, CityColumns);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
        {
            report.FatalError = ex.Message;
            return report;
        }

        report.States.Read = stateRows.Count;
        report.Cities.Read = cityRows?.Count ?? 0;

        List<State> validStates = ValidateStates(stateRows, report.States);
        List<CityRow> validCities = cityRows == null
            ? new List<CityRow>()
            : ValidateCities(cityRows, validStates.Select(s => s.Code!).ToHashSet(StringComparer.OrdinalIgnoreCase), report.Cities);

        if (abortOnReject && (report.States.Rejected > 0 || report.Cities.Rejected > 0))
        {
            report.FatalError = "Rejected rows found and abortOnReject is set; no changes were made.";
            return report;
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                report.States.Inserted = report.States.Updated = report.States.Skipped = 0;
                report.Cities.Inserted = report.Cities.Updated = report.Cities.Skipped = 0;

                await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

                Dictionary<string, State> stateByCode = await UpsertStatesAsync(validStates, report.States, cancellationToken);
                await UpsertCitiesAsync(validCities, stateByCode, report.Cities, cancellationToken);

                if (dryRun)
                {
                    await tx.RollbackAsync(cancellationToken);
                }
                else
                {
                    await tx.CommitAsync(cancellationToken);
                    report.Committed = true;
                }
            });
        }
        catch (Exception ex)
        {
            // The transaction is rolled back by disposal. Surface the failure without leaking internals beyond the message.
            report.FatalError = $"Import failed and was rolled back: {ex.GetType().Name}: {ex.Message}";
            report.States.Failed = report.States.Read - report.States.Rejected;
            report.Cities.Failed = report.Cities.Read - report.Cities.Rejected;
            report.States.Inserted = report.States.Updated = report.States.Skipped = 0;
            report.Cities.Inserted = report.Cities.Updated = report.Cities.Skipped = 0;
            report.Committed = false;
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }

        return report;
    }

    private static List<State> ValidateStates(List<Dictionary<string, string>> rows, GeoImportEntityReport report)
    {
        List<State> valid = new();
        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rows.Count; i++)
        {
            int line = i + 2;
            Dictionary<string, string> r = rows[i];
            string code = r["Code"].Trim(), name = r["Name"].Trim();

            string? error = null;
            if (!System.Text.RegularExpressions.Regex.IsMatch(code, @"^[0-9]{2}$")) error = $"Code '{code}' must be a two-digit official code (leading zeros kept).";
            else if (name.Length == 0 || name.Length > 100) error = "Name is required (max 100).";
            else if (!Enum.TryParse(r["AdministrativeType"].Trim(), true, out AdministrativeUnitType type) || !Enum.IsDefined(type))
                error = $"Invalid AdministrativeType '{r["AdministrativeType"]}'.";
            else if (!codes.Add(code)) error = $"Duplicate state code '{code}' in file.";
            else if (!names.Add(name)) error = $"Duplicate state name '{name}' in file.";

            if (error != null)
            {
                report.Rejected++;
                report.Messages.Add($"states line {line}: {error}");
                continue;
            }

            valid.Add(new State
            {
                Code = code,
                Name = name,
                AdministrativeType = Enum.Parse<AdministrativeUnitType>(r["AdministrativeType"].Trim(), true)
            });
        }

        return valid;
    }

    private sealed record CityRow(string StateCode, string Name, string SourceSystem, string SourceKey, string? Code, TownClassification? Classification);

    private static List<CityRow> ValidateCities(List<Dictionary<string, string>> rows, HashSet<string> stateCodes, GeoImportEntityReport report)
    {
        List<CityRow> valid = new();
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rows.Count; i++)
        {
            int line = i + 2;
            Dictionary<string, string> r = rows[i];
            string stateCode = r["StateCode"].Trim(), name = r["Name"].Trim();
            string system = r["SourceSystem"].Trim(), key = r["SourceKey"].Trim();
            string? code = r.TryGetValue("Code", out string? c) && c.Trim().Length > 0 ? c.Trim() : null;
            string classText = r.TryGetValue("Classification", out string? ct) ? ct.Trim() : "";
            TownClassification? classification = null;

            string? error = null;
            if (name.Length == 0 || name.Length > 200) error = "Name is required (max 200).";
            else if (system.Length == 0 || system.Length > 50 || key.Length == 0 || key.Length > 250) error = "SourceSystem (max 50) and SourceKey (max 250) are required.";
            else if (code is { Length: > 50 }) error = "Code max length is 50.";
            else if (!stateCodes.Contains(stateCode)) error = $"Unknown StateCode '{stateCode}'.";
            else if (classText.Length > 0)
            {
                if (Enum.TryParse(classText, true, out TownClassification parsed) && Enum.IsDefined(parsed)) classification = parsed;
                else error = $"Invalid Classification '{classText}'.";
            }
            if (error == null && !keys.Add($"{system}|{key}")) error = $"Duplicate SourceSystem/SourceKey '{system}|{key}' in file.";

            if (error != null)
            {
                report.Rejected++;
                report.Messages.Add($"cities line {line}: {error}");
                continue;
            }

            valid.Add(new CityRow(stateCode, name, system, key, code, classification));
        }

        return valid;
    }

    private async Task<Dictionary<string, State>> UpsertStatesAsync(List<State> incoming, GeoImportEntityReport report, CancellationToken ct)
    {
        List<State> existing = await _context.States.ToListAsync(ct);
        Dictionary<string, State> byCode = existing.Where(s => s.Code != null)
            .ToDictionary(s => s.Code!, StringComparer.OrdinalIgnoreCase);
        DateTime now = IndianStandardTime.Now;

        foreach (State s in incoming)
        {
            if (byCode.TryGetValue(s.Code!, out State? current))
            {
                if (current.Name == s.Name && current.AdministrativeType == s.AdministrativeType)
                {
                    report.Skipped++;
                    continue;
                }
                current.Name = s.Name;
                current.AdministrativeType = s.AdministrativeType;
                current.UpdatedAtIst = now;
                report.Updated++;
            }
            else
            {
                s.CreatedAtIst = now;
                s.IsActive = true;
                _context.States.Add(s);
                byCode[s.Code!] = s;
                report.Inserted++;
            }
        }

        await _context.SaveChangesAsync(ct);
        return byCode;
    }

    private async Task UpsertCitiesAsync(List<CityRow> incoming, Dictionary<string, State> stateByCode, GeoImportEntityReport report, CancellationToken ct)
    {
        if (incoming.Count == 0) return;

        HashSet<string> systems = incoming.Select(c => c.SourceSystem).ToHashSet();
        List<City> existing = await _context.Cities.Where(c => c.SourceSystem != null && systems.Contains(c.SourceSystem)).ToListAsync(ct);
        Dictionary<string, City> byKey = existing.ToDictionary(c => $"{c.SourceSystem}|{c.SourceKey}", StringComparer.OrdinalIgnoreCase);
        DateTime now = IndianStandardTime.Now;

        foreach (CityRow row in incoming)
        {
            int stateId = stateByCode[row.StateCode].Id;
            string key = $"{row.SourceSystem}|{row.SourceKey}";

            if (byKey.TryGetValue(key, out City? current))
            {
                if (current.Name == row.Name && current.StateId == stateId && current.Code == row.Code && current.Classification == row.Classification)
                {
                    report.Skipped++;
                    continue;
                }
                current.Name = row.Name;
                current.StateId = stateId;
                current.Code = row.Code;
                current.Classification = row.Classification;
                current.UpdatedAtIst = now;
                report.Updated++;
            }
            else
            {
                _context.Cities.Add(new City
                {
                    Name = row.Name,
                    StateId = stateId,
                    Code = row.Code,
                    Classification = row.Classification,
                    SourceSystem = row.SourceSystem,
                    SourceKey = row.SourceKey,
                    IsActive = true,
                    CreatedAtIst = now
                });
                report.Inserted++;
            }
        }

        await _context.SaveChangesAsync(ct);
    }
}

/// <summary>Minimal RFC 4180 CSV reader (quoted fields, escaped quotes, UTF-8 with optional BOM).</summary>
internal static class CsvFile
{
    public static List<Dictionary<string, string>> Read(string path, string[] requiredColumns)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"File not found: {path}");

        using StreamReader reader = new(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        List<List<string>> records = Parse(text);
        if (records.Count == 0) throw new InvalidDataException($"{Path.GetFileName(path)} is empty.");

        List<string> header = records[0].Select(h => h.Trim()).ToList();
        string[] missing = requiredColumns.Where(c => !header.Contains(c, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"{Path.GetFileName(path)} is missing required column(s): {string.Join(", ", missing)}.");

        List<Dictionary<string, string>> rows = new();
        foreach (List<string> rec in records.Skip(1))
        {
            if (rec.Count == 1 && rec[0].Length == 0) continue; // blank line
            Dictionary<string, string> row = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++) row[header[i]] = i < rec.Count ? rec[i] : "";
            rows.Add(row);
        }
        return rows;
    }

    private static List<List<string>> Parse(string text)
    {
        List<List<string>> records = new();
        List<string> fields = new();
        StringBuilder sb = new();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else if (ch == '\r') { /* handled with \n */ }
            else if (ch == '\n') { fields.Add(sb.ToString()); sb.Clear(); records.Add(fields); fields = new List<string>(); }
            else sb.Append(ch);
        }

        if (inQuotes) throw new InvalidDataException("CSV has an unterminated quoted field.");
        if (sb.Length > 0 || fields.Count > 0) { fields.Add(sb.ToString()); records.Add(fields); }
        return records;
    }
}
