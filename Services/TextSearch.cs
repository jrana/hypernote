using System.Text.RegularExpressions;

namespace HyperNote.Services;

/// <summary>Search options surfaced in the Find/Replace UI.</summary>
public readonly record struct SearchOptions(bool MatchCase, bool WholeWord, bool Backward, bool UseRegex = false);

/// <summary>
/// Stateless find/replace over a string. Backed by <see cref="Regex"/> with the
/// search term escaped (so it's treated literally, not as a pattern). Kept free of
/// any WPF/editor dependency so the matching rules can be unit-tested directly.
/// </summary>
public static class TextSearch
{
    public static Regex BuildRegex(string term, bool matchCase, bool wholeWord, bool useRegex = false)
    {
        var opts = RegexOptions.CultureInvariant;
        if (!matchCase) opts |= RegexOptions.IgnoreCase;

        string pattern = useRegex ? term : Regex.Escape(term);
        if (wholeWord)
            // Lookarounds instead of \b so terms that begin/end with non-word
            // characters still match sensibly.
            pattern = $@"(?<!\w){pattern}(?!\w)";

        return new Regex(pattern, opts);
    }

    /// <summary>All non-overlapping matches, in ascending offset order.</summary>
    public static IReadOnlyList<(int Index, int Length)> FindAll(string text, string term,
                                                                 bool matchCase, bool wholeWord, bool useRegex = false)
    {
        var list = new List<(int, int)>();
        if (string.IsNullOrEmpty(term)) return list;

        foreach (Match m in BuildRegex(term, matchCase, wholeWord, useRegex).Matches(text))
            if (m.Length > 0) list.Add((m.Index, m.Length));
        return list;
    }

    /// <summary>
    /// Finds the next match relative to <paramref name="start"/>. Forward searches
    /// at/after <paramref name="start"/>; backward searches strictly before it.
    /// Wraps around the document when <paramref name="wrap"/> is set.
    /// </summary>
    public static (int Index, int Length)? Find(string text, string term, int start,
                                                SearchOptions o, bool wrap)
    {
        if (string.IsNullOrEmpty(term)) return null;
        start = Math.Clamp(start, 0, text.Length);

        if (!o.Backward)
        {
            var re = BuildRegex(term, o.MatchCase, o.WholeWord, o.UseRegex);
            var m = re.Match(text, start);
            if (!m.Success && wrap) m = re.Match(text, 0);
            return m.Success ? (m.Index, m.Length) : null;
        }

        // Backward: take the last forward match that starts before `start`.
        // (Selecting from the full forward list keeps whole-word boundaries correct,
        // which a windowed RightToLeft match can get wrong at the window edge.)
        var all = FindAll(text, term, o.MatchCase, o.WholeWord, o.UseRegex);
        (int, int)? best = null;
        foreach (var mt in all)
        {
            if (mt.Item1 < start) best = mt;
            else break;
        }
        if (best == null && wrap && all.Count > 0) best = all[^1];
        return best;
    }
}
