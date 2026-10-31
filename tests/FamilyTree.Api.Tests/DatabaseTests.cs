using System.Net;
using FamilyTree.Infrastructure;
using Microsoft.AspNetCore.Hosting;
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
