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

/// <summary>A date that may only be known to the month or the year.</summary>
public sealed record PartialDate(int Year, int? Month = null, int? Day = null)
{
    public override string ToString()
    {
        var month = Month is { } m ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m) : null;
        return string.Join(' ', new object?[] { Day, month, Year }.Where(part => part is not null));
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

    private const string Examples = "Try 12 March 1901, Mar 1901, 1901, abt 1870, bef 1901, bet 1870 and 1875, or Q2 1880.";

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

        return Valid(Number(words, "year"), month, words.Groups["day"].Success ? Number(words, "day") : null);
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
        var validDay = day is null || (month is { } m && validYear && validMonth && day >= 1 && day <= DateTime.DaysInMonth(year, m));

        return validYear && validMonth && validDay ? new PartialDate(year, month, day) : null;
    }

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

    [GeneratedRegex(@"^(?:(?<day>\d{1,2})(?:st|nd|rd|th)? )?(?:(?<month>[a-z]+)\.? )?(?<year>\d{4})\z")]
    private static partial Regex WordDate();
}
