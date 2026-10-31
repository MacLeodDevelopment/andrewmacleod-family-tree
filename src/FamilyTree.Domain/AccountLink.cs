namespace FamilyTree.Domain;

/// <summary>
/// A sign-in account's claim to be someone in the tree. It means nothing until an admin
/// approves it, because anyone can say they're anyone. Once approved, that account sees
/// the person's own details in full.
/// </summary>
public sealed class AccountLink
{
    public const int MaxNameLength = 200;
    public const int MaxUsernameLength = 320;

    // For Entity Framework, which sets the properties itself.
    private AccountLink()
    {
    }

    /// <summary>The account's object ID in Entra, which unlike an email address never changes or gets reused.</summary>
    public Guid AccountId { get; private set; }

    public Guid PersonId { get; private set; }

    /// <summary>Who the account said it was when asking, so the admin can judge the request.</summary>
    public string? DisplayName { get; private set; }

    public string? Username { get; private set; }

    public bool IsApproved { get; private set; }

    public static AccountLink Request(Guid accountId, Guid personId, string? displayName, string? username) => new()
    {
        AccountId = accountId,
        PersonId = personId,
        DisplayName = Shortened(displayName, MaxNameLength),
        Username = Shortened(username, MaxUsernameLength),
    };

    /// <summary>Changes who an unapproved request is for. An approved link can only be removed by an admin.</summary>
    public void ChangePerson(Guid personId)
    {
        if (IsApproved)
        {
            throw new InvalidOperationException("An approved link can't be changed.");
        }

        PersonId = personId;
    }

    public void Approve() => IsApproved = true;

    private static string? Shortened(string? text, int maxLength) => text?.Length > maxLength ? text[..maxLength] : text;
}
