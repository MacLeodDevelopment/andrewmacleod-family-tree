using System.ComponentModel.DataAnnotations;

namespace FamilyTree.Api.Authorization;

/// <summary>
/// The "AzureAd" configuration section read by Microsoft.Identity.Web. Bound here only
/// so the API refuses to start, with a clear message, when sign-in isn't configured.
/// </summary>
public sealed class EntraSettings
{
    public const string SectionName = "AzureAd";

    [Required(ErrorMessage = "AzureAd:TenantId is not configured. See docs/setup-entra.md.")]
    public string? TenantId { get; init; }

    [Required(ErrorMessage = "AzureAd:ClientId is not configured. See docs/setup-entra.md.")]
    public string? ClientId { get; init; }
}
