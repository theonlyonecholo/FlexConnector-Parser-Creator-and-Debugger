using System.Text;

namespace ParserCore;

public sealed record LogEntry(int LineNumber, string Text);
/// <summary>Loads bounded, line-oriented log samples while retaining original line numbers and blank lines.</summary>
public static class LogFile
{
    public static IReadOnlyList<LogEntry> Read(string path, bool latin1 = false, CancellationToken cancellation = default)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024)
            throw new FormatException("Limit souboru pro náhled je 32 MB.");
        var entries = new List<LogEntry>();
        using var reader = new StreamReader(path, latin1 ? Encoding.Latin1 : new UTF8Encoding(false, true), true);
        string? line;
        int number = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellation.ThrowIfCancellationRequested();
            number++;
            if (number > 50000)
                throw new FormatException("Limit náhledu je 50 000 řádků.");
            entries.Add(new(number, line)); // Includes blank lines; line numbers remain faithful to the file.
        }
        return entries;
    }
}
