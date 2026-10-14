using System.Text.RegularExpressions;

namespace ParserCore;

public sealed record RegexGroupRegion(int Start, int End, int Number, string Name);

/// <summary>Source positions of capturing groups, using the engine's actual named-group numbering.</summary>
public static class RegexGroupMap
{
    public static IReadOnlyList<RegexGroupRegion> Build(string pattern)
    {
        if (pattern.Length > RegexDebugger.MaxPatternLength)
            return [];
        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException) { return []; }
        var regions = new List<RegexGroupRegion>();
        var stack = new Stack<(int Start, string? Name, int Number, bool X, bool N)>();
        bool x = false, n = false;
        int next = 1;
        for (int i = 0; i < pattern.Length; i++)
        {
            char ch = pattern[i];
            if (ch == '\\')
            {
                i += i + 1 < pattern.Length && pattern[i + 1] == 'c' ? 2 : 1;
                continue;
            }
            if (x && ch == '#')
            {
                while (i < pattern.Length && pattern[i] != '\n')
                    i++;
                continue;
            }
            if (ch == '[')
            {
                int depth = 1;
                bool first = true;
                bool subtraction = false;
                if (i + 1 < pattern.Length && pattern[i + 1] == '^')
                    i++;
                while (++i < pattern.Length)
                {
                    ch = pattern[i];
                    if (ch == '\\')
                    {
                        i += i + 1 < pattern.Length && pattern[i + 1] == 'c' ? 2 : 1;
                        first = false;
                        subtraction = false;
                        continue;
                    }
                    if (ch == ']' && !first)
                    {
                        if (--depth == 0)
                            break;
                    }
                    if (ch == '[' && subtraction)
                    {
                        depth++;
                        first = true;
                        subtraction = false;
                        if (i + 1 < pattern.Length && pattern[i + 1] == '^')
                            i++;
                        continue;
                    }
                    subtraction = ch == '-';
                    first = false;
                }
                continue;
            }
            if (ch == ')')
            {
                if (!stack.TryPop(out var group))
                    return [];
                if (group.Number > 0)
                    regions.Add(new(group.Start, i, group.Number, regex.GroupNameFromNumber(group.Number)));
                x = group.X;
                n = group.N;
                continue;
            }
            if (ch != '(')
                continue;
            int start = i, number = 0;
            string? name = null;
            bool oldX = x, oldN = n;
            if (i + 1 >= pattern.Length || pattern[i + 1] != '?')
                number = n ? 0 : next++;
            else
            {
                i += 2;
                ch = pattern[i];
                if (ch == '#')
                {
                    while (i < pattern.Length && pattern[i] != ')')
                        i++;
                    continue;
                }
                // Conditional and balancing groups need a full grammar: never guess their positions.
                if (ch == '(')
                    return [];
                if (ch == '\'' || (ch == '<' && pattern[i + 1] is not ('=' or '!')))
                {
                    char end = ch == '\'' ? '\'' : '>';
                    int from = ++i;
                    while (i < pattern.Length && pattern[i] != end)
                        i++;
                    name = pattern[from..i];
                    if (name.Contains('-'))
                        return [];
                    number = regex.GroupNumberFromName(name);
                }
                else if (ch is '<')
                    i++; // Lookbehind.
                else if (ch is not (':' or '=' or '!' or '>'))
                {
                    bool enabled = true;
                    for (; i < pattern.Length && pattern[i] is not (':' or ')'); i++)
                    {
                        if (pattern[i] == '-')
                            enabled = false;
                        if (pattern[i] == 'x')
                            x = enabled;
                        if (pattern[i] == 'n')
                            n = enabled;
                    }
                    if (pattern[i] == ')')
                        continue; // Options affect the rest of the containing group.
                }
            }
            stack.Push((start, name, number, oldX, oldN));
        }
        return regions;
    }

    public static RegexGroupRegion? At(IReadOnlyList<RegexGroupRegion> regions, int position) =>
        regions.Where(g => g.Start <= position && position <= g.End).OrderBy(g => g.End - g.Start).FirstOrDefault();
}
