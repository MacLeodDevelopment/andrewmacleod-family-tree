using System.Security.Claims;
using FamilyTree.Api.Authorization;

namespace FamilyTree.Api.Account;

public static class MeEndpoints
{
    /// <param name="Name">Display name from the user's profile; null if they don't have one.</param>
    /// <param name="Username">The account they signed in with (usually an email address); null if the token has none. For display only: it can change, so never use it to decide access.</param>
    /// <param name="Roles">The user's family roles.</param>
    public sealed record MeResponse(string? Name, string? Username, string[] Roles);

    /// <summary>
    /// GET /me: who the signed-in user is and which family roles they have.
    /// Protected by the API's fallback authorisation policy, like every endpoint
    /// that doesn't opt out.
    /// </summary>
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/me", (ClaimsPrincipal user) => new MeResponse(
            Name: ValueOrNull(user.Identity?.Name),
            Username: ValueOrNull(user.FindFirst("preferred_username")?.Value),
            Roles: FamilyRoles.All.Where(user.IsInRole).ToArray()));

        return group;
    }

    private static string? ValueOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
