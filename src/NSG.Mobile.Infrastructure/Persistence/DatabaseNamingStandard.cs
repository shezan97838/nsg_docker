using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace NSG.Mobile.Infrastructure.Persistence;

/// <summary>
/// Enforces the project's MySQL naming standard (docs/DATABASE-STANDARDS.md) on the EF model: every table, column, key, index
/// and foreign key is lowercase snake_case, so the same SQL works on Linux (case-sensitive table names) and Windows.
/// <list type="bullet">
/// <item><b>Tables</b> are never derived: each entity configuration must call <c>ToTable("snake_case_name")</c>. A missing or
/// non-compliant table name stops model building, so a mistake fails immediately instead of reaching a database.</item>
/// <item><b>Columns</b> are mapped from the C# property name by <see cref="ToSnakeCase"/> unless the configuration set a name
/// explicitly with <c>HasColumnName</c>. IST timestamps (<c>DateTime</c> properties) always end in <c>_ist</c>.</item>
/// <item><b>Keys, indexes and foreign keys</b> get <c>pk_</c>, <c>ix_</c> and <c>fk_</c> names derived from the final table and
/// column names.</item>
/// </list>
/// The complete resulting schema is pinned by <c>DatabaseNamingRuleTests</c>.
/// </summary>
public static class DatabaseNamingStandard
{
    /// <summary>MySQL's maximum length for table, column, index and constraint names.</summary>
    public const int MaxIdentifierLength = 64;

    private static readonly Regex SnakeCasePattern = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// "UsersPIS" → "users_pis", "NsgTradeId" → "nsg_trade_id", "CreatedAtIst" → "created_at_ist",
    /// "IX_UsersPIS_NsgNumber" → "ix_users_pis_nsg_number".
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        string snake = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1_$2", RegexOptions.CultureInvariant);
        snake = Regex.Replace(snake, "([A-Z]+)([A-Z][a-z])", "$1_$2", RegexOptions.CultureInvariant);
        snake = Regex.Replace(snake, "_+", "_", RegexOptions.CultureInvariant);
        return snake.ToLowerInvariant();
    }

    public static bool IsCompliant(string name) => name.Length <= MaxIdentifierLength && SnakeCasePattern.IsMatch(name);

    public static void Apply(ModelBuilder modelBuilder)
    {
        List<IMutableEntityType> entities = modelBuilder.Model.GetEntityTypes().Where(e => e.GetTableName() != null).ToList();

        // Pass 1: tables are validated, columns are named. Everything below depends on the final column names.
        foreach (IMutableEntityType entity in entities)
        {
            string table = entity.GetTableName()!;
            if (!IsCompliant(table))
                throw new InvalidOperationException(
                    $"Entity '{entity.DisplayName()}' maps to table '{table}'. Every table must be mapped explicitly with ToTable(...) " +
                    "to a lowercase snake_case name of at most 64 characters (see docs/DATABASE-STANDARDS.md).");

            foreach (IMutableProperty property in entity.GetProperties())
            {
                if (property.FindAnnotation(RelationalAnnotationNames.ColumnName) == null)
                    property.SetColumnName(ColumnNameFor(property));
            }
        }

        // Pass 2: keys, indexes and foreign keys, named from the final table and column names.
        foreach (IMutableEntityType entity in entities)
        {
            string table = entity.GetTableName()!;
            StoreObjectIdentifier store = StoreObjectIdentifier.Table(table, entity.GetSchema());

            IMutableKey? primaryKey = entity.FindPrimaryKey();
            primaryKey?.SetName(Checked("pk_" + table, entity));

            foreach (IMutableIndex index in entity.GetIndexes())
                index.SetDatabaseName(Checked(ToSnakeCase(index.GetDatabaseName(store)!), entity));

            foreach (IMutableForeignKey foreignKey in entity.GetForeignKeys())
            {
                // A name set explicitly with HasConstraintName is kept (still length- and format-checked): the generated
                // fk_{table}_{principal}_{columns} form exceeds 64 characters between long table names.
                if (((IConventionForeignKey)foreignKey).GetConstraintNameConfigurationSource() == ConfigurationSource.Explicit)
                {
                    foreignKey.SetConstraintName(Checked(foreignKey.GetConstraintName()!, entity));
                    continue;
                }

                string principalTable = foreignKey.PrincipalEntityType.GetTableName()!;
                string columns = string.Join("_", foreignKey.Properties.Select(p => p.GetColumnName(store)));
                foreignKey.SetConstraintName(Checked($"fk_{table}_{principalTable}_{columns}", entity));
            }
        }
    }

    private static string ColumnNameFor(IMutableProperty property)
    {
        string column = ToSnakeCase(property.Name);

        // A DateTime is always an IST instant in this code base (see docs/DATABASE-STANDARDS.md), and the column says so.
        bool isDateTime = (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTime);
        if (isDateTime && !column.EndsWith("_ist", StringComparison.Ordinal))
            column += "_ist";

        return Checked(column, property.DeclaringType);
    }

    private static string Checked(string name, object owner)
    {
        if (!IsCompliant(name))
            throw new InvalidOperationException(
                $"Generated database name '{name}' for '{(owner as IReadOnlyTypeBase)?.DisplayName() ?? owner}' is not valid lowercase snake_case " +
                $"within {MaxIdentifierLength} characters. Set a shorter name explicitly in the entity configuration.");
        return name;
    }
}
