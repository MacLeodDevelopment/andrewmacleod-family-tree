namespace FamilyTree.Api.Authorization;

/// <summary>
/// App roles defined on the "Family Tree" app registration (infra/entra.bicep).
/// Entra ID puts the user's assigned roles in the access token's "roles" claim.
/// </summary>
public static class FamilyRoles
{
    public const string Viewer = "Family.Viewer";
    public const string Admin = "Family.Admin";

    public static readonly string[] All = [Viewer, Admin];
}
