using System.Text.Json;
using System.Text.RegularExpressions;

namespace ParserCore;

public sealed record RegexPreset(string Id, string Name, string Pattern, string Description)
{
    public override string ToString() => Name;
}

/// <summary>Local, editable regex library. Loading invalid data never silently resets the user's file.</summary>
public sealed class RegexPresetStore(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    private sealed record Library(int Version, List<RegexPreset> Presets);
    public List<RegexPreset> Load()
    {
        if (!File.Exists(Path))
            return Defaults();
        if (new FileInfo(Path).Length > 4 * 1024 * 1024)
            throw new FormatException("The regex library exceeds 4 MB.");
        var library = JsonSerializer.Deserialize<Library>(File.ReadAllText(Path))
            ?? throw new FormatException("The regex library is empty.");
        if (library.Version != 1 || library.Presets is null)
            throw new FormatException("Unsupported regex library format.");
        Validate(library.Presets);
        return library.Presets;
    }
    public void Save(IReadOnlyList<RegexPreset> presets)
    {
        Validate(presets);
        string data = JsonSerializer.Serialize(new Library(1, presets.ToList()), new JsonSerializerOptions { WriteIndented = true });
        if (System.Text.Encoding.UTF8.GetByteCount(data) > 4 * 1024 * 1024)
            throw new FormatException("The regex library exceeds 4 MB.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, data);
            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
    private static void Validate(IReadOnlyList<RegexPreset> presets)
    {
        if (presets.Count > 200 || presets.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) ||
            string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 100 || p.Description is null || p.Description.Length > 2000) ||
            presets.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != presets.Count ||
            presets.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != presets.Count)
            throw new FormatException("Use unique preset names (1–100 characters), IDs, and at most 200 presets.");
        foreach (var preset in presets)
            RegexPresetEditing.Compile(preset.Pattern);
    }
    public static List<RegexPreset> Defaults()
    {
        const string octet = @"(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])";
        string ipv4 = $@"{octet}(?:\.{octet}){{3}}";
        const string h = "[0-9A-Fa-f]{1,4}";
        // IPv6 ABNF alternatives from RFC 3986 section 3.2.2, including an IPv4 tail.
        string ls32 = $"(?:{h}:{h}|{ipv4})";
        var alternatives = new List<string> { $"(?:{h}:){{6}}{ls32}", $"::(?:{h}:){{5}}{ls32}" };
        for (int n = 0; n <= 4; n++)
            alternatives.Add($"(?:(?:{h}:){{0,{n}}}{h})?::(?:{h}:){{{4 - n}}}{ls32}");
        alternatives.Add($"(?:(?:{h}:){{0,5}}{h})?::{h}");
        alternatives.Add($"(?:(?:{h}:){{0,6}}{h})?::");
        const string label = @"[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?";
        return
        [
            new("ipv4", "IPv4", ipv4, "Four decimal octets, 0–255; no leading zeros except 0."),
            new("ipv6", "IPv6", "(?:" + string.Join("|", alternatives) + ")", "Full or compressed IPv6, including an IPv4 tail. No brackets, zone ID or CIDR suffix. RFC 3986 §3.2.2."),
            new("fqdn", "FQDN (ASCII labels)", $@"(?:{label}\.)+{label}\.?", "Two or more ASCII labels, each up to 63 characters; optional final dot. Punycode is accepted. Total DNS name length and DNS existence are not checked."),
            new("mac", "MAC address", @"(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}", "Six colon-separated hexadecimal octets."),
            new("integer", "Unsigned integer", "[0-9]+", "One or more decimal digits; no numeric range limit."),
            new("uuid", "UUID (text shape)", "[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}", "Hyphenated 8-4-4-4-12 hexadecimal form; version and variant are not constrained.")
        ];
    }
}

/// <summary>Tests selected literal text before proposing a regex substitution. Never evaluates regex equivalence.</summary>
public static class RegexPresetEditing
{
    internal static Regex Compile(string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern.Length > 16000)
            throw new FormatException("A preset regex must contain 1–16,000 characters.");
        return new Regex(@"\A(?:" + pattern + @")\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    }
    public static string Replacement(string original, int start, int length, string pattern)
    {
        if (start < 0 || length <= 0 || start > original.Length - length || length > 250000)
            throw new FormatException("Select a nonempty text range in the regex editor (up to 250,000 characters).");
        if (!Compile(pattern).IsMatch(original.Substring(start, length)))
            throw new FormatException("The preset does not match the entire selected text. Nothing was replaced.");
        string replacement = "(?:" + pattern + ")";
        // Validate in context too: a standalone preset may be invalid inside the selected expression.
        _ = new Regex(original[..start] + replacement + original[(start + length)..],
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        return replacement;
    }
}
