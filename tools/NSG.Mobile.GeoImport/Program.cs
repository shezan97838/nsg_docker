using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Infrastructure.Geography;
using NSG.Mobile.Infrastructure.Persistence;

// NSG geography import tool.
//   import   --states <csv> [--cities <csv>] [--dry-run] [--allow-rejects] [--report <file>]
//   validate [--expected-states 28] [--expected-uts 8]
//   prepare-census --xlsx <PC11_TV_DIR.xlsx> --lgd-states <csv> --out <cities csv> --rejected <csv> --raw <tsv> --report <txt> [--a01 <xlsx>]
//                  (offline: builds the cities CSV from the Census 2011 Location Code Directory; needs no database)
// The connection string is read ONLY from the environment variable ConnectionStrings__MySql.

const string ConnectionVariable = "ConnectionStrings__MySql";

if (args.Length > 0 && args[0] == "prepare-census")
{
    string? A(string name) => Array.IndexOf(args, name) is int i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    string?[] required = { A("--xlsx"), A("--lgd-states"), A("--out"), A("--rejected"), A("--raw"), A("--report") };
    if (required.Any(string.IsNullOrWhiteSpace)) { Console.Error.WriteLine("prepare-census requires --xlsx --lgd-states --out --rejected --raw --report"); return 1; }
    return NSG.Mobile.GeoImport.CensusPreparer.Run(required[0]!, required[1]!, required[2]!, required[3]!, required[4]!, required[5]!, A("--a01"));
}

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage:\n  import   --states <csv> [--cities <csv>] [--dry-run] [--allow-rejects] [--report <file>]\n  validate [--expected-states 28] [--expected-uts 8]\nSet " + ConnectionVariable + " to the MySQL connection string.");
    return 1;
}

string? connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Environment variable {ConnectionVariable} is not set.");
    return 2;
}

string? Arg(string name) => Array.IndexOf(args, name) is int i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
bool Flag(string name) => args.Contains(name);

DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
    .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), my => my.EnableRetryOnFailure(3))
    .Options;

await using AppDbContext context = new(options);

switch (args[0])
{
    case "import":
    {
        string? states = Arg("--states");
        if (states == null) { Console.Error.WriteLine("--states is required."); return 1; }

        GeoImportReport report = await new GeoImporter(context)
            .ImportAsync(states, Arg("--cities"), Flag("--dry-run"), abortOnReject: !Flag("--allow-rejects"));

        string text = report.ToString();
        Console.WriteLine(text);
        if (Arg("--report") is string path) await File.WriteAllTextAsync(path, text);
        return report.Succeeded ? 0 : 3;
    }
    case "validate":
    {
        int es = int.TryParse(Arg("--expected-states"), out int a) ? a : 28;
        int eu = int.TryParse(Arg("--expected-uts"), out int b) ? b : 8;
        GeoValidationReport report = await new GeoValidator(context).ValidateAsync(es, eu);
        Console.WriteLine(report);
        return report.IsValid ? 0 : 4;
    }
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        return 1;
}
