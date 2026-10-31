using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Infrastructure;

/// <summary>
/// What the deploy pipeline does to the database, as the database administrator:
/// bring the schema up to date, and let the API read and write data (but nothing
/// more). Run by the FamilyTree.DbMigrator tool.
/// </summary>
public static partial class DatabaseDeployment
{
    /// <summary>
    /// Applies any migrations the database doesn't have yet, creating the database first
    /// if it doesn't exist (locally; in Azure, Bicep creates it). Safe to run repeatedly.
    /// </summary>
    public static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var database = new FamilyTreeDbContext(SqlServerOptions.ForFamilyTree(connectionString));
        await database.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Makes sure a Microsoft Entra managed identity is a user in the database, with
    /// read and write access to data only: it can't change the schema or permissions.
    /// Safe to run repeatedly. Azure SQL only.
    /// </summary>
    /// <remarks>
    /// The user is created from the identity's client ID (its SID), rather than with
    /// <c>FROM EXTERNAL PROVIDER</c>, which would make SQL look the name up in Entra and
    /// so need the SQL server to have permission to read the directory.
    /// </remarks>
    public static async Task GrantReadWriteAsync(
        string connectionString, string identityName, Guid identityClientId, CancellationToken cancellationToken = default)
    {
        if (!SafeName().IsMatch(identityName))
        {
            throw new ArgumentException($"'{identityName}' isn't a valid identity name.", nameof(identityName));
        }

        // EXEC (...) only accepts strings and variables, not function calls, so each
        // statement is built in a variable first.
        const string sql = """
            DECLARE @user nvarchar(258) = QUOTENAME(@name);
            DECLARE @statement nvarchar(max);

            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)
            BEGIN
                DECLARE @sid nvarchar(100) = CONVERT(nvarchar(100), CONVERT(varbinary(16), @clientId), 1);
                SET @statement = N'CREATE USER ' + @user + N' WITH SID = ' + @sid + N', TYPE = E';
                EXEC (@statement);
            END;

            SET @statement = N'ALTER ROLE db_datareader ADD MEMBER ' + @user;
            EXEC (@statement);
            SET @statement = N'ALTER ROLE db_datawriter ADD MEMBER ' + @user;
            EXEC (@statement);
            """;

        await using var database = new FamilyTreeDbContext(SqlServerOptions.ForFamilyTree(connectionString));
        await database.Database.ExecuteSqlRawAsync(
            sql,
            [new SqlParameter("@name", identityName), new SqlParameter("@clientId", identityClientId)],
            cancellationToken);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex SafeName();
}
