using System.Text.RegularExpressions;

namespace ParserCore;

public sealed record EventAssignment(string Field, string Expression);

/// <summary>Edits direct event assignments and diagnoses simple references without executing ArcSight expressions.</summary>
public static class EventMapping
{
    // Appendix E, FlexConn_DevGuide.pdf, pp. 198–203. Custom fields remain editable.
    public static IReadOnlyDictionary<string, string> Fields { get; } = BuildFields();
    private static Dictionary<string, string> BuildFields()
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string type, string names)
        {
            foreach (string name in names.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                fields["event." + name] = type;
        }
        Add("String", "applicationProtocol categoryBehavior categoryDeviceGroup categoryObject categoryOutcome categorySignificance categoryTechnique cryptoSignature customerURI deviceAction deviceDnsDomain deviceDomain deviceEventCategory deviceEventClassId deviceExternalId deviceFacility deviceHostName deviceInboundInterface deviceNtDomain deviceOutboundInterface devicePayloadId deviceProcessName deviceProduct deviceSeverity deviceTimeZone deviceTranslatedZoneURI deviceVendor deviceVersion deviceZoneURI externalId fileHash fileId fileName filePath filePermission fileType message name oldFileHash oldFileId oldFileName oldFilePath oldFilePermission rawEvent requestClientApplication requestContext requestCookies requestMethod requestUrl transportProtocol");
        Add("Integer", "baseEventCount");
        Add("Long", "bytesIn bytesOut fileSize flexNumber1 flexNumber2");
        Add("IPAddress", "deviceAddress deviceTranslatedAddress");
        Add("MacAddress", "deviceMacAddress");
        Add("TimeStamp", "deviceReceiptTime startTime endTime fileCreateTime fileModificationTime oldFileCreateTime oldFileModificationTime flexDate1");
        Add("String", "flexDate1Label flexNumber1Label flexNumber2Label flexString1 flexString1Label flexString2 flexString2Label");
        foreach (string prefix in new[] { "source", "destination" })
        {
            foreach (string suffix in new[] { "DnsDomain", "HostName", "NtDomain", "ProcessName", "ServiceName", "TranslatedZoneURI", "UserId", "UserName", "UserPrivileges", "ZoneURI" })
                Add("String", prefix + suffix);
            Add("IPAddress", prefix + "Address " + prefix + "TranslatedAddress");
            Add("Integer", prefix + "Port " + prefix + "TranslatedPort");
            Add("MacAddress", prefix + "MacAddress");
        }
        foreach (var (family, type, count) in new[] { ("String", "String", 6), ("Number", "Long", 3), ("Date", "TimeStamp", 2), ("IPv6Address", "IPAddress", 3) })
            for (int i = 1; i <= count; i++)
            {
                Add(type, "deviceCustom" + family + i);
                Add("String", "deviceCustom" + family + i + "Label");
            }
        return fields;
    }
    public static bool IsDirectField(string key) => Regex.IsMatch(key, @"\Aevent\.[A-Za-z][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static IReadOnlyList<EventAssignment> Load(string source) => new PropertiesDocument(source).Values
        .Where(kv => IsDirectField(kv.Key)).Select(kv => new EventAssignment(kv.Key, kv.Value)).ToArray();
    public static IReadOnlyDictionary<string, string> Tokens(string source)
    {
        var doc = new PropertiesDocument(source);
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in doc.Values.Where(kv => Regex.IsMatch(kv.Key, @"\Atoken\[\d+\]\.name\z")))
            if (item.Value.Length > 0)
                tokens[item.Value] = doc.Get(item.Key[..^4] + "type");
        return tokens;
    }
    public static string Constant(string text) => "__stringConstant(\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\")";
    /// <summary>Replaces direct event fields only; leaves conditional keys, submessages and unrelated blocks intact.</summary>
    public static string Apply(string source, IReadOnlyList<EventAssignment> assignments)
    {
        if (assignments.Any(a => !IsDirectField(a.Field)))
            throw new FormatException("Cílové pole musí mít tvar event.nazevPole bez mezer.");
        if (assignments.Any(a => string.IsNullOrWhiteSpace(a.Expression)))
            throw new FormatException("Každé mapování musí mít token nebo výraz. Nepotřebný řádek odstraňte.");
        if (assignments.Select(a => a.Field).Distinct(StringComparer.Ordinal).Count() != assignments.Count)
            throw new FormatException("Cílové pole je uvedeno vícekrát. Pro každé pole ponechte jedno mapování.");
        var doc = new PropertiesDocument(source);
        var retained = assignments.Select(a => a.Field).ToHashSet(StringComparer.Ordinal);
        doc.RemoveWhere(key => IsDirectField(key) && !retained.Contains(key));
        foreach (var assignment in assignments)
            doc.Set(assignment.Field, assignment.Expression);
        return doc.Source;
    }
    /// <summary>Returns advisory diagnostics, not a verdict on the complete event produced by a connector.</summary>
    public static IReadOnlyList<string> Warnings(string source, IReadOnlyList<EventAssignment> assignments)
    {
        var warnings = new List<string>();
        var tokens = Tokens(source);
        foreach (var a in assignments)
        {
            if (!Fields.TryGetValue(a.Field, out string? expected))
                warnings.Add(a.Field + ": vlastní nebo neznámé pole; ověřte jej v cílovém konektoru.");
            if (tokens.TryGetValue(a.Expression, out string? actual))
            {
                if (expected is not null && actual != expected)
                    warnings.Add($"{a.Field}: token {a.Expression} má typ {actual}, cílové pole očekává {expected}.");
            }
            else if (Regex.IsMatch(a.Expression, @"\A[A-Za-z][A-Za-z0-9_]*\z"))
                warnings.Add($"{a.Field}: odkaz {a.Expression} není mezi deklarovanými tokeny.");
        }
        if (!assignments.Any(a => a.Field == "event.name"))
            warnings.Add("event.name chybí; příručka jej označuje jako povinné. Může jej doplňovat submessage nebo navazující parser.");
        if (assignments.Any(a => a.Field == "event.requestUrl") && assignments.Any(a => a.Field is "event.requestProtocol" or "event.requestUrlAuthority" or "event.requestUrlHost" or "event.requestUrlPort" or "event.requestUrlFileName" or "event.requestUrlQuery"))
            warnings.Add("requestUrl je nastaven současně s URI komponentami; příručka upozorňuje na konflikt.");
        return warnings;
    }
}
