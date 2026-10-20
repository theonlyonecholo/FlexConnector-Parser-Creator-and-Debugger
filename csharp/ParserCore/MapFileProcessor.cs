using System.Text;

namespace ParserCore;

public sealed record MapDependency(string Stage, string Type, string FileName, string? ResolvedPath, string Status);

/// <summary>Bounded local CSV maps with exact getters, ordered setters and explicit overwrite controls.</summary>
public static class MapFileProcessor
{
    public static IReadOnlyList<MapDependency> Inspect(PropertiesDocument doc, string? root)
    {
        var result = new List<MapDependency>();
        for (int i = 0; i < EventPipeline.Count(doc, "extraprocessor.count", 100); i++)
        {
            string stage = $"extraprocessor[{i}]", prefix = stage + ".";
            string file = doc.Get(prefix + "filename"), type = doc.Get(prefix + "type");
            string? path = null;
            string status = "Ready";
            try
            {
                if (string.IsNullOrWhiteSpace(root))
                    status = "Choose a dependency root directory.";
                else if (file.Length == 0)
                    status = "Missing filename.";
                else
                {
                    string basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
                    path = Path.GetFullPath(Path.Combine(basePath, file.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
                    if (!path.StartsWith(basePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        status = "Reference is outside the selected dependency root.";
                    else if (!File.Exists(path))
                        status = "File not found.";
                    else if (new FileInfo(path).Length > 4 * 1024 * 1024)
                        status = "Map exceeds 4 MB.";
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { status = ex.Message; }
            result.Add(new(stage, type, file, path, status));
        }
        return result;
    }
    public static void Apply(PropertiesDocument doc, MapDependency dependency, Dictionary<string, string?> fields,
        List<EventWrite> trace, CancellationToken cancellation)
    {
        string prefix = dependency.Stage + ".";
        string[] options = ["type", "filename", "allowoverwrite", "casesensitive", "charencoding", "trimgettertokens", "trimsettertokens"];
        var unsupported = doc.Values.Keys.FirstOrDefault(k => k.StartsWith(prefix, StringComparison.Ordinal) && !options.Contains(k[prefix.Length..]));
        if (unsupported is not null)
            throw new NotSupportedException("Unsupported map option: " + unsupported);
        bool Option(string key) => !doc.Values.ContainsKey(prefix + key) || bool.Parse(doc.Get(prefix + key));
        bool overwrite = Option("allowoverwrite"), caseSensitive = Option("casesensitive"),
            trimGetters = Option("trimgettertokens"), trimSetters = Option("trimsettertokens");
        string encodingName = doc.Get(prefix + "charencoding").ToUpperInvariant();
        Encoding encoding = encodingName switch
        {
            "" or "UTF-8" or "UTF8" => new UTF8Encoding(false, true),
            "US-ASCII" or "ASCII" => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            "ISO-8859-1" or "LATIN-1" => Encoding.Latin1,
            _ => throw new NotSupportedException("Unsupported map encoding: " + encodingName)
        };
        using var reader = new StreamReader(dependency.ResolvedPath!, encoding, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[4096];
        var text = new StringBuilder();
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (text.Length + read > 4 * 1024 * 1024)
                throw new FormatException("Map exceeds the preview length limit.");
            text.Append(buffer, 0, read);
        }
        var rows = Csv(text.ToString(), cancellation).ToList();
        if (rows.Count < 2)
            throw new FormatException("Map requires a header and at least one data row.");
        if (rows[0][0] == "!Flags")
        {
            foreach (string flag in rows[0].Skip(1).Select(s => s.Trim()))
            {
                bool value = !flag.EndsWith('-');
                switch (flag.TrimEnd('-'))
                {
                    case "Overwrite":
                        overwrite = value;
                        break;
                    case "CaseSens":
                        caseSensitive = value;
                        break;
                    case "TrimGetters":
                        trimGetters = value;
                        break;
                    case "TrimSetters":
                        trimSetters = value;
                        break;
                    case "EnfrcUniqID":
                        break; // Duplicate getters are always rejected in the local preview.
                    default:
                        throw new NotSupportedException("Unsupported map flag: " + flag);
                }
            }
            rows.RemoveAt(0);
        }
        string[] headers = rows[0].Select(s => s.Trim()).ToArray();
        bool Target(string key) => EventMapping.IsDirectField(key);
        if (headers.Distinct().Count() != headers.Length ||
            headers.Any(h => !Target(h.StartsWith("set.") ? h[4..] : h)))
            throw new NotSupportedException("Only unique event.field getters and set.event.field setters are supported; range/regex/expression headers are unresolved.");
        int[] getters = Enumerable.Range(0, headers.Length).Where(i => !headers[i].StartsWith("set.")).ToArray();
        int[] setters = Enumerable.Range(0, headers.Length).Where(i => headers[i].StartsWith("set.")).ToArray();
        if (setters.Length == 0)
            throw new FormatException("Map has no setters.");
        string Get(string value) => trimGetters ? value.Trim(' ', '\t') : value;
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var keys = new HashSet<string>(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var matches = new List<(string[] Values, int Row)>();
        for (int r = 1; r < rows.Count; r++)
        {
            cancellation.ThrowIfCancellationRequested();
            string[] row = rows[r];
            if (row.Length != headers.Length)
                throw new FormatException($"CSV row {r + 1}: column count mismatch.");
            // Length-prefixed keys prevent collisions when a cell contains a delimiter.
            string key = string.Concat(getters.Select(i => Get(row[i]).Length + ":" + Get(row[i])));
            if (!keys.Add(key))
                throw new FormatException("Duplicate map getters; local evaluation cannot choose an unambiguous row.");
            if (getters.All(i => fields.TryGetValue(headers[i], out var v) && v is not null && string.Equals(Get(v), Get(row[i]), comparison)))
                matches.Add((row, r + 1));
        }
        if (matches.Count == 0)
        {
            trace.Add(new(dependency.Stage, "", null, null, "No map row matched"));
            return;
        }
        var match = matches.Single();
        foreach (int i in setters)
        {
            string field = headers[i][4..], value = trimSetters ? match.Values[i].Trim(' ', '\t') : match.Values[i];
            string? before = fields.GetValueOrDefault(field);
            if (before is not null && !overwrite)
                trace.Add(new(dependency.Stage, field, before, before, $"CSV row {match.Row}: retained (overwrite disabled)"));
            else
            {
                if (EventFieldCatalog.BuiltIn.TryGetValue(field, out var definition) &&
                    definition.Type is "Integer" or "Long" or "IPAddress")
                    value = EventExpressions.Convert(value, definition.Type, "")!;
                fields[field] = value;
                trace.Add(new(dependency.Stage, field, before, value, $"CSV row {match.Row}: assigned"));
            }
        }
    }
    internal static IReadOnlyList<string[]> Csv(string text, CancellationToken cancellation)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var value = new StringBuilder();
        bool quoted = false, closedQuote = false;
        void Cell()
        {
            row.Add(value.ToString());
            value.Clear();
            closedQuote = false;
            if (row.Count > 128)
                throw new FormatException("CSV column limit exceeded.");
        }
        void Row()
        {
            bool content = row.Count > 0 || value.Length > 0 || closedQuote;
            Cell();
            if (content)
                rows.Add(row.ToArray());
            row.Clear();
            if (rows.Count > 10000)
                throw new FormatException("CSV row limit exceeded.");
        }
        for (int i = 0; i < text.Length; i++)
        {
            if (i % 4096 == 0)
                cancellation.ThrowIfCancellationRequested();
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                    closedQuote = true;
                }
                else
                    value.Append(c);
            }
            else if (c == ',')
                Cell();
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                Row();
            }
            else if (c == '"' && value.Length == 0 && !closedQuote)
                quoted = true;
            else if (closedQuote || c == '"')
                throw new FormatException("Malformed quoted CSV cell.");
            else
                value.Append(c);
        }
        if (quoted)
            throw new FormatException("Unclosed CSV quote.");
        if (value.Length > 0 || row.Count > 0 || closedQuote)
            Row();
        return rows;
    }
}
