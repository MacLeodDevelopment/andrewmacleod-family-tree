using FamilyTree.Infrastructure;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace FamilyTree.Api.Tests;

/// <summary>
/// A real SQL Server in Docker for the tests that need a database (Testcontainers),
/// migrated by the same code the deploy pipeline runs. Started once per test class
/// that uses it, and removed afterwards.
/// </summary>
public sealed class SqlServerContainer : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <summary>The family tree database on the container.</summary>
    public string ConnectionString =>
        new SqlConnectionStringBuilder(container.GetConnectionString()) { InitialCatalog = "familytree" }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();
        await DatabaseDeployment.MigrateAsync(ConnectionString);
    }

    public ValueTask DisposeAsync() => container.DisposeAsync();
}
