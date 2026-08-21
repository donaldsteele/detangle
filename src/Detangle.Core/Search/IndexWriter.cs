using Detangle.Core.Vault;
using Microsoft.Data.Sqlite;

namespace Detangle.Core.Search;

/// <summary>
/// Writes documents and their sections into the index using statements prepared once and
/// reused for every row.
/// <para>
/// This exists for one reason: a command created per insert makes SQLite parse the same
/// SQL tens of thousands of times over a large vault, and that parsing — not the writing
/// — was the bulk of a cold index. Reusing three prepared statements took a 5,000-file
/// vault from around nineteen seconds to a fraction of that.
/// </para>
/// </summary>
internal sealed class IndexWriter : IDisposable
{
    private readonly SqliteCommand _deleteCommand;
    private readonly SqliteCommand _documentCommand;
    private readonly SqliteCommand _sectionCommand;

    /// <summary>Prepares the statements against a connection and transaction.</summary>
    public IndexWriter(SqliteConnection connection, SqliteTransaction transaction)
    {
        _deleteCommand = connection.CreateCommand();
        _deleteCommand.Transaction = transaction;
        _deleteCommand.CommandText = """
            DELETE FROM sections WHERE path = $path;
            DELETE FROM documents WHERE path = $path;
            """;
        _deleteCommand.Parameters.Add("$path", SqliteType.Text);
        _deleteCommand.Prepare();

        _documentCommand = connection.CreateCommand();
        _documentCommand.Transaction = transaction;
        _documentCommand.CommandText = """
            INSERT INTO documents(path, title, type, status, author, id, tags, created, updated)
            VALUES($path, $title, $type, $status, $author, $id, $tags, $created, $updated)
            """;
        _documentCommand.Parameters.Add("$path", SqliteType.Text);
        _documentCommand.Parameters.Add("$title", SqliteType.Text);
        _documentCommand.Parameters.Add("$type", SqliteType.Text);
        _documentCommand.Parameters.Add("$status", SqliteType.Text);
        _documentCommand.Parameters.Add("$author", SqliteType.Text);
        _documentCommand.Parameters.Add("$id", SqliteType.Text);
        _documentCommand.Parameters.Add("$tags", SqliteType.Text);
        _documentCommand.Parameters.Add("$created", SqliteType.Integer);
        _documentCommand.Parameters.Add("$updated", SqliteType.Integer);
        _documentCommand.Prepare();

        _sectionCommand = connection.CreateCommand();
        _sectionCommand.Transaction = transaction;
        _sectionCommand.CommandText = """
            INSERT INTO sections(path, heading, body, line)
            VALUES($path, $heading, $body, $line)
            """;
        _sectionCommand.Parameters.Add("$path", SqliteType.Text);
        _sectionCommand.Parameters.Add("$heading", SqliteType.Text);
        _sectionCommand.Parameters.Add("$body", SqliteType.Text);
        _sectionCommand.Parameters.Add("$line", SqliteType.Integer);
        _sectionCommand.Prepare();
    }

    /// <summary>Removes a document and its sections.</summary>
    public void Delete(string relativePath)
    {
        _deleteCommand.Parameters["$path"].Value = relativePath;
        _deleteCommand.ExecuteNonQuery();
    }

    /// <summary>Writes a document's scalar fields and its sections.</summary>
    /// <param name="document">The document to index.</param>
    /// <param name="content">Its text, or null when it could not be read.</param>
    public void Write(VaultDocument document, string? content)
    {
        _documentCommand.Parameters["$path"].Value = document.RelativePath;
        _documentCommand.Parameters["$title"].Value = (object?)document.DisplayName ?? DBNull.Value;
        _documentCommand.Parameters["$type"].Value = (object?)document.Frontmatter.Type ?? DBNull.Value;
        _documentCommand.Parameters["$status"].Value = (object?)document.Frontmatter.Status ?? DBNull.Value;
        _documentCommand.Parameters["$author"].Value = document.Frontmatter.Authors.Count > 0
            ? document.Frontmatter.Authors[0]
            : DBNull.Value;
        _documentCommand.Parameters["$id"].Value = (object?)document.Frontmatter.Id ?? DBNull.Value;
        _documentCommand.Parameters["$tags"].Value = string.Join(' ', document.Frontmatter.Tags);
        _documentCommand.Parameters["$created"].Value =
            document.Frontmatter.Created?.ToUnixTimeSeconds() ?? (object)DBNull.Value;
        _documentCommand.Parameters["$updated"].Value =
            document.Frontmatter.Updated?.ToUnixTimeSeconds() ?? document.LastModified.ToUnixTimeSeconds();

        _documentCommand.ExecuteNonQuery();

        if (content is null)
        {
            return;
        }

        foreach ((string? heading, string body, int line) in SearchIndex.Sections(content))
        {
            _sectionCommand.Parameters["$path"].Value = document.RelativePath;
            _sectionCommand.Parameters["$heading"].Value = (object?)heading ?? DBNull.Value;
            _sectionCommand.Parameters["$body"].Value = body;
            _sectionCommand.Parameters["$line"].Value = line;

            _sectionCommand.ExecuteNonQuery();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _deleteCommand.Dispose();
        _documentCommand.Dispose();
        _sectionCommand.Dispose();
    }
}
