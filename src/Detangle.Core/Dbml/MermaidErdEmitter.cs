using System.Text;

namespace Detangle.Core.Dbml;

/// <summary>
/// Emits a Mermaid <c>erDiagram</c> from a parsed DBML schema.
/// <para>
/// The translation is lossy and that is handled explicitly rather than hidden (plan.md
/// section 4.2). Cardinality maps cleanly; enums, table groups, index definitions,
/// colours, sticky notes and default values have no <c>erDiagram</c> equivalent at all.
/// Rather than mangling them into the picture, they are left here and shown in the
/// table-detail panel beside it — <see cref="UnrepresentedFeatures"/> lists exactly what
/// the diagram is not saying.
/// </para>
/// </summary>
public static class MermaidErdEmitter
{
    /// <summary>Renders a schema as Mermaid ER diagram source.</summary>
    public static string Emit(DbmlSchema schema)
    {
        var builder = new StringBuilder();
        builder.Append("erDiagram\n");

        foreach (DbmlTable table in schema.Tables)
        {
            builder.Append("    ").Append(Identifier(table.ReferenceName)).Append(" {\n");

            foreach (DbmlColumn column in table.Columns)
            {
                // Mermaid's grammar takes "type name key comment" and accepts only PK, FK
                // and UK as keys, so everything else about a column lives in the panel.
                builder.Append("        ")
                    .Append(Identifier(TypeOf(column)))
                    .Append(' ')
                    .Append(Identifier(column.Name));

                string key = KeyOf(column, table, schema);

                if (key.Length > 0)
                {
                    builder.Append(' ').Append(key);
                }

                if (column.Note is { Length: > 0 } note)
                {
                    builder.Append(" \"").Append(Escape(note)).Append('"');
                }

                builder.Append('\n');
            }

            builder.Append("    }\n");
        }

        foreach (DbmlRelationship relationship in schema.Relationships)
        {
            string label = relationship.Name is { Length: > 0 } name
                ? Escape(name)
                : string.Join(", ", relationship.From.Columns);

            builder.Append("    ")
                .Append(Identifier(Resolve(schema, relationship.From.Table)))
                .Append(' ')
                .Append(SymbolFor(relationship.Cardinality))
                .Append(' ')
                .Append(Identifier(Resolve(schema, relationship.To.Table)))
                .Append(" : \"")
                .Append(label.Length == 0 ? "references" : label)
                .Append("\"\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Names the parts of the schema the diagram cannot express, so the reader can be
    /// told rather than left to wonder where they went.
    /// </summary>
    public static IReadOnlyList<string> UnrepresentedFeatures(DbmlSchema schema)
    {
        var missing = new List<string>();

        if (schema.Enums.Count > 0)
        {
            missing.Add($"{schema.Enums.Count} enum{(schema.Enums.Count == 1 ? string.Empty : "s")}");
        }

        if (schema.TableGroups.Count > 0)
        {
            missing.Add($"{schema.TableGroups.Count} table group{(schema.TableGroups.Count == 1 ? string.Empty : "s")}");
        }

        int indexes = schema.Tables.Sum(t => t.Indexes.Count);

        if (indexes > 0)
        {
            missing.Add($"{indexes} index definition{(indexes == 1 ? string.Empty : "s")}");
        }

        int defaults = schema.Tables.Sum(t => t.Columns.Count(c => c.Default is not null));

        if (defaults > 0)
        {
            missing.Add($"{defaults} default value{(defaults == 1 ? string.Empty : "s")}");
        }

        if (schema.Notes.Count > 0)
        {
            missing.Add($"{schema.Notes.Count} sticky note{(schema.Notes.Count == 1 ? string.Empty : "s")}");
        }

        if (schema.Tables.Any(t => t.HeaderColor is not null))
        {
            missing.Add("table colours");
        }

        return missing;
    }

    /// <summary>
    /// Maps DBML cardinality onto Mermaid's crow's-foot notation. "&gt;" means many rows on
    /// the left refer to one on the right, which Mermaid writes as "}o--||". Public because
    /// the mapping is the whole of the lossy-translation contract and is asserted directly.
    /// </summary>
    public static string SymbolFor(DbmlCardinality cardinality) => cardinality switch
    {
        DbmlCardinality.ManyToOne => "}o--||",
        DbmlCardinality.OneToMany => "||--o{",
        DbmlCardinality.ManyToMany => "}o--o{",
        _ => "||--||",
    };

    private static string KeyOf(DbmlColumn column, DbmlTable table, DbmlSchema schema)
    {
        if (column.IsPrimaryKey || table.Indexes.Any(i => i.IsPrimaryKey && i.Columns.Contains(column.Name)))
        {
            return "PK";
        }

        bool isForeignKey = schema.Relationships.Any(r =>
            (string.Equals(r.From.Table, table.ReferenceName, StringComparison.OrdinalIgnoreCase)
                && r.From.Columns.Contains(column.Name, StringComparer.OrdinalIgnoreCase))
            || (string.Equals(r.To.Table, table.ReferenceName, StringComparison.OrdinalIgnoreCase)
                && r.To.Columns.Contains(column.Name, StringComparer.OrdinalIgnoreCase)));

        if (isForeignKey)
        {
            return "FK";
        }

        return column.IsUnique ? "UK" : string.Empty;
    }

    private static string TypeOf(DbmlColumn column) =>
        column.Type.Length == 0 ? "unknown" : column.Type;

    private static string Resolve(DbmlSchema schema, string tableName) =>
        schema.FindTable(tableName)?.ReferenceName ?? tableName;

    /// <summary>
    /// Mermaid identifiers cannot hold spaces, parentheses or commas, and a "varchar(255)"
    /// type would break its parser outright.
    /// </summary>
    private static string Identifier(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        }

        string identifier = builder.ToString().Trim('_');

        return identifier.Length == 0 ? "unnamed" : identifier;
    }

    private static string Escape(string value) =>
        value.Replace("\"", "'", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
}
