using System.Text.Json;
using System.Text.RegularExpressions;

namespace NSG.Mobile.Tests;

/// <summary>
/// Guards against real credentials reaching committed configuration. Files scanned are the ones that are tracked by git
/// (appsettings.json, appsettings.Development.json, launchSettings.json); real values belong in user-secrets, environment
/// variables or a vault. Failure messages name the file and JSON path only - never the value.
/// </summary>
public class ConfigurationSafetyTests
{
    private const string PlaceholderPrefix = "REPLACE_";
    private static readonly Regex SecretName = new("secret|key|password|pwd|token|credential", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NSG.Mobile.API.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (NSG.Mobile.API.sln) not found.");
    }

    private static IEnumerable<string> TrackedConfigFiles()
    {
        string root = RepositoryRoot();
        return new[] { "src", "tools", "tests" }
            .Select(d => Path.Combine(root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.json", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(f).Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns "file :: json.path :: reason" entries (never values) for every non-placeholder secret.</summary>
    internal static List<string> FindViolations(string fileLabel, string json)
    {
        List<string> violations = new();
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        Walk(doc.RootElement, string.Empty);
        return violations;

        void Walk(JsonElement element, string path)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty p in element.EnumerateObject())
                        Walk(p.Value, path.Length == 0 ? p.Name : $"{path}:{p.Name}");
                    break;
                case JsonValueKind.Array:
                    int i = 0;
                    foreach (JsonElement item in element.EnumerateArray())
                        Walk(item, $"{path}[{i++}]");
                    break;
                case JsonValueKind.String:
                    Check(path, element.GetString() ?? string.Empty);
                    break;
            }
        }

        void Check(string path, string value)
        {
            string leaf = path.Split(':').Last();
            bool placeholderOrEmpty = value.Length == 0 || value.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);

            if (value.Contains("-----BEGIN", StringComparison.Ordinal))
                violations.Add($"{fileLabel} :: {path} :: contains a private key / certificate block");

            if (Regex.IsMatch(value, "(pwd|password)=", RegexOptions.IgnoreCase))
            {
                string? password = Regex.Match(value, "(?:pwd|password)=([^;]*)", RegexOptions.IgnoreCase).Groups[1].Value;
                if (!password.StartsWith(PlaceholderPrefix, StringComparison.Ordinal))
                    violations.Add($"{fileLabel} :: {path} :: connection string contains a real password");
                string user = Regex.Match(value, "(?:uid|user id|user)=([^;]*)", RegexOptions.IgnoreCase).Groups[1].Value;
                if (user.Equals("root", StringComparison.OrdinalIgnoreCase))
                    violations.Add($"{fileLabel} :: {path} :: connection string uses the root account");
            }
            else if (SecretName.IsMatch(leaf) && !placeholderOrEmpty)
            {
                violations.Add($"{fileLabel} :: {path} :: value is not empty or a {PlaceholderPrefix}* placeholder");
            }
        }
    }

    [Fact]
    public void Committed_configuration_files_contain_only_placeholders_for_secrets()
    {
        List<string> files = TrackedConfigFiles().ToList();
        Assert.Contains(files, f => Path.GetFileName(f) == "appsettings.json");   // the scan really found the files

        List<string> violations = files
            .SelectMany(f => FindViolations(Path.GetRelativePath(RepositoryRoot(), f), File.ReadAllText(f)))
            .ToList();

        Assert.True(violations.Count == 0, "Real secrets found in committed configuration:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Scanner_flags_real_looking_secrets_without_revealing_them()
    {
        const string leakedPassword = "Sup3r-S3cret-Value", leakedKey = "dGhpcyBpcyBhIGZha2Uga2V5IGZvciB0ZXN0aW5nIG9ubHk=";
        string json = $$"""
            {
              "ConnectionStrings": { "MySql": "Server=h;Uid=root;Pwd={{leakedPassword}};" },
              "Jwt": { "Secret": "{{leakedKey}}" },
              "Encryption": { "Key": "REPLACE_WITH_BASE64_32_BYTE_KEY" },
              "Sms": { "ApiKey": "" },
              "Logging": { "Level": "Debug" }
            }
            """;

        List<string> violations = FindViolations("sample.json", json);

        Assert.Equal(3, violations.Count);   // real password, root account, Jwt:Secret - placeholder and empty values pass
        Assert.Contains(violations, v => v.Contains("ConnectionStrings:MySql") && v.Contains("real password"));
        Assert.Contains(violations, v => v.Contains("root account"));
        Assert.Contains(violations, v => v.Contains("Jwt:Secret"));
        Assert.All(violations, v =>
        {
            Assert.DoesNotContain(leakedPassword, v);
            Assert.DoesNotContain(leakedKey, v);
        });
    }

    [Fact]
    public void Scanner_accepts_the_documented_placeholder_connection_string()
        => Assert.Empty(FindViolations("sample.json",
            "{ \"ConnectionStrings\": { \"MySql\": \"Server=REPLACE_WITH_DB_HOST;Uid=REPLACE_WITH_DB_USER;Pwd=REPLACE_WITH_DB_PASSWORD;\" } }"));

    [Fact]
    public void Gitignore_covers_local_secret_files_but_not_the_templates()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), ".gitignore")).Select(l => l.Trim()).ToArray();

        foreach (string required in new[] { "appsettings.Production.json", "appsettings.Staging.json", "appsettings.Local.json",
                     "appsettings.*.local.json", "**/secrets.json", "**/UserSecrets/", ".env", ".env.*", "*.pfx", "*.pem", "*.key" })
            Assert.Contains(required, lines);

        Assert.DoesNotContain("appsettings.json", lines);
        Assert.DoesNotContain("appsettings.Development.json", lines);   // tracked, but guarded to be secret-free above
        Assert.Contains("!*.env.example", lines);
    }

    [Fact]
    public void Api_project_declares_a_user_secrets_id_for_local_development()
    {
        string csproj = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NSG.Mobile.API", "NSG.Mobile.API.csproj"));
        Assert.Matches("<UserSecretsId>[0-9a-fA-F-]{36}</UserSecretsId>", csproj);
    }

    [Fact]
    public void Base_configuration_does_not_embed_a_usable_database_account()
    {
        string json = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NSG.Mobile.API", "appsettings.json"));
        using JsonDocument doc = JsonDocument.Parse(json);
        string connection = doc.RootElement.GetProperty("ConnectionStrings").GetProperty("MySql").GetString()!;

        Assert.Contains("REPLACE_WITH_DB_HOST", connection);
        Assert.Contains("REPLACE_WITH_DB_USER", connection);
        Assert.Contains("REPLACE_WITH_DB_PASSWORD", connection);
        Assert.DoesNotContain("127.0.0.1", connection);
        Assert.DoesNotContain("SslMode=None", connection, StringComparison.OrdinalIgnoreCase);
    }
}
