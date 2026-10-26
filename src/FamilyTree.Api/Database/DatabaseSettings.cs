using System.ComponentModel.DataAnnotations;
using FamilyTree.Infrastructure;
using Microsoft.Extensions.Options;

namespace FamilyTree.Api.Database;

/// <summary>
/// The database connection string, from ConnectionStrings:FamilyTree. In Azure, Bicep
/// sets it (managed identity, no password); locally it's in appsettings.Development.json.
/// </summary>
public sealed class DatabaseSettings
{
    public const string ConnectionStringName = "FamilyTree";

    [Required(ErrorMessage = "ConnectionStrings:FamilyTree is not configured. See README, \"The database\".")]
    public string? ConnectionString { get; set; }
}

public static class DatabaseRegistration
{
    /// <summary>
    /// The family tree database, failing at startup if the connection string is missing,
    /// rather than on the first request that needs it.
    /// </summary>
    public static IServiceCollection AddFamilyTreeDatabase(this IServiceCollection services)
    {
        services.AddOptions<DatabaseSettings>()
            .Configure<IConfiguration>((settings, configuration) =>
                settings.ConnectionString = configuration.GetConnectionString(DatabaseSettings.ConnectionStringName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<FamilyTreeDbContext>((provider, options) =>
            options.UseFamilyTreeSqlServer(provider.GetRequiredService<IOptions<DatabaseSettings>>().Value.ConnectionString!));

        return services;
    }
}
