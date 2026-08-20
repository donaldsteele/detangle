namespace Detangle.Core.Dbml;

/// <summary>Relationship cardinality, as written between the two column references.</summary>
public enum DbmlCardinality
{
    /// <summary>"&gt;" — many to one.</summary>
    ManyToOne,

    /// <summary>"&lt;" — one to many.</summary>
    OneToMany,

    /// <summary>"-" — one to one.</summary>
    OneToOne,

    /// <summary>"&lt;&gt;" — many to many.</summary>
    ManyToMany,
}

/// <summary>A parse or validation problem, with the position that caused it.</summary>
/// <param name="Message">What went wrong, in the reader's terms.</param>
/// <param name="Line">1-based line.</param>
/// <param name="Column">1-based column.</param>
/// <param name="SourceLine">The offending source line, for the inline error card.</param>
public sealed record DbmlDiagnostic(string Message, int Line, int Column, string SourceLine)
{
    /// <inheritdoc />
    public override string ToString() => $"{Line}:{Column}: {Message}";
}

/// <summary>One column of a table.</summary>
public sealed record DbmlColumn
{
    /// <summary>The column name.</summary>
    public required string Name { get; init; }

    /// <summary>The declared type, including any length or precision.</summary>
    public required string Type { get; init; }

    /// <summary>True for "[pk]" or "[primary key]".</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>True for "[unique]".</summary>
    public bool IsUnique { get; init; }

    /// <summary>True for "[not null]".</summary>
    public bool IsNotNull { get; init; }

    /// <summary>True for "[increment]".</summary>
    public bool IsIncrement { get; init; }

    /// <summary>The "[default: …]" value, as written.</summary>
    public string? Default { get; init; }

    /// <summary>The "[note: '…']" text.</summary>
    public string? Note { get; init; }

    /// <summary>Settings the parser did not recognise, kept for the detail panel.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>1-based line the column was declared on.</summary>
    public int Line { get; init; }
}

/// <summary>An entry in a table's "indexes { … }" block.</summary>
/// <param name="Columns">The indexed columns or expressions.</param>
/// <param name="Name">The "[name: '…']" setting.</param>
/// <param name="IsUnique">True for "[unique]".</param>
/// <param name="IsPrimaryKey">True for "[pk]".</param>
/// <param name="Type">The "[type: …]" setting, such as btree or hash.</param>
/// <param name="Note">The "[note: '…']" setting.</param>
public sealed record DbmlIndex(
    IReadOnlyList<string> Columns,
    string? Name = null,
    bool IsUnique = false,
    bool IsPrimaryKey = false,
    string? Type = null,
    string? Note = null);

/// <summary>A table.</summary>
public sealed record DbmlTable
{
    /// <summary>The table name.</summary>
    public required string Name { get; init; }

    /// <summary>The schema qualifier, when the table was written as "schema.table".</summary>
    public string? Schema { get; init; }

    /// <summary>The "as" alias, which refs may use in place of the name.</summary>
    public string? Alias { get; init; }

    /// <summary>The table's columns, in declaration order.</summary>
    public IReadOnlyList<DbmlColumn> Columns { get; init; } = [];

    /// <summary>Entries from the "indexes" block.</summary>
    public IReadOnlyList<DbmlIndex> Indexes { get; init; } = [];

    /// <summary>The table's "Note:".</summary>
    public string? Note { get; init; }

    /// <summary>The "[headercolor: …]" setting.</summary>
    public string? HeaderColor { get; init; }

    /// <summary>1-based line the table was declared on.</summary>
    public int Line { get; init; }

    /// <summary>The name refs address this table by, preferring the alias.</summary>
    public string ReferenceName => Alias ?? Name;

    /// <summary>The fully qualified name, including the schema when there is one.</summary>
    public string QualifiedName => Schema is null ? Name : $"{Schema}.{Name}";
}

/// <summary>One endpoint of a relationship.</summary>
/// <param name="Table">The table name or alias as written.</param>
/// <param name="Columns">The referenced columns; more than one for a composite ref.</param>
/// <param name="Schema">The schema qualifier, when written.</param>
public sealed record DbmlEndpoint(string Table, IReadOnlyList<string> Columns, string? Schema = null)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{(Schema is null ? Table : $"{Schema}.{Table}")}.{string.Join(", ", Columns)}";
}

/// <summary>A relationship between two tables.</summary>
public sealed record DbmlRelationship
{
    /// <summary>The optional relationship name.</summary>
    public string? Name { get; init; }

    /// <summary>The left endpoint, as written.</summary>
    public required DbmlEndpoint From { get; init; }

    /// <summary>The right endpoint, as written.</summary>
    public required DbmlEndpoint To { get; init; }

    /// <summary>The cardinality symbol between them.</summary>
    public required DbmlCardinality Cardinality { get; init; }

    /// <summary>The "[delete: …]" referential action.</summary>
    public string? OnDelete { get; init; }

    /// <summary>The "[update: …]" referential action.</summary>
    public string? OnUpdate { get; init; }

    /// <summary>True when the ref was written inline on a column rather than as a "Ref:".</summary>
    public bool IsInline { get; init; }

    /// <summary>1-based line the relationship was declared on.</summary>
    public int Line { get; init; }
}

/// <summary>One value of an enum.</summary>
/// <param name="Name">The value.</param>
/// <param name="Note">Its "[note: '…']", when present.</param>
public sealed record DbmlEnumValue(string Name, string? Note = null);

/// <summary>An enum type.</summary>
/// <param name="Name">The enum name.</param>
/// <param name="Values">Its values, in declaration order.</param>
/// <param name="Schema">The schema qualifier, when written.</param>
/// <param name="Line">1-based line the enum was declared on.</param>
public sealed record DbmlEnum(
    string Name,
    IReadOnlyList<DbmlEnumValue> Values,
    string? Schema = null,
    int Line = 0);

/// <summary>A named group of tables.</summary>
/// <param name="Name">The group name.</param>
/// <param name="Tables">The table names in it.</param>
/// <param name="Note">The group's note, when present.</param>
/// <param name="Line">1-based line the group was declared on.</param>
public sealed record DbmlTableGroup(
    string Name,
    IReadOnlyList<string> Tables,
    string? Note = null,
    int Line = 0);

/// <summary>The "Project" block.</summary>
/// <param name="Name">The project name.</param>
/// <param name="DatabaseType">The "database_type" setting.</param>
/// <param name="Note">The project note.</param>
/// <param name="Settings">Any other key/value settings.</param>
public sealed record DbmlProject(
    string Name,
    string? DatabaseType = null,
    string? Note = null,
    IReadOnlyDictionary<string, string>? Settings = null);

/// <summary>A standalone "Note name { '…' }" block.</summary>
/// <param name="Name">The note's name.</param>
/// <param name="Text">Its content.</param>
/// <param name="Line">1-based line the note was declared on.</param>
public sealed record DbmlStickyNote(string Name, string Text, int Line = 0);

/// <summary>
/// A parsed DBML document.
/// <para>
/// The model keeps everything the source said, including the parts Mermaid's
/// <c>erDiagram</c> cannot express — enums, table groups, indexes, notes, colours. Those
/// are not dropped on the way to a diagram; they are what the table-detail panel beside
/// it shows (plan.md section 4.2).
/// </para>
/// </summary>
public sealed record DbmlSchema
{
    /// <summary>An empty schema.</summary>
    public static DbmlSchema Empty { get; } = new();

    /// <summary>The project block, when the document declared one.</summary>
    public DbmlProject? Project { get; init; }

    /// <summary>Tables in declaration order.</summary>
    public IReadOnlyList<DbmlTable> Tables { get; init; } = [];

    /// <summary>Relationships, both standalone and inline.</summary>
    public IReadOnlyList<DbmlRelationship> Relationships { get; init; } = [];

    /// <summary>Enum types.</summary>
    public IReadOnlyList<DbmlEnum> Enums { get; init; } = [];

    /// <summary>Table groups.</summary>
    public IReadOnlyList<DbmlTableGroup> TableGroups { get; init; } = [];

    /// <summary>Standalone sticky notes.</summary>
    public IReadOnlyList<DbmlStickyNote> Notes { get; init; } = [];

    /// <summary>Problems found while parsing; a schema with errors still renders.</summary>
    public IReadOnlyList<DbmlDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>True when nothing was parsed successfully.</summary>
    public bool IsEmpty => Tables.Count == 0 && Enums.Count == 0 && Notes.Count == 0;

    /// <summary>Finds a table by name or alias, ignoring case.</summary>
    public DbmlTable? FindTable(string nameOrAlias) =>
        Tables.FirstOrDefault(t =>
            string.Equals(t.Name, nameOrAlias, StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Alias, nameOrAlias, StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.QualifiedName, nameOrAlias, StringComparison.OrdinalIgnoreCase));
}
