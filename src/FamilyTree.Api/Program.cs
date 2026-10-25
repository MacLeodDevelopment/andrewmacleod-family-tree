using FamilyTree.Api.Account;
using FamilyTree.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// The SPA is served from a different origin to the API, so only its origin(s)
// may call the API from a browser. Origins come from configuration
// (Cors:AllowedOrigins), which is set per environment by the infrastructure.
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IConfiguration>((options, configuration) =>
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        options.AddDefaultPolicy(policy => policy
            .WithOrigins(allowedOrigins)
            .WithMethods("GET")
            .WithHeaders("Authorization"));
    });

// Sign-in: validate bearer tokens issued by Microsoft Entra ID for this API, using
// the "AzureAd" configuration section. Fail at startup if it's missing, rather than
// on the first request.
builder.Services.AddOptions<EntraSettings>()
    .BindConfiguration(EntraSettings.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, EntraSettings.SectionName);

// Deny by default: every endpoint requires a signed-in user with a family role,
// unless it explicitly allows anonymous access.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(FamilyRoles.All)
        .Build());

var app = builder.Build();

// CORS runs first, so browsers' preflight requests are answered before authentication.
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

app.MapGroup("/api/v1")
    .MapMeEndpoints();

app.Run();
