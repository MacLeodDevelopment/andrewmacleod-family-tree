using System.Security.Claims;
using FamilyTree.Api.Authorization;

namespace FamilyTree.Api.Account;

public static class MeEndpoints
{
    public sealed record MeResponse(string Name, string[] Roles);

    /// <summary>
    /// GET /me: who the signed-in user is and which family roles they have.
    /// Protected by the API's fallback authorisation policy, like every endpoint
    /// that doesn't opt out.
    /// </summary>
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/me", (ClaimsPrincipal user) => new MeResponse(
            Name: user.Identity?.Name ?? string.Empty,
            Roles: FamilyRoles.All.Where(user.IsInRole).ToArray()));

        return group;
    }
}
