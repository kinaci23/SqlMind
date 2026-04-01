using SqlMind.Core.Enums;
using SqlMind.Infrastructure.SqlParsing;

namespace SqlMind.UnitTests;

public sealed class SqlParserTests
{
    private readonly CustomSqlAnalyzer _analyzer = new();

    // -------------------------------------------------------------------------
    // DELETE
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_WithoutWhere_ShouldSetUnfilteredMutation()
    {
        var result = await _analyzer.ParseAsync("DELETE FROM users");

        Assert.Contains(OperationType.DELETE, result.Operations);
        Assert.False(result.WhereClauseExists);
        Assert.True(result.HasUnfilteredMutation);
    }

    [Fact]
    public async Task Delete_WithWhere_ShouldNotSetUnfilteredMutation()
    {
        var result = await _analyzer.ParseAsync("DELETE FROM users WHERE id = 1");

        Assert.Contains(OperationType.DELETE, result.Operations);
        Assert.True(result.WhereClauseExists);
        Assert.False(result.HasUnfilteredMutation);
    }

    [Fact]
    public async Task Delete_ShouldExtractTableName()
    {
        var result = await _analyzer.ParseAsync("DELETE FROM users WHERE id = 1");

        Assert.Contains("USERS", result.TablesDetected);
    }

    // -------------------------------------------------------------------------
    // UPDATE
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Update_WithoutWhere_ShouldSetUnfilteredMutation()
    {
        var result = await _analyzer.ParseAsync("UPDATE orders SET status = 'x'");

        Assert.Contains(OperationType.UPDATE, result.Operations);
        Assert.False(result.WhereClauseExists);
        Assert.True(result.HasUnfilteredMutation);
    }

    [Fact]
    public async Task Update_WithWhere_ShouldNotSetUnfilteredMutation()
    {
        var result = await _analyzer.ParseAsync("UPDATE orders SET status = 'x' WHERE id = 1");

        Assert.Contains(OperationType.UPDATE, result.Operations);
        Assert.True(result.WhereClauseExists);
        Assert.False(result.HasUnfilteredMutation);
    }

    // -------------------------------------------------------------------------
    // DDL
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DropTable_ShouldSetDdlAndDropFlags()
    {
        var result = await _analyzer.ParseAsync("DROP TABLE customers");

        Assert.Contains(OperationType.DDL, result.Operations);
        Assert.True(result.HasDdlOperation);
        Assert.True(result.HasDropStatement);
        Assert.Contains("CUSTOMERS", result.TablesDetected);
    }

    [Fact]
    public async Task Truncate_ShouldSetDdlAndTruncateFlags()
    {
        var result = await _analyzer.ParseAsync("TRUNCATE TABLE orders");

        Assert.Contains(OperationType.DDL, result.Operations);
        Assert.True(result.HasDdlOperation);
        Assert.True(result.HasTruncateStatement);
    }

    [Fact]
    public async Task AlterTable_ShouldSetDdlAndAlterFlags()
    {
        var result = await _analyzer.ParseAsync("ALTER TABLE products ADD COLUMN price DECIMAL(10,2)");

        Assert.Contains(OperationType.DDL, result.Operations);
        Assert.True(result.HasDdlOperation);
        Assert.True(result.HasAlterStatement);
    }

    // -------------------------------------------------------------------------
    // SELECT
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Select_Simple_ShouldBeLowRisk()
    {
        var result = await _analyzer.ParseAsync("SELECT * FROM products");

        Assert.Contains(OperationType.SELECT, result.Operations);
        Assert.False(result.HasUnfilteredMutation);
        Assert.False(result.HasDdlOperation);
        Assert.False(result.JoinExists);
        Assert.Contains("PRODUCTS", result.TablesDetected);
    }

    [Fact]
    public async Task Select_WithJoin_ShouldSetJoinFlag()
    {
        var result = await _analyzer.ParseAsync(
            "SELECT * FROM orders o JOIN customers c ON o.customer_id = c.id");

        Assert.Contains(OperationType.SELECT, result.Operations);
        Assert.True(result.JoinExists);
        Assert.Contains("ORDERS", result.TablesDetected);
        Assert.Contains("CUSTOMERS", result.TablesDetected);
    }

    // -------------------------------------------------------------------------
    // Multi-statement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MultiStatement_ShouldDetectAllOperations()
    {
        var result = await _analyzer.ParseAsync(
            "SELECT * FROM users; DELETE FROM sessions WHERE expired = 1");

        Assert.Contains(OperationType.SELECT, result.Operations);
        Assert.Contains(OperationType.DELETE, result.Operations);
        // DELETE has WHERE, so no unfiltered mutation
        Assert.False(result.HasUnfilteredMutation);
        Assert.True(result.WhereClauseExists);
    }

    // -------------------------------------------------------------------------
    // Comments
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LineComment_ShouldBeIgnored()
    {
        var result = await _analyzer.ParseAsync(
            "-- this is a comment\nSELECT * FROM products");

        Assert.Contains(OperationType.SELECT, result.Operations);
    }

    [Fact]
    public async Task BlockComment_ShouldBeIgnored()
    {
        var result = await _analyzer.ParseAsync(
            "/* fetch all */ SELECT * FROM products");

        Assert.Contains(OperationType.SELECT, result.Operations);
    }

    // -------------------------------------------------------------------------
    // Quoted identifiers
    // -------------------------------------------------------------------------

    [Fact]
    public async Task QuotedIdentifier_BracketSyntax_ShouldExtractTableName()
    {
        var result = await _analyzer.ParseAsync("SELECT * FROM [my_schema].[orders]");

        Assert.Contains("ORDERS", result.TablesDetected);
    }
}
