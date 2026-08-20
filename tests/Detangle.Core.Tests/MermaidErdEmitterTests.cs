using Detangle.Core.Dbml;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for the DBML to Mermaid ER translation, including what it deliberately cannot
/// carry across (plan.md section 4.2).
/// </summary>
public class MermaidErdEmitterTests
{
    private static string Ecommerce { get; } =
        MermaidErdEmitter.Emit(DbmlParserTests.ParseFixture("ecommerce.dbml"));

    [Fact]
    public void StartsWithTheErDiagramHeader() =>
        Assert.StartsWith("erDiagram", Ecommerce, StringComparison.Ordinal);

    [Fact]
    public void EmitsOneEntityPerTable()
    {
        Assert.Contains("U {", Ecommerce, StringComparison.Ordinal);
        Assert.Contains("merchants {", Ecommerce, StringComparison.Ordinal);
        Assert.Contains("orders {", Ecommerce, StringComparison.Ordinal);
    }

    [Fact]
    public void UsesTheTableAliasWhereOneWasDeclared()
    {
        // Refs address the alias, so the entity has to carry it or the edges dangle.
        Assert.DoesNotContain("users {", Ecommerce, StringComparison.Ordinal);

        // "Ref: users.id < orders.user_id" names the table; the edge must name the alias,
        // because that is what the entity above it is called.
        Assert.Contains("U ||--o{ orders", Ecommerce, StringComparison.Ordinal);
        Assert.Contains("merchants }o--|| U", Ecommerce, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DbmlCardinality.ManyToOne, "}o--||")]
    [InlineData(DbmlCardinality.OneToMany, "||--o{")]
    [InlineData(DbmlCardinality.OneToOne, "||--||")]
    [InlineData(DbmlCardinality.ManyToMany, "}o--o{")]
    public void MapsEveryCardinality(DbmlCardinality cardinality, string expected) =>
        Assert.Equal(expected, MermaidErdEmitter.SymbolFor(cardinality));

    [Fact]
    public void MarksPrimaryAndForeignKeys()
    {
        Assert.Contains("integer id PK", Ecommerce, StringComparison.Ordinal);
        Assert.Contains("int admin_id FK", Ecommerce, StringComparison.Ordinal);
    }

    [Fact]
    public void MarksUniqueColumnsThatAreNotKeys() =>
        Assert.Contains("username UK", Ecommerce, StringComparison.Ordinal);

    [Fact]
    public void SanitizesTypesThatWouldBreakMermaidsParser()
    {
        // "varchar(255)" is not a legal Mermaid identifier; parentheses and commas have to
        // go, or the whole diagram fails to render rather than one column.
        Assert.DoesNotContain("varchar(255)", Ecommerce, StringComparison.Ordinal);
        Assert.DoesNotContain("(", Ecommerce, StringComparison.Ordinal);
    }

    [Fact]
    public void CarriesColumnNotesAsMermaidComments() =>
        Assert.Contains("\"Login name\"", Ecommerce, StringComparison.Ordinal);

    [Fact]
    public void LabelsRelationshipsWithTheirNameOrColumn()
    {
        Assert.Contains(": \"user_orders\"", Ecommerce, StringComparison.Ordinal);
        Assert.Contains(": \"product_id\"", Ecommerce, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsWhatTheDiagramCannotSay()
    {
        IReadOnlyList<string> missing =
            MermaidErdEmitter.UnrepresentedFeatures(DbmlParserTests.ParseFixture("ecommerce.dbml"));

        Assert.Contains("1 enum", missing);
        Assert.Contains("1 table group", missing);
        Assert.Contains("2 index definitions", missing);
        Assert.Contains("1 sticky note", missing);
        Assert.Contains(missing, m => m.Contains("default value", StringComparison.Ordinal));
    }

    [Fact]
    public void ASchemaWithNothingHiddenReportsNothingMissing() =>
        Assert.Empty(MermaidErdEmitter.UnrepresentedFeatures(
            DbmlParser.Parse("Table a {\n  id int [pk]\n}\n")));

    [Fact]
    public void AnEmptySchemaStillEmitsValidSource() =>
        Assert.Equal("erDiagram\n", MermaidErdEmitter.Emit(DbmlSchema.Empty));
}
