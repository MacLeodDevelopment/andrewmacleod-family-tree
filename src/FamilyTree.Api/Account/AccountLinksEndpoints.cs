using System.Security.Claims;
using FamilyTree.Api.Authorization;
using FamilyTree.Domain;
using FamilyTree.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

namespace FamilyTree.Api.Account;

public static class AccountLinksEndpoints
{
    public sealed record MyLink(Guid PersonId, bool IsApproved);

    public sealed record AccountLinkResponse(Guid AccountId, string? DisplayName, string? Username, bool IsApproved, Guid PersonId, string PersonName);

    /// <summary>
    /// Linking a sign-in account to someone in the tree. Family members ask with "This is me",
    /// and an admin approves, so nobody has to find out an account's ID.
    /// </summary>
    public static RouteGroupBuilder MapAccountLinksEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/me/link", GetMineAsync);
        group.MapPost("/people/{id:guid}/claim", ClaimAsync);

        var admin = group.MapGroup("/admin/account-links").RequireAuthorization(FamilyPolicies.Admin);
        admin.MapGet("/", ListAsync);
        admin.MapPost("/{accountId:guid}/approve", ApproveAsync);
        admin.MapDelete("/{accountId:guid}", RemoveAsync);

        return group;
    }

    /// <summary>The person this user has been approved as, if any.</summary>
    internal static async Task<Guid?> ApprovedPersonAsync(ClaimsPrincipal user, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        if (AccountId(user) is not { } accountId)
        {
            return null;
        }

        return await database.AccountLinks
            .Where(link => link.AccountId == accountId && link.IsApproved)
            .Select(link => (Guid?)link.PersonId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static Guid? AccountId(ClaimsPrincipal user) => Guid.TryParse(user.GetObjectId(), out var id) ? id : null;

    private static async Task<IResult> GetMineAsync(ClaimsPrincipal user, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var accountId = AccountId(user);
        var link = await database.AccountLinks.AsNoTracking().SingleOrDefaultAsync(l => l.AccountId == accountId, cancellationToken);

        return link is null ? Results.NoContent() : Results.Ok(new MyLink(link.PersonId, link.IsApproved));
    }

    /// <summary>Asks to be linked to someone. Asking again before approval changes who it's for.</summary>
    private static async Task<IResult> ClaimAsync(Guid id, ClaimsPrincipal user, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        if (AccountId(user) is not { } accountId)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This account has no ID to link");
        }

        if (!await database.People.AnyAsync(person => person.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var link = await database.AccountLinks.SingleOrDefaultAsync(l => l.AccountId == accountId, cancellationToken);
        if (link is { IsApproved: true })
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This account is already linked",
                detail: "Ask an admin to remove the link first.");
        }

        if (link is null)
        {
            link = AccountLink.Request(accountId, id, user.FindFirstValue("name"), user.GetLoginHint());
            database.AccountLinks.Add(link);
        }
        else
        {
            link.ChangePerson(id);
        }

        await database.SaveChangesAsync(cancellationToken);

        return Results.Ok(new MyLink(link.PersonId, link.IsApproved));
    }

    private static async Task<IReadOnlyList<AccountLinkResponse>> ListAsync(FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var links = await database.AccountLinks.AsNoTracking().ToListAsync(cancellationToken);
        var personIds = links.Select(link => link.PersonId).Distinct().ToList();
        var people = await database.People.AsNoTracking().Where(person => personIds.Contains(person.Id)).ToDictionaryAsync(person => person.Id, cancellationToken);

        return links
            .OrderBy(link => link.IsApproved)
            .Select(link =>
            {
                var name = people[link.PersonId].Names.OrderBy(n => n.Position).First();
                var personName = string.Join(' ', new[] { name.GivenName, name.Surname }.Where(part => part is not null));
                return new AccountLinkResponse(link.AccountId, link.DisplayName, link.Username, link.IsApproved, link.PersonId, personName);
            })
            .ToList();
    }

    private static async Task<IResult> ApproveAsync(Guid accountId, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var link = await database.AccountLinks.SingleOrDefaultAsync(l => l.AccountId == accountId, cancellationToken);
        if (link is null)
        {
            return Results.NotFound();
        }

        link.Approve();
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> RemoveAsync(Guid accountId, FamilyTreeDbContext database, CancellationToken cancellationToken)
    {
        var link = await database.AccountLinks.SingleOrDefaultAsync(l => l.AccountId == accountId, cancellationToken);
        if (link is null)
        {
            return Results.NotFound();
        }

        database.AccountLinks.Remove(link);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
