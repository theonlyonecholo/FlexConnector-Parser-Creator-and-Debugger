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
        "Date" or "Time" or "TimeStamp" => "Formát data/času ArcSight (Java): MM = měsíc, mm = minuty, SSS = milisekundy. Text uzavřete do apostrofů, apostrof zapište dvakrát. Šablony lze upravit; rok chybějící v syslog formátu doplňte v mapování.",
        "RegexToken" => "Substituční výraz s/{exp}/{subst}/. Příklad z příručky: s/From: (.*)/$1/ získá jméno z textu From: rajiv. Výraz i náhradu upravte přímo v poli Formát.",
        "IPAddress" => "Bez formátu. IPv4; IPv6 pouze pro parser s ipv6.aware=true.",
        "IPv6Address" => "Bez formátu. Příručka uvádí 16 bajtů jako 32 hexadecimálních znaků.",
        "Double" => "Desetinné číslo; dokumentováno pro ESM domain field sets (str. 204). Příručka neuvádí token.format.",
        _ => "Příručka pro tento typ neuvádí formát. Pole ponechte prázdné, pokud nemáte vlastní konfiguraci vyžadující formát."
    };
    public static IReadOnlyList<(string Symbol, string Meaning)> Symbols
    {
        get;
    } =
    [
        ("yyyy", "Rok"), ("MM", "Měsíc číslem"), ("MMM", "Název měsíce"), ("dd", "Den v měsíci"),
        ("HH", "Hodina 0–23"), ("mm", "Minuta"), ("ss", "Sekunda"), ("SSS", "Milisekundy"),
        ("Z", "Zóna RFC 822, např. +0200"), ("z", "Název časové zóny"), ("EEE", "Den v týdnu"),
        ("a", "AM/PM"), ("hh", "Hodina 1–12"), ("kk", "Hodina 1–24"), ("KK", "Hodina 0–11"),
        ("G", "Éra"), ("w", "Týden v roce"), ("W", "Týden v měsíci"), ("D", "Den v roce"), ("F", "Pořadí dne týdne v měsíci"),
        ("'text'", "Doslovný text"), ("''", "Apostrof"), ("-", "Pomlčka"), ("/", "Lomítko"), (":", "Dvojtečka"), (".", "Tečka"), (" ", "Mezera")
    ];
}
