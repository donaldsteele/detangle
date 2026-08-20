namespace Detangle.Core.Dbml;

/// <summary>
/// A recursive-descent parser for DBML.
/// <para>
/// It never throws on bad input. A `.dbml` file in a wiki is as likely to be
/// half-written as any other generated artifact, so an unparseable construct becomes a
/// diagnostic with a line and column and the parser resynchronises at the next top-level
/// block — the rest of the schema still renders, and the error shows up as an inline
/// card rather than as a blank diagram (plan.md section 4.2).
/// </para>
/// </summary>
public sealed class DbmlParser
{
    private readonly IReadOnlyList<DbmlToken> _tokens;
    private readonly string[] _sourceLines;
    private readonly List<DbmlDiagnostic> _diagnostics = [];
    private int _position;

    private DbmlParser(string source)
    {
        _tokens = DbmlLexer.Tokenize(source);
        _sourceLines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    /// <summary>Parses a DBML document.</summary>
    public static DbmlSchema Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return DbmlSchema.Empty;
        }

        return new DbmlParser(source).ParseSchema();
    }

    private DbmlToken Current => _tokens[_position];

    private DbmlToken Peek(int offset = 1) =>
        _tokens[Math.Min(_position + offset, _tokens.Count - 1)];

    private DbmlSchema ParseSchema()
    {
        DbmlProject? project = null;
        var tables = new List<DbmlTable>();
        var relationships = new List<DbmlRelationship>();
        var enums = new List<DbmlEnum>();
        var groups = new List<DbmlTableGroup>();
        var notes = new List<DbmlStickyNote>();

        while (Current.Kind != DbmlTokenKind.End)
        {
            DbmlToken token = Current;

            if (token.Kind != DbmlTokenKind.Identifier)
            {
                Report($"Expected a top-level block, found \"{Describe(token)}\".", token);
                Resynchronise();
                continue;
            }

            if (token.Is("project"))
            {
                project = ParseProject() ?? project;
                continue;
            }

            if (token.Is("table"))
            {
                DbmlTable? table = ParseTable(relationships);

                if (table is not null)
                {
                    tables.Add(table);
                }

                continue;
            }

            if (token.Is("ref"))
            {
                DbmlRelationship? relationship = ParseRef();

                if (relationship is not null)
                {
                    relationships.Add(relationship);
                }

                continue;
            }

            if (token.Is("enum"))
            {
                DbmlEnum? parsed = ParseEnum();

                if (parsed is not null)
                {
                    enums.Add(parsed);
                }

                continue;
            }

            if (token.Is("tablegroup") || (token.Is("table") && Peek().Is("group")))
            {
                DbmlTableGroup? group = ParseTableGroup();

                if (group is not null)
                {
                    groups.Add(group);
                }

                continue;
            }

            if (token.Is("note"))
            {
                DbmlStickyNote? note = ParseStickyNote();

                if (note is not null)
                {
                    notes.Add(note);
                }

                continue;
            }

            Report($"Unknown top-level block \"{token.Text}\".", token);
            Resynchronise();
        }

        return new DbmlSchema
        {
            Project = project,
            Tables = tables,
            Relationships = relationships,
            Enums = enums,
            TableGroups = groups,
            Notes = notes,
            Diagnostics = _diagnostics,
        };
    }

    private DbmlProject? ParseProject()
    {
        Advance();

        string name = ReadName() ?? "Project";

        if (!Expect(DbmlTokenKind.OpenBrace))
        {
            return null;
        }

        string? databaseType = null;
        string? note = null;
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (Current.Kind is not (DbmlTokenKind.CloseBrace or DbmlTokenKind.End))
        {
            if (Current.Kind != DbmlTokenKind.Identifier)
            {
                Advance();
                continue;
            }

            string key = Current.Text;
            Advance();

            if (Current.Kind == DbmlTokenKind.Colon)
            {
                Advance();
            }

            string value = ReadValue();

            if (string.Equals(key, "database_type", StringComparison.OrdinalIgnoreCase))
            {
                databaseType = value;
            }
            else if (string.Equals(key, "note", StringComparison.OrdinalIgnoreCase))
            {
                note = value;
            }
            else
            {
                settings[key] = value;
            }
        }

        Expect(DbmlTokenKind.CloseBrace);

        return new DbmlProject(name, databaseType, note, settings);
    }

    private DbmlTable? ParseTable(List<DbmlRelationship> relationships)
    {
        int line = Current.Line;
        Advance();

        (string? schema, string? name) = ReadQualifiedName();

        if (name is null)
        {
            Report("A table needs a name.", Current);
            Resynchronise();
            return null;
        }

        string? alias = null;

        if (Current.Is("as"))
        {
            Advance();
            alias = ReadName();
        }

        string? headerColor = null;

        if (Current.Kind == DbmlTokenKind.OpenBracket)
        {
            IReadOnlyDictionary<string, string> settings = ParseSettings();
            settings.TryGetValue("headercolor", out headerColor);
        }

        if (!Expect(DbmlTokenKind.OpenBrace))
        {
            Resynchronise();
            return null;
        }

        var columns = new List<DbmlColumn>();
        var indexes = new List<DbmlIndex>();
        string? note = null;

        while (Current.Kind is not (DbmlTokenKind.CloseBrace or DbmlTokenKind.End))
        {
            if (Current.Is("indexes"))
            {
                Advance();
                indexes.AddRange(ParseIndexes());
                continue;
            }

            if (Current.Is("note"))
            {
                Advance();

                if (Current.Kind == DbmlTokenKind.Colon)
                {
                    Advance();
                    note = ReadValue();
                    continue;
                }

                if (Current.Kind == DbmlTokenKind.OpenBrace)
                {
                    Advance();
                    note = ReadValue();
                    Expect(DbmlTokenKind.CloseBrace);
                    continue;
                }

                continue;
            }

            DbmlColumn? column = ParseColumn(name, alias, relationships);

            if (column is null)
            {
                break;
            }

            columns.Add(column);
        }

        Expect(DbmlTokenKind.CloseBrace);

        return new DbmlTable
        {
            Name = name,
            Schema = schema,
            Alias = alias,
            Columns = columns,
            Indexes = indexes,
            Note = note,
            HeaderColor = headerColor,
            Line = line,
        };
    }

    private DbmlColumn? ParseColumn(string tableName, string? alias, List<DbmlRelationship> relationships)
    {
        // A quoted column name is legal DBML and common where a column has a space in it.
        if (Current.Kind is not (DbmlTokenKind.Identifier or DbmlTokenKind.String))
        {
            Report($"Expected a column name, found \"{Describe(Current)}\".", Current);
            Advance();
            return null;
        }

        int line = Current.Line;
        string name = Current.Text;
        Advance();

        string type = ReadColumnType();

        bool primaryKey = false;
        bool unique = false;
        bool notNull = false;
        bool increment = false;
        string? defaultValue = null;
        string? note = null;
        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Current.Kind == DbmlTokenKind.OpenBracket)
        {
            foreach (KeyValuePair<string, string> setting in ParseSettings())
            {
                switch (setting.Key.ToLowerInvariant())
                {
                    case "pk":
                    case "primary key":
                        primaryKey = true;
                        break;
                    case "unique":
                        unique = true;
                        break;
                    case "not null":
                        notNull = true;
                        break;
                    case "null":
                        break;
                    case "increment":
                        increment = true;
                        break;
                    case "default":
                        defaultValue = setting.Value;
                        break;
                    case "note":
                        note = setting.Value;
                        break;
                    case "ref":
                        // An inline "[ref: > other.id]" is a relationship written on the
                        // column; it becomes a first-class ref so the diagram sees it.
                        DbmlRelationship? inline = ParseInlineRef(alias ?? tableName, name, setting.Value, line);

                        if (inline is not null)
                        {
                            relationships.Add(inline);
                        }

                        break;
                    default:
                        extra[setting.Key] = setting.Value;
                        break;
                }
            }
        }

        return new DbmlColumn
        {
            Name = name,
            Type = type,
            IsPrimaryKey = primaryKey,
            IsUnique = unique,
            IsNotNull = notNull,
            IsIncrement = increment,
            Default = defaultValue,
            Note = note,
            Settings = extra,
            Line = line,
        };
    }

    /// <summary>
    /// Reads a column type, which may carry a length — "varchar(255)" — a schema
    /// qualifier, or an array suffix.
    /// </summary>
    private string ReadColumnType()
    {
        if (Current.Kind is not (DbmlTokenKind.Identifier or DbmlTokenKind.String))
        {
            return string.Empty;
        }

        string type = Current.Text;
        Advance();

        while (Current.Kind == DbmlTokenKind.Dot && Peek().Kind == DbmlTokenKind.Identifier)
        {
            Advance();
            type += $".{Current.Text}";
            Advance();
        }

        if (Current.Kind == DbmlTokenKind.OpenParen)
        {
            Advance();
            var arguments = new List<string>();

            while (Current.Kind is not (DbmlTokenKind.CloseParen or DbmlTokenKind.End))
            {
                if (Current.Kind != DbmlTokenKind.Comma)
                {
                    arguments.Add(Current.Text);
                }

                Advance();
            }

            Expect(DbmlTokenKind.CloseParen);
            type += $"({string.Join(",", arguments)})";
        }

        while (Current.Kind == DbmlTokenKind.OpenBracket
            && Peek().Kind == DbmlTokenKind.CloseBracket)
        {
            Advance();
            Advance();
            type += "[]";
        }

        return type;
    }

    private List<DbmlIndex> ParseIndexes()
    {
        var indexes = new List<DbmlIndex>();

        if (!Expect(DbmlTokenKind.OpenBrace))
        {
            return indexes;
        }

        while (Current.Kind is not (DbmlTokenKind.CloseBrace or DbmlTokenKind.End))
        {
            var columns = new List<string>();

            if (Current.Kind == DbmlTokenKind.OpenParen)
            {
                Advance();

                while (Current.Kind is not (DbmlTokenKind.CloseParen or DbmlTokenKind.End))
                {
                    if (Current.Kind != DbmlTokenKind.Comma)
                    {
                        columns.Add(Current.Text);
                    }

                    Advance();
                }

                Expect(DbmlTokenKind.CloseParen);
            }
            else if (Current.Kind is DbmlTokenKind.Identifier or DbmlTokenKind.Expression)
            {
                columns.Add(Current.Text);
                Advance();
            }
            else
            {
                Advance();
                continue;
            }

            string? name = null;
            string? type = null;
            string? note = null;
            bool unique = false;
            bool primaryKey = false;

            if (Current.Kind == DbmlTokenKind.OpenBracket)
            {
                foreach (KeyValuePair<string, string> setting in ParseSettings())
                {
                    switch (setting.Key.ToLowerInvariant())
                    {
                        case "name":
                            name = setting.Value;
                            break;
                        case "unique":
                            unique = true;
                            break;
                        case "pk":
                        case "primary key":
                            primaryKey = true;
                            break;
                        case "type":
                            type = setting.Value;
                            break;
                        case "note":
                            note = setting.Value;
                            break;
                    }
                }
            }

            indexes.Add(new DbmlIndex(columns, name, unique, primaryKey, type, note));
        }

        Expect(DbmlTokenKind.CloseBrace);

        return indexes;
    }

    private DbmlRelationship? ParseRef()
    {
        int line = Current.Line;
        Advance();

        string? name = null;

        if (Current.Kind == DbmlTokenKind.Identifier)
        {
            name = Current.Text;
            Advance();
        }

        // Both "Ref: a.b > c.d" and "Ref name { a.b > c.d }" are legal.
        bool braced = Current.Kind == DbmlTokenKind.OpenBrace;

        if (braced)
        {
            Advance();
        }
        else if (Current.Kind == DbmlTokenKind.Colon)
        {
            Advance();
        }
        else
        {
            Report("A ref needs either \":\" or a block.", Current);
            Resynchronise();
            return null;
        }

        DbmlEndpoint? from = ReadEndpoint();

        if (from is null || Current.Kind != DbmlTokenKind.Cardinality)
        {
            Report("A ref needs two endpoints separated by <, >, - or <>.", Current);
            Resynchronise();
            return null;
        }

        DbmlCardinality cardinality = ToCardinality(Current.Text);
        Advance();

        DbmlEndpoint? to = ReadEndpoint();

        if (to is null)
        {
            Report("A ref needs a second endpoint.", Current);
            Resynchronise();
            return null;
        }

        string? onDelete = null;
        string? onUpdate = null;

        if (Current.Kind == DbmlTokenKind.OpenBracket)
        {
            IReadOnlyDictionary<string, string> settings = ParseSettings();
            settings.TryGetValue("delete", out onDelete);
            settings.TryGetValue("update", out onUpdate);
        }

        if (braced)
        {
            Expect(DbmlTokenKind.CloseBrace);
        }

        return new DbmlRelationship
        {
            Name = name,
            From = from,
            To = to,
            Cardinality = cardinality,
            OnDelete = onDelete,
            OnUpdate = onUpdate,
            Line = line,
        };
    }

    /// <summary>Parses the value of an inline "[ref: &gt; table.column]" setting.</summary>
    private DbmlRelationship? ParseInlineRef(string tableName, string columnName, string value, int line)
    {
        string trimmed = value.Trim();

        if (trimmed.Length < 2)
        {
            return null;
        }

        string symbol = trimmed.StartsWith("<>", StringComparison.Ordinal) ? "<>" : trimmed[..1];
        string target = trimmed[symbol.Length..].Trim();

        string[] parts = target.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return null;
        }

        string? schema = parts.Length >= 3 ? parts[0] : null;
        string table = parts[^2];
        string column = parts[^1];

        return new DbmlRelationship
        {
            From = new DbmlEndpoint(tableName, [columnName]),
            To = new DbmlEndpoint(table, [column], schema),
            Cardinality = ToCardinality(symbol),
            IsInline = true,
            Line = line,
        };
    }

    private DbmlEnum? ParseEnum()
    {
        int line = Current.Line;
        Advance();

        (string? schema, string? name) = ReadQualifiedName();

        if (name is null)
        {
            Report("An enum needs a name.", Current);
            Resynchronise();
            return null;
        }

        if (!Expect(DbmlTokenKind.OpenBrace))
        {
            Resynchronise();
            return null;
        }

        var values = new List<DbmlEnumValue>();

        while (Current.Kind is not (DbmlTokenKind.CloseBrace or DbmlTokenKind.End))
        {
            if (Current.Kind is not (DbmlTokenKind.Identifier or DbmlTokenKind.String or DbmlTokenKind.Number))
            {
                Advance();
                continue;
            }

            string value = Current.Text;
            Advance();

            string? note = null;

            if (Current.Kind == DbmlTokenKind.OpenBracket)
            {
                ParseSettings().TryGetValue("note", out note);
            }

            values.Add(new DbmlEnumValue(value, note));
        }

        Expect(DbmlTokenKind.CloseBrace);

        return new DbmlEnum(name, values, schema, line);
    }

    private DbmlTableGroup? ParseTableGroup()
    {
        int line = Current.Line;
        Advance();

        if (Current.Is("group"))
        {
            Advance();
        }

        string name = ReadName() ?? "group";

        if (!Expect(DbmlTokenKind.OpenBrace))
        {
            Resynchronise();
            return null;
        }

        var tables = new List<string>();
        string? note = null;

        while (Current.Kind is not (DbmlTokenKind.CloseBrace or DbmlTokenKind.End))
        {
            if (Current.Is("note"))
            {
                Advance();

                if (Current.Kind == DbmlTokenKind.Colon)
                {
                    Advance();
                }

                note = ReadValue();
                continue;
            }

            if (Current.Kind == DbmlTokenKind.Identifier)
            {
                (_, string? table) = ReadQualifiedName();

                if (table is not null)
                {
                    tables.Add(table);
                }

                continue;
            }

            Advance();
        }

        Expect(DbmlTokenKind.CloseBrace);

        return new DbmlTableGroup(name, tables, note, line);
    }

    private DbmlStickyNote? ParseStickyNote()
    {
        int line = Current.Line;
        Advance();

        string name = ReadName() ?? "note";

        if (Current.Kind == DbmlTokenKind.Colon)
        {
            Advance();
            return new DbmlStickyNote(name, ReadValue(), line);
        }

        if (!Expect(DbmlTokenKind.OpenBrace))
        {
            Resynchronise();
            return null;
        }

        string text = ReadValue();
        Expect(DbmlTokenKind.CloseBrace);

        return new DbmlStickyNote(name, text, line);
    }

    private DbmlEndpoint? ReadEndpoint()
    {
        var parts = new List<string>();

        while (Current.Kind is DbmlTokenKind.Identifier or DbmlTokenKind.String)
        {
            parts.Add(Current.Text);
            Advance();

            if (Current.Kind != DbmlTokenKind.Dot)
            {
                break;
            }

            Advance();
        }

        if (parts.Count < 2)
        {
            return null;
        }

        var columns = new List<string> { parts[^1] };

        // A composite endpoint names its columns in parentheses: "table.(a, b)".
        if (Current.Kind == DbmlTokenKind.OpenParen)
        {
            columns.Clear();
            Advance();

            while (Current.Kind is not (DbmlTokenKind.CloseParen or DbmlTokenKind.End))
            {
                if (Current.Kind != DbmlTokenKind.Comma)
                {
                    columns.Add(Current.Text);
                }

                Advance();
            }

            Expect(DbmlTokenKind.CloseParen);

            return new DbmlEndpoint(parts[^1], columns, parts.Count >= 2 ? parts[^2] : null);
        }

        string table = parts[^2];
        string? schema = parts.Count >= 3 ? parts[^3] : null;

        return new DbmlEndpoint(table, columns, schema);
    }

    /// <summary>
    /// Reads a "[key: value, flag]" settings block. Flags without a value — "pk",
    /// "unique" — come back with an empty value, and multi-word flags such as "not null"
    /// are joined so the caller sees one key.
    /// </summary>
    private IReadOnlyDictionary<string, string> ParseSettings()
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Expect(DbmlTokenKind.OpenBracket))
        {
            return settings;
        }

        while (Current.Kind is not (DbmlTokenKind.CloseBracket or DbmlTokenKind.End))
        {
            if (Current.Kind == DbmlTokenKind.Comma)
            {
                Advance();
                continue;
            }

            var words = new List<string>();

            while (Current.Kind is DbmlTokenKind.Identifier or DbmlTokenKind.Number
                && Peek().Kind is not DbmlTokenKind.Dot)
            {
                words.Add(Current.Text);
                Advance();

                if (Current.Kind is DbmlTokenKind.Colon or DbmlTokenKind.Comma or DbmlTokenKind.CloseBracket)
                {
                    break;
                }
            }

            if (words.Count == 0)
            {
                Advance();
                continue;
            }

            string key = string.Join(' ', words);

            if (Current.Kind != DbmlTokenKind.Colon)
            {
                settings[key] = string.Empty;
                continue;
            }

            Advance();
            settings[key] = ReadSettingValue();
        }

        Expect(DbmlTokenKind.CloseBracket);

        return settings;
    }

    /// <summary>
    /// Reads a setting's value, which runs to the next comma or "]". An inline ref value
    /// is several tokens ("&gt; users . id"), so they are joined rather than taken one at a
    /// time.
    /// </summary>
    private string ReadSettingValue()
    {
        var parts = new List<string>();

        while (Current.Kind is not (DbmlTokenKind.Comma or DbmlTokenKind.CloseBracket or DbmlTokenKind.End))
        {
            parts.Add(Current.Kind == DbmlTokenKind.Expression ? $"`{Current.Text}`" : Current.Text);
            Advance();
        }

        // Tokens are joined with spaces so that "[update: no action]" keeps its space, then
        // the separators that are never spaced in DBML — the dot of a qualified name — are
        // closed back up.
        return string.Join(' ', parts)
            .Replace(" . ", ".", StringComparison.Ordinal)
            .Replace(". ", ".", StringComparison.Ordinal)
            .Replace(" .", ".", StringComparison.Ordinal)
            .Trim();
    }

    private string ReadValue()
    {
        if (Current.Kind is DbmlTokenKind.String or DbmlTokenKind.Identifier
            or DbmlTokenKind.Number or DbmlTokenKind.Expression)
        {
            string value = Current.Text;
            Advance();
            return value;
        }

        return string.Empty;
    }

    private string? ReadName()
    {
        if (Current.Kind is DbmlTokenKind.Identifier or DbmlTokenKind.String)
        {
            string name = Current.Text;
            Advance();
            return name;
        }

        return null;
    }

    private (string? Schema, string? Name) ReadQualifiedName()
    {
        string? first = ReadName();

        if (first is null)
        {
            return (null, null);
        }

        if (Current.Kind != DbmlTokenKind.Dot)
        {
            return (null, first);
        }

        Advance();
        string? second = ReadName();

        return second is null ? (null, first) : (first, second);
    }

    private static DbmlCardinality ToCardinality(string symbol) => symbol switch
    {
        ">" => DbmlCardinality.ManyToOne,
        "<" => DbmlCardinality.OneToMany,
        "<>" => DbmlCardinality.ManyToMany,
        _ => DbmlCardinality.OneToOne,
    };

    private void Advance()
    {
        if (_position < _tokens.Count - 1)
        {
            _position++;
        }
    }

    private bool Expect(DbmlTokenKind kind)
    {
        if (Current.Kind == kind)
        {
            Advance();
            return true;
        }

        Report($"Expected \"{Symbol(kind)}\", found \"{Describe(Current)}\".", Current);

        return false;
    }

    /// <summary>
    /// Skips to the next plausible top-level block after an error, so one bad table does
    /// not swallow the rest of the file.
    /// </summary>
    private void Resynchronise()
    {
        int depth = 0;

        while (Current.Kind != DbmlTokenKind.End)
        {
            switch (Current.Kind)
            {
                case DbmlTokenKind.OpenBrace:
                    depth++;
                    break;
                case DbmlTokenKind.CloseBrace:
                    depth--;

                    if (depth <= 0)
                    {
                        Advance();
                        return;
                    }

                    break;
                case DbmlTokenKind.Identifier when depth == 0 && IsTopLevelKeyword(Current):
                    return;
            }

            Advance();
        }
    }

    private static bool IsTopLevelKeyword(DbmlToken token) =>
        token.Is("table") || token.Is("ref") || token.Is("enum")
        || token.Is("project") || token.Is("tablegroup") || token.Is("note");

    private void Report(string message, DbmlToken token)
    {
        string sourceLine = token.Line >= 1 && token.Line <= _sourceLines.Length
            ? _sourceLines[token.Line - 1]
            : string.Empty;

        _diagnostics.Add(new DbmlDiagnostic(message, token.Line, token.Column, sourceLine));
    }

    private static string Describe(DbmlToken token) =>
        token.Kind == DbmlTokenKind.End ? "end of file" : token.Text;

    private static string Symbol(DbmlTokenKind kind) => kind switch
    {
        DbmlTokenKind.OpenBrace => "{",
        DbmlTokenKind.CloseBrace => "}",
        DbmlTokenKind.OpenBracket => "[",
        DbmlTokenKind.CloseBracket => "]",
        DbmlTokenKind.OpenParen => "(",
        DbmlTokenKind.CloseParen => ")",
        DbmlTokenKind.Colon => ":",
        DbmlTokenKind.Comma => ",",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
