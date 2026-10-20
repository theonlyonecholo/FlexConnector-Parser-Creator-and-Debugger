using System.Text.Json;

namespace ParserCore;

public sealed record EventFieldDefinition(string Field, string Type, string Length, string Restrictions, string Source)
{
    public override string ToString() => $"{Field} · {Type} · {Length}";
}

/// <summary>Documented field metadata plus local overrides; this does not extend the connector's schema.</summary>
public sealed class EventFieldCatalog(string path)
{
    public const string DocumentationUrl = "https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-25.1/pdfdoc/flexconn_devguide/flexconn_devguide.pdf";
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public static IReadOnlyDictionary<string, EventFieldDefinition> BuiltIn { get; } = Build();
    private sealed record SavedCatalog(int Version, List<EventFieldDefinition> Fields);
    public List<EventFieldDefinition> LoadOverrides()
    {
        if (!File.Exists(Path))
            return [];
        if (new FileInfo(Path).Length > 4 * 1024 * 1024)
            throw new FormatException("The field catalog exceeds 4 MB.");
        var saved = JsonSerializer.Deserialize<SavedCatalog>(File.ReadAllText(Path));
        if (saved is null || saved.Version != 1 || saved.Fields is null)
            throw new FormatException("Unsupported field catalog format.");
        Validate(saved.Fields);
        return saved.Fields;
    }
    public IReadOnlyDictionary<string, EventFieldDefinition> Load()
    {
        var result = BuiltIn.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (var item in LoadOverrides())
            result[item.Field] = item;
        return result;
    }
    public void Save(IReadOnlyList<EventFieldDefinition> overrides)
    {
        Validate(overrides);
        string json = JsonSerializer.Serialize(new SavedCatalog(1, overrides.ToList()), new JsonSerializerOptions { WriteIndented = true });
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024)
            throw new FormatException("The field catalog exceeds 4 MB.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, Path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Validate(IReadOnlyList<EventFieldDefinition> fields)
    {
        if (fields.Count > 1000 || fields.Any(f => f is null || string.IsNullOrEmpty(f.Field) ||
            f.Field.Length > 200 || !EventMapping.IsDirectField(f.Field) || string.IsNullOrWhiteSpace(f.Type) ||
            f.Type.Length > 100 || string.IsNullOrWhiteSpace(f.Length) || f.Length.Length > 300 ||
            f.Restrictions is null || f.Restrictions.Length > 4000 || f.Source is null || f.Source.Length > 2000) ||
            fields.Select(f => f.Field).Distinct(StringComparer.Ordinal).Count() != fields.Count)
            throw new FormatException("Use unique event.fieldName keys, a type and length description; at most 1,000 local definitions.");
    }
    private static Dictionary<string, EventFieldDefinition> Build()
    {
        var result = new Dictionary<string, EventFieldDefinition>(StringComparer.Ordinal);
        var types = EventMapping.Fields.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        types["event.oldFileSize"] = "Long";
        types["event.oldFileType"] = "String";
        foreach (var (field, type) in types)
        {
            string name = field[6..];
            string length = type == "String" ? "1023" : "n/a";
            string restriction = type switch
            {
                "Integer" => name == "baseEventCount" ? "0 .. 2147483647" : "0 .. 65535",
                "Long" => name is "bytesIn" or "fileSize" or "oldFileSize" ? "0 .. 9223372036854775807"
                    : name == "bytesOut" ? "Nonnegative Long. The guide prints an ambiguous upper bound (2^63-1 -1); confirm the target version."
                    : "-9223372036854775808 .. 9223372036854775807",
                "IPAddress" => "IPv4 or IPv6",
                "MacAddress" => "Six hexadecimal octets, separated by colons or hyphens.",
                "TimeStamp" => "Milliseconds since 1970-01-01; convert the source timestamp using its token format.",
                _ => "No additional range specified."
            };
            if (name.EndsWith("URI", StringComparison.Ordinal))
            {
                length = "Not specified";
                restriction = "Database resource reference; configure via the network model/customer settings where possible.";
            }
            if (name.StartsWith("category", StringComparison.Ordinal))
                restriction = "Prefer a categorization file; use the target Console category definitions.";
            if (name.StartsWith("deviceCustomString", StringComparison.Ordinal) && !name.EndsWith("Label", StringComparison.Ordinal))
                length = "4000";
            if (name.StartsWith("deviceCustomIPv6Address", StringComparison.Ordinal))
                restriction = name.EndsWith("Label", StringComparison.Ordinal)
                    ? "Legacy labels: 1 = Device IPv6 Address; 2 = Source IPv6 Address; 3 = Destination IPv6 Address. IPv6-aware parsers may use appropriate labels."
                    : "Legacy parsers: IPv6, 1=device / 2=source / 3=destination. IPv6-aware parsers also allow IPv4; prefer standard address fields.";
            result[field] = new(field, type, length, restriction, "OpenText FlexConnector CE 24.3, pp. 184–189 (July 2024). Checked 2026-10-20. " + DocumentationUrl);
        }
        void Length(string value, string names)
        {
            foreach (string name in names.Split(' '))
                result["event." + name] = result["event." + name] with
                {
                    Length = value
                };
        }
        Length("31", "applicationProtocol deviceVersion transportProtocol");
        Length("63", "deviceAction deviceHostName deviceProduct deviceSeverity deviceVendor");
        Length("15", "deviceInboundInterface deviceOutboundInterface");
        Length("255", "destinationDnsDomain destinationNtDomain sourceDnsDomain sourceNtDomain deviceDnsDomain deviceExternalId deviceNtDomain deviceTimeZone fileHash oldFileHash");
        Length("128", "devicePayloadId flexDate1Label flexNumber1Label flexNumber2Label flexString1Label flexString2Label");
        Length("512", "cryptoSignature name");
        Length("40", "externalId");
        Length("4000", "rawEvent");
        Length("2048", "requestContext");
        result["event.name"] = result["event.name"] with
        {
            Restrictions = "Required event field; it may also be supplied by submessages or downstream processing."
        };
        result["event.transportProtocol"] = result["event.transportProtocol"] with
        {
            Restrictions = "Guide lists TCP, UDP, ICMP, IGMP, ARP."
        };
        result["event.rawEvent"] = result["event.rawEvent"] with
        {
            Restrictions = "PreserveRawEvent=Yes populates this automatically; with No the parser can assign it."
        };
        return result;
    }
}
