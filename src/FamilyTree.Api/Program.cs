using FamilyTree.Api.Account;
using FamilyTree.Api.Authorization;
using FamilyTree.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// Every error response, including bare 401s, 404s and 429s, gets a body in the same
// standard JSON shape (RFC 9457 "problem details"). Unexpected exceptions become a
// plain 500 that reveals nothing about the code.
builder.Services.AddProblemDetails();

// The API sits behind Azure Container Apps' ingress proxy, which passes on the caller's
// real address in X-Forwarded-For. Trust it: the app can only be reached through that
// proxy. (Only the last address is used, so callers can't spoof one.) Without this,
// every caller would appear to come from the proxy, sharing one rate-limit allowance.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

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
            .WithHeaders("Authorization")
            .WithExposedHeaders("Retry-After"));
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

builder.Services.AddFamilyTreeRateLimiting();

var app = builder.Build();

// The order matters: each step wraps the ones after it.
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
// CORS before authentication, so browsers' preflight requests are answered first, and
// so error responses carry CORS headers the browser will let the app read.
app.UseCors();
app.UseAuthentication();
// After authentication, so signed-in users are limited by who they are.
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

app.MapGroup("/api/v1")
    .MapMeEndpoints();

app.Run();
