using System.Globalization;
using System.Text.RegularExpressions;

namespace FamilyTree.Domain;

public enum DateQualifier
{
    Exact,
    About,
    Estimated,
    Calculated,
    Before,
    After,
    Between,
}

/// <summary>
/// A date that may only be known to the month or the year. <see cref="Year"/> is always the
/// year by today's reckoning. <see cref="IsDualYear"/> means it was written both ways, as
/// 1650/51: until 1752 the legal year began on 25 March, so a date early in 1651 by today's
/// reckoning was still 1650 at the time.
/// </summary>
public sealed record PartialDate(int Year, int? Month = null, int? Day = null, bool IsDualYear = false)
{
    public override string ToString()
    {
        var month = Month is { } m ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m) : null;
        var year = IsDualYear ? $"{Year - 1}/{Year % 100:00}" : Year.ToString(CultureInfo.InvariantCulture);
        return string.Join(' ', new object?[] { Day, month, year }.Where(part => part is not null));
    }

    /// <summary>A number that sorts by date, with unknown parts filled in as given.</summary>
    internal int SortValue(int unknownPart) => (Year * 10000) + ((Month ?? unknownPart) * 100) + (Day ?? unknownPart);
}

/// <summary>
/// A date as family history records give it: often approximate, partial or a range. The
/// text as entered is kept, because it's the source: "abt 1870" isn't the same as 1870.
/// Create one with <see cref="Parse"/>.
/// </summary>
public sealed partial record GenealogicalDate
{
    public const int MaxLength = 100;

    private const string Examples = "Try 12 March 1901, Mar 1901, 1901, abt 1870, bef 1901, bet 1870 and 1875, Q2 1880, or 1650/51.";

    /// <summary>The last year Britain used the Julian calendar and the 25 March new year.</summary>
    private const int LastOldStyleYear = 1752;

    private static readonly Dictionary<string, DateQualifier> Prefixes = new()
    {
        ["abt"] = DateQualifier.About,
        ["about"] = DateQualifier.About,
        ["around"] = DateQualifier.About,
        ["approx"] = DateQualifier.About,
        ["c"] = DateQualifier.About,
        ["ca"] = DateQualifier.About,
        ["circa"] = DateQualifier.About,
        ["est"] = DateQualifier.Estimated,
        ["estimated"] = DateQualifier.Estimated,
        ["calc"] = DateQualifier.Calculated,
        ["calculated"] = DateQualifier.Calculated,
        ["bef"] = DateQualifier.Before,
        ["before"] = DateQualifier.Before,
        ["aft"] = DateQualifier.After,
        ["after"] = DateQualifier.After,
    };

    private GenealogicalDate(DateQualifier qualifier, PartialDate start, PartialDate? end, string originalText)
    {
        Qualifier = qualifier;
        Start = start;
        End = end;
        OriginalText = originalText;
    }

    public DateQualifier Qualifier { get; }

    public PartialDate Start { get; }

    /// <summary>The end of the range, for <see cref="DateQualifier.Between"/> only.</summary>
    public PartialDate? End { get; }

    /// <summary>The date as it was entered.</summary>
    public string OriginalText { get; }

    /// <summary>The date for reading, in British style: "c. 1870", "before March 1901".</summary>
    public string Display => Qualifier switch
    {
        DateQualifier.About => $"c. {Start}",
        DateQualifier.Estimated => $"est. {Start}",
        DateQualifier.Calculated => $"calc. {Start}",
        DateQualifier.Before => $"before {Start}",
        DateQualifier.After => $"after {Start}",
        DateQualifier.Between => $"between {Start} and {End}",
        _ => Start.ToString(),
    };

    /// <summary>
    /// Puts dates in order. "before 1900" sorts ahead of anything in 1900, and "after 1900"
    /// behind it; a date known only to the year sorts at the start of that year.
    /// </summary>
    public int SortKey => Qualifier switch
    {
        DateQualifier.Before => Start.SortValue(unknownPart: 0) - 1,
        DateQualifier.After => Start.SortValue(unknownPart: 99) + 1,
        _ => Start.SortValue(unknownPart: 0),
    };

    public static Result<GenealogicalDate> Parse(string text)
    {
        var original = text.Trim();
        var date = original.Length <= MaxLength ? TryParse(original) : null;

        return date is not null
            ? Result<GenealogicalDate>.Success(date)
            : Result<GenealogicalDate>.Failure(new Dictionary<string, string[]>
            {
                ["date"] = [$"\"{original}\" isn't a date this understands. {Examples}"],
            });
    }

    private static GenealogicalDate? TryParse(string original)
    {
        var text = Whitespace().Replace(original.ToLowerInvariant().Replace('–', '-').Replace(',', ' '), " ").Trim();

        if (Quarter().Match(text) is { Success: true } quarter)
        {
            var year = int.Parse(quarter.Groups["year"].Value, CultureInfo.InvariantCulture);
            var lastMonth = int.Parse(quarter.Groups["quarter"].Value, CultureInfo.InvariantCulture) * 3;
            return new GenealogicalDate(
                DateQualifier.Between, new PartialDate(year, lastMonth - 2), new PartialDate(year, lastMonth), original);
        }

        if (Between().Match(text) is { Success: true } between)
        {
            return Range(between.Groups["start"].Value, between.Groups["end"].Value, original);
        }

        if (YearRange().Match(text) is { Success: true } years)
        {
            var start = years.Groups["start"].Value;
            var end = years.Groups["end"].Value;
            return Range(start, end.Length == 2 ? start[..2] + end : end, original);
        }

        if (Prefix().Match(text) is { Success: true } prefix && Prefixes.TryGetValue(prefix.Groups["word"].Value, out var qualifier))
        {
            return ParsePartial(prefix.Groups["date"].Value) is { } qualified
                ? new GenealogicalDate(qualifier, qualified, null, original)
                : null;
        }

        return ParsePartial(text) is { } exact ? new GenealogicalDate(DateQualifier.Exact, exact, null, original) : null;
    }

    private static GenealogicalDate? Range(string startText, string endText, string original)
    {
        var start = ParsePartial(startText);
        var end = ParsePartial(endText);

        return start is not null && end is not null && start.SortValue(unknownPart: 0) <= end.SortValue(unknownPart: 99)
            ? new GenealogicalDate(DateQualifier.Between, start, end, original)
            : null;
    }

    private static PartialDate? ParsePartial(string text)
    {
        if (IsoDate().Match(text) is { Success: true } iso)
        {
            return Valid(Number(iso, "year"), Number(iso, "month"), Number(iso, "day"));
        }

        if (SlashDate().Match(text) is { Success: true } slash)
        {
            return Valid(Number(slash, "year"), Number(slash, "month"), Number(slash, "day"));
        }

        if (WordDate().Match(text) is not { Success: true } words)
        {
            return null;
        }

        var month = MonthNumber(words.Groups["month"].Value);
        if (words.Groups["month"].Success && month is null)
        {
            return null;
        }

        var day = words.Groups["day"].Success ? Number(words, "day") : (int?)null;
        return words.Groups["laterYear"].Success
            ? DualYear(Number(words, "year"), words.Groups["laterYear"].Value, month, day)
            : Valid(Number(words, "year"), month, day);
    }

    /// <summary>
    /// A date written both ways, such as 1731/2 or 1699/00. The later year must follow the
    /// first, and be written in full or by its final digits. Only the start of the year was
    /// ever in doubt, so a known month must be January to March.
    /// </summary>
    private static PartialDate? DualYear(int firstYear, string laterYearText, int? month, int? day)
    {
        var year = firstYear + 1;
        var followsOn = year.ToString(CultureInfo.InvariantCulture).EndsWith(laterYearText, StringComparison.Ordinal);

        return followsOn && year <= LastOldStyleYear && month is null or <= 3
            ? Valid(year, month, day) is { } date ? date with { IsDualYear = true } : null
            : null;
    }

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    private static int? MonthNumber(string word)
    {
        if (word.Length < 3)
        {
            return null;
        }

        var months = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;
        var index = Array.FindIndex(months, name => name.Length > 0
            && (word == name.ToLowerInvariant() || (word.Length == 3 && name.StartsWith(word, StringComparison.OrdinalIgnoreCase))));

        return index >= 0 ? index + 1 : word == "sept" ? 9 : null;
    }

    private static PartialDate? Valid(int year, int? month, int? day)
    {
        var validYear = year is >= 1 and <= 9999;
        var validMonth = month is null or (>= 1 and <= 12);
        var validDay = day is null || (month is { } m && validYear && validMonth && day >= 1 && day <= DaysInMonth(year, m));

        return validYear && validMonth && validDay ? new PartialDate(year, month, day) : null;
    }

    /// <summary>
    /// Britain kept the Julian calendar, where every fourth year is a leap year, until 1752.
    /// Its dates are checked by those rules, so 29 February 1700 is a real date here. Other
    /// countries changed at other times, so nothing narrower than this is enforced: the
    /// days dropped in September 1752 are accepted, because they existed elsewhere.
    /// </summary>
    private static int DaysInMonth(int year, int month) =>
        month == 2 && year <= LastOldStyleYear && year % 4 == 0 ? 29 : DateTime.DaysInMonth(year, month);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^q(?<quarter>[1-4]) (?<year>\d{4})\z")]
    private static partial Regex Quarter();

    [GeneratedRegex(@"^(?:bet|btw|between|from) (?<start>.+?) (?:and|to|-) (?<end>.+)\z")]
    private static partial Regex Between();

    [GeneratedRegex(@"^(?<start>\d{4}) ?- ?(?<end>\d{2}|\d{4})\z")]
    private static partial Regex YearRange();

    [GeneratedRegex(@"^(?<word>[a-z]+)\.? (?<date>.*\d.*)\z")]
    private static partial Regex Prefix();

    [GeneratedRegex(@"^(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})\z")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"^(?<day>\d{1,2})/(?<month>\d{1,2})/(?<year>\d{4})\z")]
    private static partial Regex SlashDate();

    [GeneratedRegex(@"^(?:(?<day>\d{1,2})(?:st|nd|rd|th)? )?(?:(?<month>[a-z]+)\.? )?(?<year>\d{4})(?:/(?<laterYear>\d{1,4}))?\z")]
    private static partial Regex WordDate();
}
