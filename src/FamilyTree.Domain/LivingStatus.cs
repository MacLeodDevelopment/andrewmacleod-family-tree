namespace FamilyTree.Domain;

/// <summary>
/// Who might still be alive, which decides whose details are kept private. Every doubt
/// falls on the side of "living": wrongly hiding someone from long ago is a nuisance, but
/// wrongly showing a living relative's details can't be undone.
/// </summary>
public static class LivingStatus
{
    /// <summary>Longer than anyone is expected to live.</summary>
    public const int LifespanYears = 110;

    /// <summary>How late an approximate year might really be.</summary>
    public const int ApproximateYears = 10;

    /// <summary>
    /// False only for someone with a recorded death, or who was born too long ago to be alive.
    /// With no birth date to go on, they're treated as living.
    /// </summary>
    public static bool MayBeLiving(this Person person, int currentYear)
    {
        if (person.Events.Any(lifeEvent => lifeEvent.Type == EventType.Death))
        {
            return false;
        }

        var birth = person.Events.FirstOrDefault(lifeEvent => lifeEvent.Type == EventType.Birth)?.Date;

        return birth is null || LatestYear(birth) + LifespanYears >= currentYear;
    }

    /// <summary>The latest year a date could really mean. "After 1890" has no limit.</summary>
    private static long LatestYear(GenealogicalDate date) => date.Qualifier switch
    {
        DateQualifier.After => int.MaxValue,
        DateQualifier.About or DateQualifier.Estimated or DateQualifier.Calculated => date.Start.Year + ApproximateYears,
        DateQualifier.Between => date.End!.Year,
        _ => date.Start.Year,
    };
}
