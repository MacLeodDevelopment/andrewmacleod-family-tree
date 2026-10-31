using System.Net;
using FamilyTree.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace FamilyTree.Api.Tests;

public class DatabaseTests(ApiFactory factory, SqlServerContainer sqlServer)
    : IClassFixture<ApiFactory>, IClassFixture<SqlServerContainer>
{
    [Fact]
    public async Task Once_its_database_is_migrated_the_api_reports_ready()
    {
        var api = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:FamilyTree", sqlServer.ConnectionString));

        var response = await api.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Migrating_an_up_to_date_database_again_is_harmless()
    {
        await DatabaseDeployment.MigrateAsync(sqlServer.ConnectionString, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_identity_is_granted_read_and_write_access_and_nothing_more()
    {
        const string identity = "familytree-test-identity";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqlConnection(sqlServer.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Creating the user for real needs an Entra identity (TYPE = E), which only Azure SQL
        // has, so create a plain one here. Everything after that is the same SQL Azure runs.
        await using (var createUser = connection.CreateCommand())
        {
            createUser.CommandText = $"CREATE USER [{identity}] WITHOUT LOGIN";
            await createUser.ExecuteNonQueryAsync(cancellationToken);
        }

        await DatabaseDeployment.GrantReadWriteAsync(sqlServer.ConnectionString, identity, Guid.NewGuid(), cancellationToken);
        await DatabaseDeployment.GrantReadWriteAsync(sqlServer.ConnectionString, identity, Guid.NewGuid(), cancellationToken);

        await using var roles = connection.CreateCommand();
        roles.CommandText = """
            SELECT role.name
            FROM sys.database_role_members AS membership
            JOIN sys.database_principals AS role ON role.principal_id = membership.role_principal_id
            JOIN sys.database_principals AS member ON member.principal_id = membership.member_principal_id
            WHERE member.name = @identity
            ORDER BY role.name
            """;
        roles.Parameters.AddWithValue("@identity", identity);
        var granted = new List<string>();
        await using var reader = await roles.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            granted.Add(reader.GetString(0));
        }

        Assert.Equal(["db_datareader", "db_datawriter"], granted);
    }

    [Fact]
    public async Task The_api_reports_not_ready_when_it_cannot_reach_its_database()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task The_api_reports_itself_running_even_when_its_database_is_unreachable()
    {
        var response = await factory.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void The_api_will_not_start_without_its_database_connection_string()
    {
        using var unconfigured = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:FamilyTree", ""));

        var error = Assert.Throws<OptionsValidationException>(() => unconfigured.CreateClient());

        Assert.Contains("ConnectionStrings:FamilyTree is not configured", error.Message);
    }
}
