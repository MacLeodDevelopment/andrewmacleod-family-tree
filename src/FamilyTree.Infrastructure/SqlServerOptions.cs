using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure;

public static class SqlServerOptions
{
    /// <summary>
    /// How the API and the migrator connect to SQL Server, so they behave the same.
    /// Retries cover transient failures, which include Azure SQL's free tier waking
    /// up: it pauses when idle, and its first connections after a pause fail for a
    /// short while.
    /// </summary>
    public static DbContextOptionsBuilder UseFamilyTreeSqlServer(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null));

    public static DbContextOptions<FamilyTreeDbContext> ForFamilyTree(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<FamilyTreeDbContext>();
        builder.UseFamilyTreeSqlServer(connectionString);
        return builder.Options;
    }
}
