using System.Text;
using System.Text.RegularExpressions;

namespace ParserCore;

public sealed record RegexElement(int Start, int Length, string Expression, IReadOnlyList<TextSpan> Captures);
public sealed record ElementDebugResult(IReadOnlyList<RegexElement> Elements, string Status);

/// <summary>Instruments regex atoms to show captures from the final successful match, not backtracking.</summary>
public static class RegexElementDebugger
{
    public static ElementDebugResult Analyze(string pattern, string input, RegexOptions options = RegexOptions.None,
        bool full = false, bool trim = false, int timeoutMs = 250)
    {
        try
        {
            if (pattern.Length > 8000 || input.Length > RegexDebugger.MaxInputLength)
                throw new FormatException("Element preview limit: 8,000 regex characters and 250,000 message characters.");
            if ((options & ~(RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)) != 0)
                throw new FormatException("These regex options are not supported by element preview.");
            var syntax = new Instrumenter(pattern);
            string instrumented = syntax.Parse();
            int offset = trim ? input.Length - input.TrimStart(' ', '\t').Length : 0;
            string text = trim ? input.Trim(' ', '\t') : input;
            string Wrap(string value) => full ? @"\A(?:" + value + @")\z" : value;
            var limit = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 10, 2000));
            var original = new Regex(Wrap(pattern), options | RegexOptions.CultureInvariant, limit).Match(text);
            var match = new Regex(Wrap(instrumented), options | RegexOptions.CultureInvariant, limit).Match(text);
            if (original.Success != match.Success || original.Index != match.Index || original.Length != match.Length)
                throw new FormatException("Instrumentation changed the match; element preview is unavailable for this expression.");
            int count = 0;
            var elements = syntax.Nodes.OrderBy(n => n.Start).ThenByDescending(n => n.Length).Select(n =>
            {
                var spans = match.Groups[n.Name].Captures.Cast<Capture>().Take(5000 - count)
                    .Select(c => new TextSpan(c.Index + offset, c.Length)).ToArray();
                count += spans.Length;
                return new RegexElement(n.Start, n.Length, pattern.Substring(n.Start, n.Length), spans);
            }).ToArray();
            return new(elements, match.Success
                ? "First match: source-order elements and their final captures. Empty spans are assertions; skipped branches have no capture. Not a backtracking trace." + (count >= 5000 ? " Capture display limited to 5,000 spans." : "")
                : "No complete match. No final element captures are available; this does not identify the first failing atom.");
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or RegexMatchTimeoutException)
        {
            return new([], ex.Message);
        }
    }

    private sealed class Instrumenter(string source)
    {
        public readonly List<(int Start, int Length, string Name)> Nodes = [];
        private int position, depth;
        private readonly string prefix = "Trace" + Guid.NewGuid().ToString("N");
        public string Parse()
        {
            string result = Sequence();
            if (position != source.Length)
                throw new FormatException("Unexpected closing parenthesis.");
            return result;
        }
        private string Sequence()
        {
            if (++depth > 64)
                throw new FormatException("Element preview supports at most 64 nested groups.");
            var output = new StringBuilder();
            while (position < source.Length && source[position] != ')')
            {
                if (source[position] == '|')
                {
                    output.Append('|');
                    position++;
                    continue;
                }
                int start = position;
                string atom;
                char current = source[position++];
                if (current == '(')
                {
                    string opener = "(";
                    if (position < source.Length && source[position] == '?')
                    {
                        position++;
                        if (position < source.Length && ":=!>".Contains(source[position]))
                            opener += "?" + source[position++];
                        else if (position + 1 < source.Length && source[position] == '<' && "=!".Contains(source[position + 1]))
                        {
                            opener += "?<" + source[position + 1];
                            position += 2;
                        }
                        else
                            throw new FormatException("Element preview does not support named/balancing groups, conditionals, comments or inline flags. Capture debug remains available.");
                    }
                    string inner = Sequence();
                    if (position >= source.Length || source[position++] != ')')
                        throw new FormatException("Unclosed group.");
                    atom = opener + inner + ")";
                }
                else if (current == '[')
                {
                    if (position < source.Length && source[position] == '^')
                        position++;
                    if (position < source.Length && source[position] == ']')
                        position++;
                    bool closed = false;
                    while (position < source.Length)
                    {
                        char ch = source[position++];
                        if (ch == '\\')
                        {
                            if (position < source.Length)
                                position++;
                        }
                        else if (ch == '[')
                            throw new FormatException("Nested/subtracted character classes are not supported in element preview.");
                        else if (ch == ']')
                        {
                            closed = true;
                            break;
                        }
                    }
                    if (!closed)
                        throw new FormatException("Unclosed character class.");
                    atom = source[start..position];
                }
                else if (current == '\\')
                {
                    if (position >= source.Length)
                        throw new FormatException("Incomplete escape.");
                    char escaped = source[position++];
                    if (char.IsDigit(escaped) || escaped == 'k')
                        throw new FormatException("Backreferences and numeric escapes are not supported in element preview.");
                    if (escaped is 'p' or 'P')
                    {
                        if (position >= source.Length || source[position++] != '{')
                            throw new FormatException("Invalid Unicode category.");
                        while (position < source.Length && source[position] != '}')
                            position++;
                        if (position == source.Length)
                            throw new FormatException("Unclosed Unicode category.");
                        position++;
                    }
                    else if (escaped is 'u' or 'x')
                    {
                        int digits = escaped == 'u' ? 4 : 2;
                        if (position + digits > source.Length)
                            throw new FormatException("Incomplete hexadecimal escape.");
                        position += digits;
                    }
                    else if (escaped == 'c')
                    {
                        if (position == source.Length)
                            throw new FormatException("Incomplete control escape.");
                        position++;
                    }
                    atom = source[start..position];
                }
                else
                {
                    if ("*+?{}".Contains(current))
                        throw new FormatException("Unexpected quantifier.");
                    atom = current.ToString();
                }
                int quantifier = position;
                if (position < source.Length && "*+?".Contains(source[position]))
                    position++;
                else if (position < source.Length && source[position] == '{')
                {
                    while (position < source.Length && source[position] != '}')
                        position++;
                    if (position == source.Length)
                        throw new FormatException("Unclosed quantifier.");
                    position++;
                }
                if (position > quantifier && position < source.Length && source[position] == '?')
                    position++;
                atom += source[quantifier..position];
                if (Nodes.Count >= 512)
                    throw new FormatException("Element preview supports at most 512 elements.");
                string name = prefix + Nodes.Count;
                Nodes.Add((start, position - start, name));
                output.Append("(?<").Append(name).Append('>').Append(atom).Append(')');
            }
            depth--;
            return output.ToString();
        }
    }
}
