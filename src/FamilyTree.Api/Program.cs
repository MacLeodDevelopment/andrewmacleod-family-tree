using Microsoft.AspNetCore.Cors.Infrastructure;

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
        options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).WithMethods("GET"));
    });

var app = builder.Build();

app.UseCors();

app.MapHealthChecks("/health");

app.Run();
