namespace ParserCore;

/// <summary>FlexConn_DevGuide.pdf, appendices C/D (196–197) and domain field sets (204).</summary>
public static class TokenFormats
{
    public static IReadOnlyList<string> Types { get; } = ["String", "Integer", "Long", "Double", "IPAddress", "IPv6Address", "MacAddress", "Date", "Time", "TimeStamp", "RegexToken"];
    public static IReadOnlyList<string> Suggestions(string type) => type switch
    {
        "Date" => ["MM/dd/yyyy", "yyyy-MM-dd", "dd.MM.yyyy"],
        "Time" => ["HH:mm:ss", "HH:mm:ss.SSS", "hh:mm:ss a"],
        "TimeStamp" => ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.SSS", "yyyy-MM-dd'T'HH:mm:ssZ", "MMM dd HH:mm:ss", "MM/dd/yyyy HH:mm:ss", "yyyy.MM.dd G 'at' HH:mm:ss z", "EEE, MMM d, ''yy"],
        "RegexToken" => ["s/From: (.*)/$1/", "s/(.*)/$1/"],
        _ => []
    };
    public static bool IsDateTime(string type) => type is "Date" or "Time" or "TimeStamp";
    public static string Help(string type) => type switch
    {
        "Date" or "Time" or "TimeStamp" => "ArcSight (Java) date/time format: MM = month, mm = minutes, SSS = milliseconds. Enclose literal text in single quotes; double a single quote to escape it. Templates are editable; supply a missing syslog year through mapping.",
        "RegexToken" => "Substitution expression s/{exp}/{subst}/. Guide example: s/From: (.*)/$1/ extracts the name from From: rajiv. Edit the expression and replacement directly in the Format field.",
        "IPAddress" => "No format. IPv4; IPv6 only when the parser uses ipv6.aware=true.",
        "IPv6Address" => "No format. The guide specifies 16 bytes represented as 32 hexadecimal characters.",
        "Double" => "Decimal number; documented for ESM domain field sets (p. 204). The guide does not specify token.format.",
        _ => "The guide does not specify a format for this type. Leave it empty unless your custom configuration requires one."
    };
    public static IReadOnlyList<(string Symbol, string Meaning)> Symbols
    {
        get;
    } =
    [
        ("yyyy", "Year"), ("MM", "Numeric month"), ("MMM", "Month name"), ("dd", "Day of month"),
        ("HH", "Hour 0–23"), ("mm", "Minute"), ("ss", "Second"), ("SSS", "Milliseconds"),
        ("Z", "RFC 822 time zone, e.g. +0200"), ("z", "Time zone name"), ("EEE", "Day of week"),
        ("a", "AM/PM"), ("hh", "Hour 1–12"), ("kk", "Hour 1–24"), ("KK", "Hour 0–11"),
        ("G", "Era"), ("w", "Week of year"), ("W", "Week of month"), ("D", "Day of year"), ("F", "Day of week in month"),
        ("'text'", "Literal text"), ("''", "Single quote"), ("-", "Hyphen"), ("/", "Slash"), (":", "Colon"), (".", "Period"), (" ", "Space")
    ];
}
