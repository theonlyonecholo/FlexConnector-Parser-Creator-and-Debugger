using System.Globalization;
using System.Text;

namespace ParserCore;

/// <summary>Java properties syntax; keeps untouched source blocks and comments.</summary>
public sealed class PropertiesDocument
{
    private sealed record Entry(string Raw, string? Key, string? Value);
    private readonly List<Entry> entries = [];
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public string Source => string.Concat(entries.Select(e => e.Raw));
    public string Get(string key) => Values.GetValueOrDefault(key, "");

    public PropertiesDocument(string source)
    {
        var lines = new List<string>();
        for (int start = 0, i = 0; i < source.Length; i++)
        {
            if (source[i] is not ('\r' or '\n'))
                continue;
            if (source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
                i++;
            lines.Add(source[start..(i + 1)]);
            start = i + 1;
            if (start == source.Length)
                break;
        }
        int consumed = lines.Sum(l => l.Length);
        if (consumed < source.Length)
            lines.Add(source[consumed..]);
        for (int i = 0; i < lines.Count;)
        {
            var raw = new StringBuilder();
            var logical = new StringBuilder();
            bool first = true;
            while (i < lines.Count)
            {
                string line = lines[i++];
                raw.Append(line);
                string part = line.TrimEnd('\r', '\n').TrimStart(' ', '\t', '\f');
                if (first && (part.Length == 0 || part[0] is '#' or '!'))
                    break;
                first = false;
                int slashes = part.Length - part.TrimEnd('\\').Length;
                bool continued = slashes % 2 != 0;
                logical.Append(continued ? part[..^1] : part);
                if (!continued)
                    break;
            }
            if (first)
            {
                entries.Add(new(raw.ToString(), null, null));
                continue;
            }
            string s = logical.ToString();
            int k = 0;
            bool escaped = false;
            while (k < s.Length)
            {
                if (!escaped && (White(s[k]) || s[k] is '=' or ':'))
                    break;
                escaped = s[k] == '\\' && !escaped;
                k++;
            }
            int v = k;
            while (v < s.Length && White(s[v]))
                v++;
            if (v < s.Length && s[v] is '=' or ':')
                v++;
            while (v < s.Length && White(s[v]))
                v++;
            string key = Decode(s[..k]), value = Decode(s[v..]);
            entries.Add(new(raw.ToString(), key, value));
            Values[key] = value;
        }
    }

    private static bool White(char c) => c is ' ' or '\t' or '\f';
    /// <summary>Decodes Java properties escapes, including four-digit Unicode escapes.</summary>
    public static string Decode(string s)
    {
        var b = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\')
            {
                b.Append(c);
                continue;
            }
            if (++i == s.Length)
                break;
            c = s[i];
            if (c == 'u')
            {
                if (i + 4 >= s.Length || !ushort.TryParse(s.AsSpan(i + 1, 4),
                    NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                    throw new FormatException("Neplatná Unicode escape sekvence v properties.");
                b.Append((char)code);
                i += 4;
            }
            else
                b.Append(c switch
                {
                    't' => '\t',
                    'r' => '\r',
                    'n' => '\n',
                    'f' => '\f',
                    _ => c
                });
        }
        return b.ToString();
    }

    /// <summary>Encodes a decoded property value for ASCII-safe output.</summary>
    public static string Escape(string value)
    {
        var b = new StringBuilder();
        foreach (char c in value)
            b.Append(c switch
            {
                '\\' => @"\\",
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                '\f' => @"\f",
                ' ' or '=' or ':' or '#' or '!' => "\\" + c,
                < ' ' or > '~' => "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture),
                _ => c.ToString()
            });
        return b.ToString();
    }

    public void RemoveWhere(Func<string, bool> predicate)
    {
        entries.RemoveAll(e => e.Key is not null && predicate(e.Key));
        foreach (string key in Values.Keys.Where(predicate).ToArray())
            Values.Remove(key);
    }
    /// <summary>Replaces a key in place, removes duplicate definitions, or deletes the key when value is null.</summary>
    public void Set(string key, string? value)
    {
        if (value is null)
        {
            RemoveWhere(k => k == key);
            return;
        }
        string line = Escape(key) + "=" + Escape(value) + "\r\n";
        int first = entries.FindIndex(e => e.Key == key);
        if (first >= 0)
        {
            entries[first] = new(line, key, value);
            for (int i = entries.Count - 1; i > first; i--)
                if (entries[i].Key == key)
                    entries.RemoveAt(i);
        }
        else
        {
            string boundary = Boundary(Source);
            if (entries.Count > 0 && boundary.Length > 0)
                entries[^1] = entries[^1] with
                {
                    Raw = entries[^1].Raw + boundary
                };
            entries.Add(new(line, key, value));
        }
        Values[key] = value;
    }

    // Add only the required line boundary. An unfinished continuation requires
    // one extra empty physical line, otherwise it would consume the next key.
    private static string Boundary(string source)
    {
        if (source.Length == 0)
            return "";
        bool newline = source[^1] is '\r' or '\n';
        string last = source;
        if (last.EndsWith("\r\n", StringComparison.Ordinal))
            last = last[..^2];
        else if (newline)
            last = last[..^1];
        int start = Math.Max(last.LastIndexOf('\n'), last.LastIndexOf('\r')) + 1;
        last = last[start..];
        int slashes = last.Length - last.TrimEnd('\\').Length;
        string trimmed = last.TrimStart(' ', '\t', '\f');
        bool continuation = slashes % 2 == 1 && !(trimmed.StartsWith('#') || trimmed.StartsWith('!'));
        return (newline ? "" : "\r\n") + (continuation ? "\r\n" : "");
    }
    public static string JoinSources(string source, string addition) => addition.Length == 0 ? source : source + Boundary(source) + addition;

    public static PropertiesDocument Load(string path)
    {
        using var reader = new StreamReader(path, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
        return new(reader.ReadToEnd());
    }

    /// <summary>Writes ASCII with Unicode escapes through a temporary file in the destination directory.</summary>
    public void Save(string path)
    {
        // Re-encode only non-ASCII property blocks, so an escaped non-ASCII character
        // does not turn into a literal backslash + u on export. Comments stay UTF-8-safe ASCII.
        var b = new StringBuilder();
        foreach (var e in entries)
        {
            if (e.Raw.All(c => c <= 127))
                b.Append(e.Raw);
            else if (e.Key is not null)
                b.Append(Escape(e.Key)).Append('=').Append(Escape(e.Value!)).Append("\r\n");
            else
                foreach (char c in e.Raw)
                    b.Append(c <= 127 ? c.ToString() : "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }
        string full = Path.GetFullPath(path);
        string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, b.ToString(), Encoding.ASCII);
            File.Move(temp, full, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
