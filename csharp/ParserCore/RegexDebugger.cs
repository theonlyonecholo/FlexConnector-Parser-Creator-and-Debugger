using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ParserCore;

public sealed record DebugRow(int Match, int Group, string Name, int Capture, bool Success, int Index, int Length, string Value);
public sealed record TextSpan(int Index, int Length);
public sealed record DebugResult(IReadOnlyList<TextSpan> Matches, IReadOnlyList<DebugRow> Rows,
    string Status, double Milliseconds, string? Error = null, int? ErrorOffset = null);

/// <summary>Runs bounded .NET regex previews and reports capture offsets in the original input.</summary>
public static class RegexDebugger
{
    public const int MaxInputLength = 250_000;
    public const int MaxPatternLength = 16_000;
    public const int MaxRows = 5000;
    public const int MaxMatches = 1000;

    /// <summary>Collects matches and repeated captures; syntax errors and timeouts become diagnostics.</summary>
    /// <remarks>Cancellation propagates to the caller. This is not a Java regex or backtracking simulator.</remarks>
    public static DebugResult Analyze(string pattern, string input, RegexOptions options = RegexOptions.None,
        bool fullEvent = false, bool trim = false, int timeoutMs = 250, CancellationToken cancellation = default)
    {
        var timer = Stopwatch.StartNew();
        var spans = new List<TextSpan>();
        var rows = new List<DebugRow>();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (input.Length > MaxInputLength || pattern.Length > MaxPatternLength)
                throw new ArgumentException($"Preview limit: {MaxInputLength:N0} message characters and {MaxPatternLength:N0} regex characters.");
            timeoutMs = Math.Clamp(timeoutMs, 10, 2000);
            var regex = new Regex(pattern, options | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(timeoutMs));
            // Compile the original first so parse error offsets always refer to the editor text.
            if (fullEvent)
                regex = new Regex(@"\A(?:" + pattern + @")\z", options | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(timeoutMs));
            int offset = 0, end = input.Length;
            if (trim)
            {
                while (offset < end && input[offset] is ' ' or '\t')
                    offset++;
                while (end > offset && input[end - 1] is ' ' or '\t')
                    end--;
            }
            string testInput = input[offset..end];
            int[] numbers = regex.GetGroupNumbers();
            bool limited = false;
            int capturedCharacters = 0;
            for (Match match = regex.Match(testInput); match.Success; match = match.NextMatch())
            {
                cancellation.ThrowIfCancellationRequested();
                if (spans.Count >= MaxMatches || rows.Count >= MaxRows || timer.ElapsedMilliseconds > 2000)
                {
                    limited = true;
                    break;
                }
                int matchNumber = spans.Count + 1;
                spans.Add(new(match.Index + offset, match.Length));
                foreach (int number in numbers)
                {
                    if (rows.Count >= MaxRows)
                    {
                        limited = true;
                        break;
                    }
                    Group group = match.Groups[number];
                    string name = regex.GroupNameFromNumber(number);
                    if (!group.Success)
                        rows.Add(new(matchNumber, number, name, 0, false, -1, 0, ""));
                    else
                        for (int c = 0; c < group.Captures.Count; c++)
                        {
                            if (rows.Count >= MaxRows)
                            {
                                limited = true;
                                break;
                            }
                            Capture capture = group.Captures[c];
                            if (capturedCharacters + capture.Length > 2_000_000)
                            {
                                limited = true;
                                break;
                            }
                            capturedCharacters += capture.Length;
                            rows.Add(new(matchNumber, number, name, c + 1, true, capture.Index + offset, capture.Length, capture.Value));
                        }
                    if (limited)
                        break;
                }
                if (fullEvent || limited)
                    break;
            }
            string status = spans.Count == 0 ? "No match." : $"Matches: {spans.Count}; captures: {rows.Count}.";
            if (limited)
                status += " Preview truncated by a limit; this is not a complete result.";
            if (spans.Any(m => m.Length == 0))
                status += " Zero-length matches have positions in the Debug table but no text to highlight.";
            return new(spans, rows, status, timer.Elapsed.TotalMilliseconds);
        }
        catch (RegexParseException ex)
        {
            return new([], [], "Invalid regex.", timer.Elapsed.TotalMilliseconds, ex.Message, ex.Offset);
        }
        catch (RegexMatchTimeoutException)
        {
            return new([], [], "Regex timeout.", timer.Elapsed.TotalMilliseconds, "Evaluation timed out. Simplify the regex or increase the timeout.");
        }
        catch (ArgumentException ex)
        {
            return new([], [], "Unable to evaluate the regex.", timer.Elapsed.TotalMilliseconds, ex.Message);
        }
    }
}
