using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace FamilyTree.Api.RateLimiting;

/// <summary>The "RateLimiting" configuration section. Defaults suit production.</summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per minute for each signed-in user.</summary>
    [Range(1, int.MaxValue)]
    public int SignedInPermitsPerMinute { get; init; } = 120;

    /// <summary>Requests per minute from each address when not signed in.</summary>
    [Range(1, int.MaxValue)]
    public int AnonymousPermitsPerMinute { get; init; } = 30;
}

public static class RateLimitingExtensions
{
    /// <summary>
    /// Limits how often anyone can call the API, so a script or a runaway loop can't
    /// overwhelm it or run up the Azure bill. Each signed-in user has their own
    /// allowance; anonymous callers are limited per address. Over the limit, requests
    /// get 429 Too Many Requests with a Retry-After header.
    /// </summary>
    public static IServiceCollection AddFamilyTreeRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitSettings>()
            .BindConfiguration(RateLimitSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitSettings>>((options, settings) =>
            {
                var limits = settings.Value;
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    context.User.Identity?.IsAuthenticated == true
                        ? RateLimitPartition.GetFixedWindowLimiter(
                            $"user:{context.User.GetObjectId() ?? context.User.Identity.Name}",
                            _ => PerMinute(limits.SignedInPermitsPerMinute))
                        : RateLimitPartition.GetFixedWindowLimiter(
                            $"address:{context.Connection.RemoteIpAddress}",
                            _ => PerMinute(limits.AnonymousPermitsPerMinute)));

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (rejected, cancellationToken) =>
                {
                    var response = rejected.HttpContext.Response;
                    if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                    }

                    var problemDetails = rejected.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                    await problemDetails.WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = rejected.HttpContext,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status429TooManyRequests,
                            Title = "Too many requests",
                            Detail = "Please wait a moment and try again.",
                        },
                    });
                };
            });

        return services;
    }

    private static FixedWindowRateLimiterOptions PerMinute(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    };
}
