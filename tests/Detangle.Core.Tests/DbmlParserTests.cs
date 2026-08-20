using Detangle.Core.Dbml;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// The DBML conformance suite. Written against the spec's own constructs before the
/// parser existed (plan.md section 4.2), so that owning the grammar rather than taking a
/// dependency is a decision the tests can hold to account.
/// </summary>
public class DbmlParserTests
{
    private static DbmlSchema Ecommerce { get; } = ParseFixture("ecommerce.dbml");

    private static DbmlSchema EdgeCases { get; } = ParseFixture("edge-cases.dbml");

    [Fact]
    public void ParsesTheProjectBlock()
    {
        Assert.Equal("ecommerce", Ecommerce.Project?.Name);
        Assert.Equal("PostgreSQL", Ecommerce.Project?.DatabaseType);
        Assert.Equal("A small shop schema.", Ecommerce.Project?.Note);
    }

    [Fact]
    public void ParsesTablesAliasesAndNotes()
    {
        DbmlTable users = Assert.Single(Ecommerce.Tables, t => t.Name == "users");

        Assert.Equal("U", users.Alias);
        Assert.Equal("U", users.ReferenceName);
        Assert.Equal("Everyone who can sign in.", users.Note);
    }

    [Fact]
    public void ParsesColumnSettings()
    {
        DbmlTable users = Ecommerce.FindTable("users")!;

        DbmlColumn id = users.Columns[0];
        Assert.True(id.IsPrimaryKey);
        Assert.True(id.IsIncrement);

        DbmlColumn username = users.Columns[1];
        Assert.Equal("varchar(255)", username.Type);
        Assert.True(username.IsNotNull);
        Assert.True(username.IsUnique);
        Assert.Equal("Login name", username.Note);

        Assert.Equal("member", users.Columns[2].Default);
        Assert.Equal("`now()`", users.Columns[3].Default);
    }

    [Fact]
    public void ParsesQuotedColumnNames() =>
        Assert.Contains(Ecommerce.FindTable("merchants")!.Columns, c => c.Name == "created at");

    [Fact]
    public void ParsesIndexes()
    {
        DbmlTable users = Ecommerce.FindTable("users")!;

        Assert.Equal(2, users.Indexes.Count);
        Assert.Equal(["username", "role"], users.Indexes[0].Columns);
        Assert.Equal("idx_user_role", users.Indexes[0].Name);
        Assert.True(users.Indexes[0].IsUnique);
        Assert.Equal("btree", users.Indexes[1].Type);
    }

    [Fact]
    public void ParsesCompositePrimaryKeyIndexes()
    {
        DbmlIndex index = Assert.Single(EdgeCases.FindTable("composite_key")!.Indexes);

        Assert.True(index.IsPrimaryKey);
        Assert.Equal(["a", "b"], index.Columns);
    }

    [Fact]
    public void ParsesStandaloneRefsWithNamesAndActions()
    {
        DbmlRelationship named = Assert.Single(Ecommerce.Relationships, r => r.Name == "user_orders");

        Assert.Equal("users", named.From.Table);
        Assert.Equal(["id"], named.From.Columns);
        Assert.Equal("orders", named.To.Table);
        Assert.Equal(DbmlCardinality.OneToMany, named.Cardinality);

        DbmlRelationship cascading = Assert.Single(
            Ecommerce.Relationships, r => r.To.Table == "merchants");

        Assert.Equal("cascade", cascading.OnDelete);
        Assert.Equal("no action", cascading.OnUpdate);
    }

    [Fact]
    public void ParsesInlineRefsOnColumns()
    {
        DbmlRelationship inline = Assert.Single(
            Ecommerce.Relationships, r => r.IsInline && r.From.Table == "merchants");

        Assert.Equal("admin_id", Assert.Single(inline.From.Columns));
        Assert.Equal("U", inline.To.Table);
        Assert.Equal("id", Assert.Single(inline.To.Columns));
        Assert.Equal(DbmlCardinality.ManyToOne, inline.Cardinality);
    }

    [Theory]
    [InlineData("many_to_many", DbmlCardinality.ManyToMany)]
    [InlineData("one_to_one", DbmlCardinality.OneToOne)]
    public void ParsesEveryCardinalitySymbol(string name, DbmlCardinality expected) =>
        Assert.Equal(expected, Assert.Single(EdgeCases.Relationships, r => r.Name == name).Cardinality);

    [Fact]
    public void ParsesEnumsWithQuotedValuesAndNotes()
    {
        DbmlEnum role = Assert.Single(Ecommerce.Enums, e => e.Name == "user_role");

        Assert.Equal(["member", "admin", "super admin"], role.Values.Select(v => v.Name));
        Assert.Equal("Can do anything", role.Values[1].Note);
    }

    [Fact]
    public void ParsesSchemaQualifiedNames()
    {
        DbmlTable events = Assert.Single(EdgeCases.Tables, t => t.Name == "events");

        Assert.Equal("analytics", events.Schema);
        Assert.Equal("analytics.events", events.QualifiedName);
        Assert.Equal("analytics", Assert.Single(EdgeCases.Enums).Schema);
    }

    [Fact]
    public void ParsesTableGroups()
    {
        DbmlTableGroup group = Assert.Single(Ecommerce.TableGroups);

        Assert.Equal("commerce", group.Name);
        Assert.Equal(["merchants", "products"], group.Tables);
    }

    [Fact]
    public void ParsesStickyNotes()
    {
        DbmlStickyNote note = Assert.Single(Ecommerce.Notes);

        Assert.Equal("shop_note", note.Name);
        Assert.Equal("Prices are in minor units.", note.Text);
    }

    [Fact]
    public void ParsesTypesWithPrecisionAndArrays()
    {
        DbmlTable events = EdgeCases.FindTable("analytics.events")!;

        Assert.Equal("text[]", events.Columns.Single(c => c.Name == "tags").Type);
        Assert.Equal("decimal(12,2)", events.Columns.Single(c => c.Name == "amount").Type);
    }

    [Fact]
    public void ParsesTripleQuotedNotes()
    {
        string? note = EdgeCases.FindTable("analytics.events")!
            .Columns.Single(c => c.Name == "note_column").Note;

        Assert.Equal("A multi-line note\nwith two lines.", note);
    }

    [Fact]
    public void IgnoresLineAndBlockComments() =>
        Assert.DoesNotContain(EdgeCases.Tables, t => t.Name.Contains("comment", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void CleanSchemasProduceNoDiagnostics()
    {
        Assert.Empty(Ecommerce.Diagnostics);
        Assert.Empty(EdgeCases.Diagnostics);
    }

    [Fact]
    public void RecoversFromErrorsAndKeepsParsing()
    {
        DbmlSchema broken = ParseFixture("broken.dbml");

        // The malformed table is reported, and the tables on either side of it survive:
        // a half-written .dbml file still renders everything it got right.
        Assert.NotEmpty(broken.Diagnostics);
        Assert.Contains(broken.Tables, t => t.Name == "good_one");
        Assert.Contains(broken.Tables, t => t.Name == "after_the_error");
    }

    [Fact]
    public void DiagnosticsCarryPositionAndSourceLine()
    {
        DbmlDiagnostic diagnostic = ParseFixture("broken.dbml").Diagnostics[0];

        Assert.True(diagnostic.Line > 0);
        Assert.True(diagnostic.Column > 0);
        Assert.NotEmpty(diagnostic.SourceLine);
    }

    [Fact]
    public void EmptyInputIsAnEmptySchema()
    {
        Assert.True(DbmlParser.Parse(string.Empty).IsEmpty);
        Assert.True(DbmlParser.Parse("   \n\n  ").IsEmpty);
    }

    [Fact]
    public void NeverThrowsOnGarbage()
    {
        DbmlSchema schema = DbmlParser.Parse("}{][ Table ( ) : , < > \"unterminated");

        Assert.NotEmpty(schema.Diagnostics);
    }

    internal static DbmlSchema ParseFixture(string name) =>
        DbmlParser.Parse(File.ReadAllText(Path.Combine(FixtureVaults.FixturesRoot, "dbml", name)));
}
