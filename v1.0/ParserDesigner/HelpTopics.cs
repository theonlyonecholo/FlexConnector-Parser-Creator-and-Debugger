namespace ParserDesigner;

internal sealed record HelpTopic(string Id, string Title, string Text);

internal static class HelpTopics
{
    public static readonly HelpTopic[] All =
    [
        new("start", "Začínáme: od logu k události", """
Parser je předpis, jak z původního textu logu získat strukturovanou událost ArcSight. Regex rozpozná text, tokeny pojmenují zachycené hodnoty a mapování je přiřadí do polí event.*.

Postup zpracování
1. Konektor získá vstupní záznam. Způsob čtení a případné spojení více řádků závisí na typu konektoru.
2. Hlavní regex rozdělí zprávu na zachytávací skupiny.
3. token[n].name/type/format určí název, datový typ a případný formát hodnoty.
4. Volitelné submessages rozliší varianty těla zprávy podle message ID a dalších regexů.
5. Mapování event.* přiřadí hodnoty cílovým polím; operace mohou hodnoty převést nebo doplnit.
6. Severity mapping sjednotí závažnost. Extra processors mohou navázat další parsery či mapování.
Tento přehled vysvětluje role částí konfigurace; není popisem všech interních fází konektoru.

První parser v programu
Otevřete .properties nebo upravte výchozí příklad. Do Message vložte log, do Regex dekódovaný výraz. Spusťte F5, prohlédněte skupiny a vytvořte Tokeny z regexu. Nastavte typy a mapování v konfiguraci. Vyzkoušejte více logů přes Test souboru logů a nakonec Uložit.

Příklad
Message: status=404 message=Resource missing
Regex: status=(\d+) message=(.*)
Skupina 1 obsahuje 404, skupina 2 text Resource missing.
Token Status můžete mapovat do event.deviceSeverity a token Message do event.message.

Úspěšný lokální test potvrzuje shodu regexů, nikoli úplnou správnost události v ArcSight. Před nasazením ověřte parser v cílovém konektoru.
Podklad: FlexConn_DevGuide.pdf, str. 69–76.
"""),
        new("regex", "Regex, Message a debugger", """
Message je původní text, který testujete. Regex je vzor pro jeho rozpoznání. Dlouhý výraz se pouze vizuálně zalamuje; do obsahu se nevkládá nový řádek.

Základní zápis
\d = číslice, \s = bílý znak, \S = neprázdný znak bez whitespace, . = libovolný znak s výjimkou konce řádku v běžném režimu.
+ znamená alespoň jednou, * nula nebo vícekrát, ? volitelné, {2} přesně dvakrát. [A-Z] vybírá znak z rozsahu, A|B alternativu. ^ a $ označují hranice řádku/vstupu podle režimu.
(...) zachytí hodnotu a přidá skupinu. (?:...) seskupí výraz bez tokenu. (.*) je velmi obecné; pro konkrétní pole raději použijte omezenější vzor.

Klikněte do zachytávací skupiny: její záchyty se v Message zvýrazní modře. U vnoření se vybere nejvnitřnější zachytávací skupina. Skupina bez záchytu není totéž jako prázdný záchyt. U prázdného záchytu se zobrazí pozice, ale není co obarvit. Podmíněné a balancing skupiny mapování kliknutí nepodporuje.

Automaticky: přepočet po pauze při psaní. Při vypnutí použijte F5.
IgnoreCase (i): ignoruje velikost písmen.
Multiline (m): mění význam ^ a $ pro jednotlivé řádky; nespojuje logy.
Singleline (s): tečka odpovídá i znaku nového řádku.
Celá zpráva: regex musí pokrýt celý vstup. Bez této volby může náhled najít několik dílčích shod. Test souboru vyžaduje celou zprávu vždy.
Trim mezery/tab: pro náhled odstraní mezery a tabulátory z obou konců; pozice záchytů ukazuje v původním textu.
Timeout ms: omezuje jednotlivé regex operace, aby složitý výraz nezablokoval náhled.
Zastavit: zruší požadavek; běžící regex doběhne nejpozději po timeoutu.

Debug tabulka
Shoda = pořadí nalezené shody; Skupina 0 = celý odpovídající úsek; Skupina 1 = token[0]. Záchyt = pořadí opakovaného zachycení stejné skupiny. Úspěch = zda skupina něco zachytila. Pozice = index UTF-16 od nuly; Délka = počet jednotek UTF-16. Nezachycená skupina má pozici -1. Hodnota = zachycený text. Kliknutí na řádek zvýrazní tento záchyt; šipky procházejí záchyty, nikoli kroky backtrackingu.

Přepínače i/m/s, Celá zpráva a Trim jsou nastavení náhledu. Pro skutečný parser zapište příznaky a trim.message do konfigurace.
"""),
        new("properties", "Konfigurace .properties a ukládání", """
Konfigurace je textový soubor vlastností klíč=hodnota. Klíče rozlišují velikost písmen. Komentáře začínají # nebo !. U duplicitního klíče platí poslední definice. Zpětné lomítko na konci může pokračovat na dalším řádku.

Dvě podoby regexu
Do pole Regex pište: status=(\d+) message=(.*)
V .properties musí být: regex=status=(\\d+) message=(.*)
Properties mají vlastní escapování, proto je zpětné lomítko zdvojené. Regex → Konfigurace escapuje automaticky; Konfigurace → Regex hodnotu dekóduje.

Minimální příklad konfigurace
regex=status=(\\d+) message=(.*)
token.count=2
token[0].name=Status
token[0].type=String
token[1].name=Message
token[1].type=String
event.deviceSeverity=Status
event.message=Message
severity.map.high.if.deviceSeverity=400..599

Otevřít .properties: načte soubor a hlavní regex. Uložit zapíše aktuální konfiguraci; Uložit jako zvolí jiný soubor. Pokud se pole Regex liší, program nabídne jeho zápis. Změna v dialogu tlačítkem Použít ještě neznamená uložení na disk.
Konfigurace → Regex nahradí pracovní výraz hodnotou z konfigurace. Regex → Konfigurace aktualizuje vlastnost regex, nikoli tokeny. Při změně skupin zkontrolujte token.count, názvy a navázané odkazy.

Čtení properties používá Latin-1 nebo BOM; export ASCII s Unicode escapes zachová i české znaky. Nezaměňujte to s volbou kódování vstupního souboru logů.

Ctrl+Z: až 100 textových kroků zpět v aktivním poli. Ctrl+Y / Ctrl+Shift+Z: znovu. Nová změna zahodí větev pro obnovení. Historie se neukládá na disk. Otevření jiného parseru ji resetuje. Použití generátoru lze vrátit v hlavním poli konfigurace. Historie se nevztahuje na strukturální operace stromu a tabulky.
Podklad: příručka, str. 69–71; podrobnosti historie a ukládání popisují tuto aplikaci.
"""),
        new("tokens", "Tokeny a datové typy", """
Token je pojmenovaná hodnota získaná ze zachytávací skupiny. První skupina odpovídá token[0], druhá token[1]; token.count určuje jejich počet. Název slouží jako odkaz v mapování. Typ má odpovídat cílovému event poli.

Tokeny z regexu
Generátor vytvoří řádek pro každou zachytávací skupinu. Skupina 0 a (?:...) token nevytvářejí. Existující metadata přebírá podle indexu; při změně pořadí skupin zkontrolujte význam názvů. Názvy musejí být neprázdné a jedinečné. Použití také zapíše hlavní regex a odstraní přebytečné tokeny. Odkazy v mapování a submessages nepřejmenovává.

Typy
String: text, například uživatel nebo zpráva.
Integer: celé číslo od -2147483648 do 2147483647, například port.
Long: celé číslo od -9223372036854775808 do 9223372036854775807.
Double: desetinné číslo; příručka ho uvádí pro ESM domain field sets (str. 204).
IPAddress: IPv4; IPv6 při ipv6.aware=true. Nepřepínejte parser na IPv6 pouze změnou typu bez kontroly konfigurace.
IPv6Address: příručka uvádí 16 bajtů jako 32 hexadecimálních znaků.
MacAddress: MAC adresa, například 00:06:3E:22:51:B9 nebo s pomlčkami.
Date: datum; výchozí návrh MM/dd/yyyy.
Time: denní čas; výchozí návrh HH:mm:ss.
TimeStamp: datum a/nebo čas; například yyyy-MM-dd HH:mm:ss.
RegexToken: další extrakce substitucí, například s/From: (.*)/$1/.

Sloupec Typ nabízí seznam. U Date, Time, TimeStamp a RegexToken se při prázdném formátu doplní návrh. Existující formát se zachová: po změně typu jej zkontrolujte. Neznámý typ z původního souboru zůstane v nabídce.
Návrh formátu otevře pomocníka pro vybraný řádek. Formát můžete také napsat přímo do tabulky. Prázdný formát při exportu odstraní vlastnost token[n].format.
Lokální regex debugger ukazuje surové záchyty; neprovádí tyto typové konverze.
Podklad: příručka, str. 71, 196–197 a 204.
"""),
        new("formats", "Formát data, času a RegexToken", """
Formát říká konektoru, jak přečíst hodnotu tokenu. Není to regex. Používá symboly ArcSight/Java, ne formátování .NET.

Návrhář
Vyberte šablonu a upravte pole Formát. Kliknutí na tlačítko symbolu jej vloží na kurzor nebo nahradí výběr. Použít formát vrátí hodnotu do řádku tokenu. Bez formátu pole vymaže; Zrušit změny zahodí. Ctrl+Z vrací textové úpravy.

Nejčastější symboly
yyyy rok, MM měsíc číslem, MMM zkrácený název měsíce, dd den v měsíci.
HH hodina 0–23, hh hodina 1–12 s a pro AM/PM, kk hodina 1–24, KK hodina 0–11.
mm minuty, ss sekundy, SSS milisekundy. Pozor na MM versus mm!
EEE den týdne, G éra, w týden v roce, W týden v měsíci, D den v roce, F pořadí dne týdne v měsíci.
z název zóny, Z číselná zóna RFC 822 (např. +0200).
Doslovný text vložte do apostrofů: 'T' nebo 'at'. Dva apostrofy znamenají jeden doslovný apostrof.

Příklady hodnot → formát
2026-10-14 09:30:05 → yyyy-MM-dd HH:mm:ss
2026-10-14T09:30:05+0200 → yyyy-MM-dd'T'HH:mm:ssZ
09:30:05.123 → HH:mm:ss.SSS
Oct 14 09:30:05 → MMM dd HH:mm:ss
Syslog bez roku vyžaduje doplnění roku v mapování, například __useCurrentYear. Jazyk názvů měsíců a časovou zónu ověřte v konektoru.

RegexToken
s/From: (.*)/$1/ vybere z hodnoty From: rajiv jméno rajiv. Část před druhým lomítkem je regex; $1 v náhradě odkazuje na jeho první skupinu. To je další zpracování hodnoty tokenu, nikoli hlavní regex celého logu.

Návrhář vytváří text konfigurace, nevaliduje převod konkrétní hodnoty. Šablona je výchozí návrh: přizpůsobte ji skutečnému logu. Do UI zapisujte dekódovaný formát; escapování properties udělá export.
Podklad: příručka, str. 138 a 196–197.
"""),
        new("mapping", "Event mapping a operace", """
Normalizace převádí různé logy na jednotná pole ArcSight. Token je vstupní proměnná; event.* je cílové pole události.

Přímé mapování
event.sourceAddress=ClientIp
event.sourcePort=ClientPort
event.message=Message
event.deviceReceiptTime=Timestamp
Typ tokenu musí odpovídat typu cílového pole. IPAddress není totéž co String, i když obě hodnoty vypadají jako text IP adresy.

Výrazy a konstanty
event.deviceProduct=__stringConstant("MyProduct")
event.deviceReceiptTime=__useCurrentYear(Timestamp)
Operace začínající __ mohou převádět čísla, skládat text, doplňovat čas nebo vybírat hodnoty. Parametry jsou odkazy na tokeny či literály podle konkrétní operace. Přesné signatury hledejte v příloze A příručky.

Příklady významu polí
source/destination označují zdroj a cíl popisované události; device označuje zařízení, které ji hlásí. deviceSeverity je původní závažnost či zvolený stavový kód zařízení. message je textová zpráva. Není správné mapovat všechny adresy do sourceAddress bez znalosti významu logu.
requestUrl obsahuje celé URL. Nekombinujte jeho nastavení s nastavováním odvozených URI komponent stejné události; příručka upozorňuje na konflikt.

Editor Event mapping…
Vyberte cílové pole; zobrazí se očekávaný typ. Režim Token nabídne deklarované tokeny, Konstanta vytvoří __stringConstant a Výraz uloží zadanou operaci. Přidat / aktualizovat vloží návrh do tabulky nebo aktualizuje existující stejné pole. Nahoře rozpracovaný návrh je nutné nejprve přidat nebo vyprázdnit.
Stávající řádky lze upravit dvojklikem v tabulce. Odstranit mapování smaže vybraný řádek. Použít v konfiguraci zapíše změny do hlavního textu; pak Uložit. Zrušit návrh zahodí. Přímá mapování event.* jsou oddělena od submessage mappings a od ostatních vlastností, které editor zachovává.
Editor odmítne duplicitní pole a prázdné výrazy. Diagnostika upozorní na neznámý přímý token, nesoulad jeho typu s katalogem polí, chybějící event.name nebo konflikt requestUrl s URI komponentami. Upozornění nebrání uložení, protože hodnoty může doplnit jiná část parseru. Vlastní event pole lze zadat ručně.
Tato aplikace obecné ArcSight operace a event mapping nevykonává. Kontrola typů se týká přímých tokenů, ne návratových typů výrazů. Zelená shoda regexu tedy neověřuje výslednou událost.
Podklad: příručka, str. 71–73, příloha A a příloha E od str. 198.
"""),
        new("submessages", "Submessages: více variant logu", """
Submessage je část původního logu, kterou po hlavním regexu rozebere další pattern. Hodí se pro společnou hlavičku a různé formáty těla zprávy.

Příklad
Log: id=LOGIN body=user=alice result=success
Hlavní regex v editoru: id=(\S+) body=(.*)
token[0].name=MessageID, token[1].name=Body (oba String).
Token podzprávy = Body; Token message ID = MessageID.
Submessage s Message ID LOGIN má regex user=(\S+) result=(\S+).
fields = event.sourceUserName,event.deviceAction
První skupina podzprávy odpovídá $1 (alice), druhá $2 (success). Jejich číslování začíná znovu od jedničky.

Pole editoru
Token podzprávy: název tokenu obsahujícího tělo, vlastnost submessage.token.
Token message ID: token určující výběr definice, submessage.messageid.token.
Trim submessage: odstraní okrajové mezery/tabulátory před testem těla.
Message ID: přesná hodnota, pro kterou je definice určena.
Default pro neznámé ID: definice bez vlastnosti messageid. Použije se pro ID, které není definováno; nikoli při neshodě všech patternů známého ID.
regex: dekódovaný vzor pro celé tělo podzprávy.
fields: cílová event pole v pořadí zachytávacích skupin, oddělená čárkami.
types: explicitní typy v témže pořadí; konektor jinak může typ odvodit z cílového pole.
formats: formáty ve stejném pořadí; null označuje položku bez formátu.
mappings: výrazy pro jednotlivá fields, například __getProtocolName($1)|$2. Výchozí oddělovač je |, protože operace mohou obsahovat čárky.
mappings.delimiter: vlastní oddělovač mappings.
extramappings: další přiřazení, například event.name=__stringConstant("Unparsed event").
extramappings.delimiter: oddělovač dalších přiřazení (výchozí |).

Strom a pořadí
+ Submessage přidá novou definici. + Pattern přidá variantu do vybrané definice. Odstranit smaže vybraný pattern nebo celou definici. Dříve/Později změní pořadí. Vyhraje první odpovídající pattern; přesné varianty dejte před obecné (.*). Indexy a počty se při použití přepočítají.

Testovat log s těmito změnami vyhodnotí návrh nad celým vybraným logem. Kliknutí do skupiny zvýrazní pole, pokud byl použit právě tento pattern. U opakované skupiny jde o poslední hodnotu pole. Použít v konfiguraci vrátí změny do editoru; pak Uložit. Zrušit návrh zahodí. Přepnutí uzlu resetuje textovou historii polí.
Lokální test provádí routing a regexy; fields jsou popisky. types, formats, mappings a extramappings se při náhledu neprovádějí.
Podklad: příručka, str. 136–146.
"""),
        new("batch", "Test souboru logů a ladění", """
Test souboru logů používá jeden parser pro všechny řádky souboru. Jeden fyzický řádek = jedna událost, včetně prázdných řádků. Multiline konfigurace zde řádky nespojuje. Limit: 32 MB a 50 000 řádků.

Postup
1. Zvolte kódování a Otevřít logy. UTF-8 rozpoznává BOM; Latin-1 použijte jen pro odpovídající soubor.
2. Test běží na pozadí. Otestovat vše jej zopakuje, Zastavit ponechá zbývající řádky jako neotestované.
3. Vyberte log nebo použijte šipky. Filtr omezí seznam na shody, problémy nebo neotestované.
4. Detail ukáže původní text, barevné tokeny/pole, message ID a použité submessage[n].pattern[m]. Seznam pokusů ukáže, které patterny neuspěly.
5. Upravit submessage otevře použitý nebo první zkoušený pattern. Přidejte variantu pro odlišný log. Upravit parser otevře celou konfiguraci. Po použití se soubor automaticky přetestuje.
6. Změny se při zavření vrátí do hlavní konfigurace. Na disk je zapíšete přes Uložit.

Výsledky
Shoda: hlavní regex a případný submessage odpovídají.
Hlavní regex: neshoda: nejprve opravte společnou hlavičku, oddělovače nebo celý vzor.
Chybí submessage: není definice pro message ID ani default.
Submessage: neshoda: ID je známé, ale žádná varianta těla neodpovídá. Default to neřeší.
Chyba / timeout: překročený limit nebo problém se zpracováním; zkontrolujte detail.
Neotestováno: záznam ještě nebyl vyhodnocen.

Při opravě zkontrolujte i dříve úspěšné logy. Obecnější pattern může změnit výběr varianty. Vybraný řádek se zachová, pokud zůstává v aktuálním filtru.
Test používá konfiguraci včetně trim.message a trim.submessage, nikoli experimentální přepínače hlavního debuggeru.
"""),
        new("severity", "Severity: sjednocení závažnosti", """
Zařízení mohou hlásit různou stupnici závažnosti nebo jen stavový kód. event.deviceSeverity nese tuto původní hodnotu; severity.map.* ji převádí na low, medium, high nebo veryhigh.

V dialogu nejprve zadejte event.deviceSeverity, například Status (název zachyceného tokenu). Pak vyplňte pravidla:
high: 400..599
medium: 300..399
low: 100..299
Seznam může být textový nebo číselný, například OPEN,CLOSE nebo 404,500. Rozsahy zahrnují obě hranice.

Výsledná vlastnost:
severity.map.high.if.deviceSeverity=400..599
Status 404 tak spadá do high. Prázdné pravidlo se odstraní. Volte pravidla podle významu událostí, nikoli jen podle velikosti čísla.

Náhled umí přímý odkaz event.deviceSeverity na token první shody. Obecný výraz ArcSight lze uložit, ale náhled jej nevyhodnotí. Text porovnává přesně; překryv pravidel označí jako nejednoznačný bez odhadu priority. Zkontrolujte pokrytí očekávaných hodnot.
Podklad: příručka, str. 73–75.
"""),
        new("processors", "Extra processors: navazující parsery", """
Extra processor předá hodnotu další konfiguraci. Používá se například pro JSON uvnitř event.message nebo pro doplnění hodnot z map souboru. Není to submessage: odkazuje na samostatný soubor a může používat jiný typ parseru.

V editoru zvolte typ a Přidat šablonu. Doplňte cestu a parametry. Indexy musí být 0,1,2,… bez mezer; počet se při použití dopočítá. Po odebrání bloku přečíslujte následující indexy.

Typy
regex: další regulární výraz; delimited: pole oddělená oddělovačem; json: JSON; xml: XML; keyvalue: dvojice klíč/hodnota; standardkeyvalue: klíč=hodnota, dvojice oddělené čárkou; map: mapovací soubor; ntsubparser: podporované Windows application parsery.

Parametry
type: typ processoru. filename: odkaz na jeho konfiguraci, nikoli obsah parseru.
field: vstupní event pole, například event.message.
flexagent: true vybírá parsery ve flexagent, false v fcp.
clearfieldafterparsing: zda po parsování vymazat vstupní pole.
overrideeventmappings: zda přepsat existující mapování event polí.
charencoding: kódování znaků.
conditionfield: pole použité pro podmínku; conditiontype: způsob porovnání; conditionvalues: hodnoty oddělené čárkou. Platné druhy podmínek ověřte pro konkrétní processor.
casesensitive / Casesensitive: rozlišování velikosti písmen; properties rozlišují velikost názvů, držte se klíče pro příslušný processor. Tabulka v příručce používá Casesensitive.
concatenatevalues: u key-value parserů spojit hodnoty duplicitního klíče místo přepsání.

Příklad
extraprocessor.count=1
extraprocessor[0].type=regex
extraprocessor[0].filename=vendor/detail
extraprocessor[0].field=event.message
extraprocessor[0].flexagent=true
extraprocessor[0].clearfieldafterparsing=false

Příručka umisťuje běžné konfigurace do user/agent/flexagent, map soubory do user/agent/fcp nebo user/agent/aup/fcp (aup má přednost).
Program kontroluje strukturu konfigurace, nikoli existenci souborů. Navázané parsery lokálně nespouští. Cesty ze šablon nahraďte skutečnými.
Podklad: příručka, str. 75–76.
"""),
        new("limits", "Co náhled provádí a co ověřit v konektoru", """
Program provádí: syntaktické čtení/zápis properties, regex shody a záchyty, jejich pozice a barvy, routing submessages a výběr prvního patternu, testování souboru logů a omezený náhled severity.

Program neprovádí: obecné ArcSight operace, úplné event mapping, typové převody tokenů, zpracování jejich formátů a substitucí RegexToken, conditional mapping, navázané extra processors ani skládání multiline událostí při testu souboru.

Regex engine je .NET; ArcSight používá Java. Pojmenované skupiny, jejich číslování a některé konstrukce či Unicode chování se mohou lišit. Lokální shoda není potvrzením kompatibility každé konstrukce.

Náhled má limity: 250 000 znaků zprávy, 16 000 znaků regexu, 1 000 shod a 5 000 řádků záchytů. Souhrnný čas je omezen, jednotlivá regex operace má timeout. Zkrácený výsledek není úplný seznam všech záchytů. Debug ukazuje výsledky, ne interní backtracking.

Při neúspěchu
1. Ověřte skutečný log, mezery, oddělovače a kódování.
2. Odlište dekódovaný regex od escapované hodnoty v properties.
3. Zkontrolujte token.count a pořadí skupin, názvy a odkazy.
4. U submessages ověřte tělo, message ID, pořadí variant a default.
5. U výsledné události ověřte typy, formáty, operace, časovou zónu, severity a navázané soubory v konektoru.

Zdroj popisu ArcSight: přiložený FlexConn_DevGuide.pdf, zejména str. 69–76, 131–146, 177–197 a 198–204. Chování tlačítek a limity popisují tuto C# aplikaci. Nápověda funguje offline a hledá i v obsahu témat.
""")
    ];
}
