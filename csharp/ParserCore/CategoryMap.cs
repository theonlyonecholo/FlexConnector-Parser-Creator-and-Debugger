using System.Text.Json;
using System.Text.RegularExpressions;

namespace ParserCore;

public sealed record CategoryValue(string Field, string Value, string Description);

/// <summary>Editable exact-ID CSV categorization maps. Advanced CSV headers are deliberately rejected.</summary>
public sealed class CategoryMap
{
    public static readonly string[] Fields = ["categoryObject", "categoryBehavior", "categoryTechnique",
        "categoryDeviceGroup", "categorySignificance", "categoryOutcome"];
    public List<string[]> Rows { get; } = [];
    public static CategoryMap Parse(string csv)
    {
        if (csv.Length > 4 * 1024 * 1024)
            throw new FormatException("Map exceeds 4 MB.");
        var rows = MapFileProcessor.Csv(csv, default);
        string[] header = ["event.deviceEventClassId", .. Fields.Select(f => "set.event." + f)];
        if (rows.Count == 0 || !rows[0].SequenceEqual(header))
            throw new FormatException("This editor supports deviceEventClassId followed by the six standard category setters. Other maps must be edited as CSV externally; they are never silently rewritten.");
        var map = new CategoryMap();
        foreach (var row in rows.Skip(1))
            map.Rows.Add(row);
        map.Validate();
        return map;
    }
    public void Validate()
    {
        if (Rows.Count > 9999)
            throw new FormatException("Maximum 9,999 category rows.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            if (row.Length != 7 || string.IsNullOrWhiteSpace(row[0]))
                throw new FormatException("Every row requires an event class ID and six category cells.");
            if (!ids.Add(row[0].Trim(' ', '\t')))
                throw new FormatException("Duplicate event class ID (case-insensitive): " + row[0]);
            foreach (string value in row.Skip(1))
                if (value.Length > 1023 || value.Length > 0 && (!value.StartsWith('/') || value.Any(char.IsControl)))
                    throw new FormatException("Category paths must start with /, contain no control characters and fit 1,023 characters.");
        }
    }
    public string ToCsv()
    {
        Validate();
        static string Cell(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        string[] header = ["event.deviceEventClassId", .. Fields.Select(f => "set.event." + f)];
        string csv = string.Join("\r\n", new[] { header }.Concat(Rows).Select(r => string.Join(",", r.Select(Cell)))) + "\r\n";
        if (System.Text.Encoding.UTF8.GetByteCount(csv) > 4 * 1024 * 1024)
            throw new FormatException("Map exceeds 4 MB.");
        return csv;
    }
    public static string Attach(string source, string relativeFile)
    {
        if (Path.IsPathRooted(relativeFile) || relativeFile.Replace('\\', '/').Split('/').Any(s => s is ".." or "" or "."))
            throw new FormatException("Choose a relative map path inside the parser directory.");
        var doc = new PropertiesDocument(source);
        int count = EventPipeline.Count(doc, "extraprocessor.count", 100);
        int index = Enumerable.Range(0, count).FirstOrDefault(i => doc.Get($"extraprocessor[{i}].type") == "map" &&
            doc.Get($"extraprocessor[{i}].filename").Replace('\\', '/') == relativeFile.Replace('\\', '/'), -1);
        if (index < 0)
        {
            if (count == 100)
                throw new FormatException("Maximum 100 extra processors in this preview.");
            index = count;
            doc.Set("extraprocessor.count", (count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        string prefix = $"extraprocessor[{index}].";
        doc.Set(prefix + "type", "map");
        doc.Set(prefix + "filename", relativeFile.Replace('\\', '/'));
        doc.Set(prefix + "charencoding", "UTF-8");
        doc.Set(prefix + "casesensitive", "false");
        doc.Set(prefix + "trimgettertokens", "true");
        doc.Set(prefix + "trimsettertokens", "true");
        doc.Set(prefix + "allowoverwrite", "true");
        return doc.Source;
    }
    public static string NativeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new FormatException("Device vendor and product must be set.");
        return Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "_");
    }
    /// <summary>A local categorizer/ directory opts into the documented native categorizer preview.</summary>
    internal static void ApplyNative(string root, Dictionary<string, string?> fields, List<EventWrite> trace,
        CancellationToken cancellation)
    {
        if (fields.Any(p => p.Key.StartsWith("event.category", StringComparison.Ordinal) && !string.IsNullOrEmpty(p.Value)))
        {
            trace.Add(new("Categorizer", "", null, null, "Skipped: event already has category fields"));
            return;
        }
        string file = NativeName(fields.GetValueOrDefault("event.deviceVendor") ?? "") + "/" +
            NativeName(fields.GetValueOrDefault("event.deviceProduct") ?? "") + ".csv";
        var doc = new PropertiesDocument(Attach("", file));
        var dependency = MapFileProcessor.Inspect(doc, root).Single();
        if (dependency.Status != "Ready")
            throw new IOException("Categorizer: " + file + ": " + dependency.Status);
        MapFileProcessor.Apply(doc, dependency with
        {
            Stage = "extraprocessor[0]"
        }, fields, trace, cancellation);
        // Rename only the entries produced by this stage; callers use a separate trace buffer.
    }
}

/// <summary>A user-maintained starter vocabulary, not a complete or authoritative ArcSight taxonomy.</summary>
public sealed class CategoryCatalog(string path)
{
    public static IReadOnlyList<CategoryValue> Defaults
    {
        get;
    } =
    [
        new("categoryObject", "/Host/Application/Service", "Application service"),
        new("categoryObject", "/Host/Resource", "Host resource"),
        new("categoryBehavior", "/Communicate/Query", "Query a service"),
        new("categoryBehavior", "/Create", "Create an object"),
        new("categoryBehavior", "/Execute", "Execute"),
        new("categoryTechnique", "/Redirection/Application", "Application redirection"),
        new("categoryDeviceGroup", "/Application", "Application event family"),
        new("categoryDeviceGroup", "/Firewall", "Firewall event family"),
        new("categorySignificance", "/Normal", "Normal activity; independent of severity"),
        new("categorySignificance", "/Informational", "Informational activity"),
        new("categorySignificance", "/Informational/Warning", "Warning"),
        new("categorySignificance", "/Informational/Error", "Error"),
        new("categoryOutcome", "/Success", "Action succeeded"),
        new("categoryOutcome", "/Failure", "Action failed"),
        new("categoryOutcome", "/Attempt", "Attempt; outcome not known")
    ];
    public List<CategoryValue> Load()
    {
        if (!File.Exists(path))
            return Defaults.ToList();
        if (new FileInfo(path).Length > 4 * 1024 * 1024)
            throw new FormatException("Category dictionary exceeds 4 MB.");
        var values = JsonSerializer.Deserialize<List<CategoryValue>>(File.ReadAllText(path)) ?? throw new FormatException("Invalid dictionary.");
        Validate(values);
        return values;
    }
    public static void Validate(IReadOnlyList<CategoryValue> values)
    {
        if (values.Count > 10000)
            throw new FormatException("Maximum 10,000 dictionary values.");
        var keys = new HashSet<(string, string)>();
        foreach (var v in values)
            if (v is null || !CategoryMap.Fields.Contains(v.Field) || string.IsNullOrEmpty(v.Value) ||
                !v.Value.StartsWith('/') || v.Value.Length > 1023 || v.Value.Any(char.IsControl) ||
                v.Description is null || v.Description.Length > 2000 || !keys.Add((v.Field, v.Value)))
                throw new FormatException("Invalid or duplicate category dictionary entry.");
    }
    public void Save(IReadOnlyList<CategoryValue> values)
    {
        Validate(values);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
