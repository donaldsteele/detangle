using System.Globalization;
using Detangle.Core.Vault;
using Microsoft.Data.Sqlite;

namespace Detangle.Core.Search;

/// <summary>One search hit.</summary>
/// <param name="Document">The document that matched.</param>
/// <param name="Heading">The nearest heading above the match, for context.</param>
/// <param name="Snippet">The matching text, with the term marked by "[" and "]".</param>
/// <param name="Line">1-based line of the match.</param>
/// <param name="Rank">FTS rank; lower is better.</param>
public sealed record SearchHit(
    VaultDocument Document,
    string? Heading,
    string Snippet,
    int Line,
    double Rank);

/// <summary>
/// The vault's full-text index, in a SQLite FTS5 sidecar (plan.md section 6.2).
/// <para>
/// One row per section rather than per file: a hit then names the heading it was found
/// under, which is what makes results readable in a wiki whose pages run long. The
/// database lives beside the vault under ".detangle" so it is gitignore-friendly and
/// disposable — deleting it costs a reindex, never data.
/// </para>
/// </summary>
public sealed class SearchIndex : IDisposable
{
    private readonly SqliteConnection _connection;

    private SearchIndex(SqliteConnection connection)
    {
        _connection = connection;
    }

    /// <summary>Opens (and creates if needed) the index for a vault.</summary>
    /// <param name="vaultRootPath">The vault root; the database goes under ".detangle".</param>
    /// <param name="inMemory">True to keep the index in memory, as tests and previews do.</param>
    public static SearchIndex Open(string vaultRootPath, bool inMemory = false)
    {
        string connectionString;

        if (inMemory)
        {
            connectionString = "Data Source=:memory:";
        }
        else
        {
            string directory = Path.Combine(vaultRootPath, ".detangle");
            Directory.CreateDirectory(directory);
            connectionString = $"Data Source={Path.Combine(directory, "cache.db")}";
        }

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        var index = new SearchIndex(connection);
        index.CreateSchema();

        return index;
    }

    /// <summary>How many sections are indexed.</summary>
    public int SectionCount
    {
        get
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sections";

            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Rebuilds the index for a whole vault.</summary>
    /// <param name="vault">The scanned vault.</param>
    /// <param name="contentReader">Reads a document's text.</param>
    /// <param name="cancellationToken">Cancels a long reindex.</param>
    public void Rebuild(
        VaultSnapshot vault,
        Func<VaultDocument, string?> contentReader,
        CancellationToken cancellationToken = default)
    {
        using SqliteTransaction transaction = _connection.BeginTransaction();

        using (SqliteCommand clear = _connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM sections; DELETE FROM documents;";
            clear.ExecuteNonQuery();
        }

        // The statements are prepared once and reused for every row. Creating a command
        // per insert means SQLite parses the same SQL tens of thousands of times, which
        // was the difference between a five-thousand-file vault indexing in seconds and
        // in the better part of a minute — the table was already empty, so no per-document
        // delete is needed either.
        using var writer = new IndexWriter(_connection, transaction);

        foreach (VaultDocument document in vault.Documents.Where(d => d.IsMarkdown))
        {
            cancellationToken.ThrowIfCancellationRequested();

            writer.Write(document, contentReader(document));
        }

        transaction.Commit();
    }

    /// <summary>Reindexes one document, replacing whatever was there.</summary>
    public void Update(VaultDocument document, string? content)
    {
        using SqliteTransaction transaction = _connection.BeginTransaction();

        using (var writer = new IndexWriter(_connection, transaction))
        {
            writer.Delete(document.RelativePath);
            writer.Write(document, content);
        }

        transaction.Commit();
    }

    /// <summary>Removes a document from the index.</summary>
    public void Remove(string relativePath)
    {
        using SqliteCommand command = _connection.CreateCommand();

        command.CommandText = """
            DELETE FROM sections WHERE path = $path;
            DELETE FROM documents WHERE path = $path;
            """;

        command.Parameters.AddWithValue("$path", relativePath);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Runs a query. Field filters are applied in SQL against the document table, and the
    /// text half through FTS5 — so "type:concept attention" narrows before it searches.
    /// </summary>
    /// <param name="query">The parsed query.</param>
    /// <param name="vault">The vault, for turning paths back into documents.</param>
    /// <param name="limit">Maximum hits.</param>
    public IReadOnlyList<SearchHit> Search(SearchQuery query, VaultSnapshot vault, int limit = 100)
    {
        if (query.IsEmpty)
        {
            return [];
        }

        var conditions = new List<string>();
        using SqliteCommand command = _connection.CreateCommand();

        string? match = query.ToMatchExpression();

        if (match is not null)
        {
            conditions.Add("sections MATCH $match");
            command.Parameters.AddWithValue("$match", match);
        }

        int parameterIndex = 0;

        foreach (FieldFilter filter in query.Filters)
        {
            string parameter = $"$f{parameterIndex++}";

            switch (filter.Field)
            {
                case "type":
                case "status":
                case "author":
                case "id":
                case "title":
                    conditions.Add($"lower(documents.{Column(filter.Field)}) = lower({parameter})");
                    command.Parameters.AddWithValue(parameter, filter.Value);
                    break;

                case "path":
                    conditions.Add($"documents.path LIKE {parameter} || '%'");
                    command.Parameters.AddWithValue(parameter, filter.Value);
                    break;

                case "tag":
                case "tags":
                    // Tags are stored space-delimited, and the browser's tree is a
                    // hierarchy, so "tag:llm" means llm and everything under it — the tag
                    // itself, or the tag followed by a separator. Matching a bare prefix
                    // instead would make "tag:llm" claim "llm-ops" as a child it is not,
                    // and the rail's count and the search's count would then disagree.
                    conditions.Add(
                        $"((' ' || lower(documents.tags) || ' ') LIKE '% ' || lower({parameter}) || ' %'"
                        + $" OR (' ' || lower(documents.tags) || ' ') LIKE '% ' || lower({parameter}) || '/%')");
                    command.Parameters.AddWithValue(parameter, filter.Value);
                    break;

                case "updated":
                case "created":
                    {
                        DateTimeOffset? date = SearchQuery.ParseDate(filter.Value);

                        if (date is null)
                        {
                            break;
                        }

                        string comparison = filter.Comparison == FieldComparison.Before ? "<" : ">";
                        conditions.Add($"documents.{filter.Field} {comparison} {parameter}");
                        command.Parameters.AddWithValue(parameter, date.Value.ToUnixTimeSeconds());
                        break;
                    }
            }
        }

        if (conditions.Count == 0)
        {
            return [];
        }

        command.CommandText = $"""
            SELECT sections.path, sections.heading, sections.line,
                   snippet(sections, 2, '[', ']', '…', 12) AS snippet,
                   {(match is null ? "0" : "bm25(sections)")} AS rank
            FROM sections
            JOIN documents ON documents.path = sections.path
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY rank
            LIMIT {limit}
            """;

        var hits = new List<SearchHit>();

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            string path = reader.GetString(0);
            VaultDocument? document = vault.Index.ByRelativePath(path).FirstOrDefault();

            if (document is null)
            {
                continue;
            }

            hits.Add(new SearchHit(
                document,
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(3),
                reader.GetInt32(2),
                reader.GetDouble(4)));
        }

        return hits;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
    }

    private void CreateSchema()
    {
        using SqliteCommand command = _connection.CreateCommand();

        // "sections" is the FTS table; "documents" holds the scalar fields the query
        // syntax filters on, which FTS5 columns cannot compare numerically.
        command.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS sections USING fts5(
                path UNINDEXED,
                heading,
                body,
                line UNINDEXED,
                tokenize = 'unicode61 remove_diacritics 2'
            );

            CREATE TABLE IF NOT EXISTS documents(
                path TEXT PRIMARY KEY,
                title TEXT,
                type TEXT,
                status TEXT,
                author TEXT,
                id TEXT,
                tags TEXT,
                created INTEGER,
                updated INTEGER
            );
            """;

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Splits a document into heading-delimited sections. Frontmatter is skipped so that
    /// a search for a tag value does not match the frontmatter block of every page that
    /// carries it. Public because the split decides what is findable, which is worth
    /// asserting on directly.
    /// </summary>
    public static IEnumerable<(string? Heading, string Body, int Line)> Sections(string content)
    {
        string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        string? heading = null;
        int start = 1;
        var body = new List<string>();
        bool inFrontmatter = lines.Length > 0 && lines[0].Trim() == "---";

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (inFrontmatter)
            {
                if (i > 0 && line.Trim() == "---")
                {
                    inFrontmatter = false;
                }

                continue;
            }

            if (line.StartsWith('#') && line.TrimStart('#').StartsWith(' '))
            {
                if (body.Count > 0)
                {
                    yield return (heading, string.Join('\n', body), start);
                }

                heading = line.TrimStart('#').Trim();
                start = i + 1;
                body = [heading];
                continue;
            }

            if (line.Trim().Length > 0)
            {
                if (body.Count == 0)
                {
                    start = i + 1;
                }

                body.Add(line);
            }
        }

        if (body.Count > 0)
        {
            yield return (heading, string.Join('\n', body), start);
        }
    }

    private static string Column(string field) => field switch
    {
        "title" => "title",
        "type" => "type",
        "status" => "status",
        "author" => "author",
        _ => "id",
    };
}
