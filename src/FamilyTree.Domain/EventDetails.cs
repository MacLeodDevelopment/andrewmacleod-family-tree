namespace FamilyTree.Domain;

/// <summary>The rules for when and where something happened, shared by every kind of event.</summary>
internal static class EventDetails
{
    public const int MaxPlaceLength = 200;

    /// <summary>The date, if there is one, adding anything wrong with the details to <paramref name="errors"/>.</summary>
    public static GenealogicalDate? Read(string? dateText, string? place, Dictionary<string, string[]> errors)
    {
        GenealogicalDate? date = null;
        if (!string.IsNullOrWhiteSpace(dateText))
        {
            var parsed = GenealogicalDate.Parse(dateText);
            if (parsed.IsSuccess)
            {
                date = parsed.Value;
            }
            else
            {
                errors["date"] = parsed.Errors["date"];
            }
        }

        if (place?.Trim().Length > MaxPlaceLength)
        {
            errors["place"] = [$"A place can't be longer than {MaxPlaceLength} characters."];
        }

        return date;
    }

    public static string? CleanPlace(string? place) => string.IsNullOrWhiteSpace(place) ? null : place.Trim();
}
