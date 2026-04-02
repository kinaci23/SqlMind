using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SqlMind.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core CLI tools (dotnet ef migrations add / database update).
/// Reads the connection string from the DATABASE_URL environment variable.
/// If DATABASE_URL is not set, falls back to a local dev connection string.
/// </summary>
public sealed class SqlMindDbContextFactory : IDesignTimeDbContextFactory<SqlMindDbContext>
{
    public SqlMindDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=sqlmind;Username=sqlmind;Password=sqlmind_secret";

        var options = new DbContextOptionsBuilder<SqlMindDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options;

        return new SqlMindDbContext(options);
    }
}
