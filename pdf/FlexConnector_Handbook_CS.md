# Jak psát FlexConnector parsery

Sources checked: 2026-10-19

## Než začnete

Praktická příručka se zaměřuje na parsery Regex File a Regex Syslog FlexConnectorů. Jde o nový výukový text, nikoli překlad přiloženého historického manuálu. Příklady používají umělé události a dokumentační IP adresy.

### Aktuálnost a rozsah ověření

Zdroje byly ověřeny 19. října 2026. Veřejný rozcestník SmartConnector CE 25.1 odkazuje na příručku FlexConnectorů označenou CE 24.3 z července 2024. Existují i novější produktové portály; to samo nepotvrzuje novější vydání příručky FlexConnectorů. Starší HTML kapitoly jsou označeny jako 8.4 a jejich pravidla jsou porovnána s veřejným PDF CE 24.3. [S1, S2, S13]

Odkazy Oracle Java SE 21 vysvětlují properties, regex a formáty času. Neurčují, jaké JRE obsahuje váš konektor. Před nasazením ověřte verzi konektoru, typ parseru a locale. [S10-S12]

### Pracovní model

Fáze | Na co musíte znát odpověď
--- | ---
Hranice události | Které bajty patří do jedné události?
Hlavní regex | Jaký text vytvoří jednotlivé skupiny?
Tokeny | Jaká mají jména, typy a formáty?
Submessage | Které ID vybere který vzor těla zprávy?
Normalizace | Která event pole dostanou které hodnoty?
Ověření | Vytvoří skutečný konektor požadovanou událost?

Doporučené pořadí: syntaxe -> úplný příklad -> typy/mapování -> submessages -> další zpracování -> testy. Bloky označené jako fragment je nutné vložit do existujícího parseru; nejsou samostatnými parsery.

## 01 / Properties a regex

Rozlišujte původní log, dekódovaný regex a hodnotu v .properties. Regex tester může přijmout jedno zpětné lomítko; v properties je zpravidla nutné další escapování. Komentář začíná # nebo !. Neescapované lomítko na konci řádku pokračuje hodnotou na dalším fyzickém řádku. Nepřidávejte je za každý řádek. [S10]

Význam | Dekódovaný regex | V .properties
--- | --- | ---
Číslice | \d | \\d
Tabulátor | \t | \\t
Doslovná tečka | \. | \\.
Zachycení pole | ([^\t]*) | ([^\\t]*)

### Záměrně tabulátorový příklad

```properties
regex=([^\\t]*)\\t([^\\t]*)\\t([^\\t]*)
```

Tento vlastní příklad popisuje přesně tři pole. Hvězdička dovoluje prázdné pole; plus je odmítne. Zachovejte sousední tabulátory. Vizuální mezera nedokazuje tabulátor: U+2003 EM SPACE není U+0009 TAB. Načítejte původní soubor, nikoli formátovanou kopii z chatu.

### Zachycujte jen potřebné hodnoty

Použijte (...) pro token a (?:...) pro seskupení bez dalšího tokenu. Tečky v IP vzoru escapujte. Vyhněte se vnořenému neomezenému opakování, například (.*)+. Regex připomínající IP adresu ještě nekontroluje rozsah oktetů. [S11]

```properties
# Structural IPv4 shape, not octet-range validation
regex=((?:\\d{1,3}\\.){3}\\d{1,3})
```

Pracovní postup: nejprve sepište požadovaná pole a regex sestavujte zleva doprava. Testujte celou událost. Samotná shoda podřetězce v obecném testeru správnost parseru nepotvrzuje.

## 02 / První úplný parser

Uložte tento vlastní příklad jako training-basic.sdkrfilereader.properties. Očekává přesné pořadí klíčů uvedené níže. Časové pásmo zdroje nastavte zvlášť; čas v logu neobsahuje offset.

```properties
2026-10-19 10:02:15 level=WARN user=alice src=192.0.2.10 action=LOGIN
```

```properties
# Training example: one line = one event
regex=(\\S+ \\S+) level=(\\w+) user=(\\S+) src=(\\S+) action=(\\w+)
token.count=5
token[0].name=EventTime
token[0].type=TimeStamp
token[0].format=yyyy-MM-dd HH:mm:ss
token[1].name=Level
token[1].type=String
token[2].name=User
token[2].type=String
token[3].name=SourceIP
token[3].type=IPAddress
token[4].name=Action
token[4].type=String

event.deviceVendor=__stringConstant("ExampleVendor")
event.deviceProduct=__stringConstant("TrainingApp")
event.name=Action
event.endTime=EventTime
event.sourceUserName=User
event.sourceAddress=SourceIP
event.deviceAction=Action
event.deviceSeverity=Level

severity.map.veryhigh.if.deviceSeverity=CRITICAL
severity.map.high.if.deviceSeverity=ERROR
severity.map.medium.if.deviceSeverity=WARN
severity.map.low.if.deviceSeverity=INFO,DEBUG
```

Očekávaný výsledek: User=alice, SourceIP=192.0.2.10, Action=LOGIN, deviceSeverity=WARN. Ukázková politika mapuje WARN na medium. Syntaxe a skupiny jsou ověřeny lokálně; konverze ArcSight a chování cíle vyžadují test na konektoru. [S4-S6]

## 03 / Tokeny, typy a čas

Indexy tokenů začínají nulou. U běžných číslovaných skupin odpovídá token[0] skupině 1. token.count musí souhlasit s počtem zachytávacích skupin; nezachytávací skupina token nepřidává. Jméno tokenu slouží jako odkaz v mapování. [S4]

Typ / použití | Praktická kontrola
--- | ---
String / ID, jména, zprávy | Identifikátor 001 ponechte jako text, potřebujete-li nuly.
Integer / porty, menší počty | Ověřte rozsah a chování prázdné hodnoty.
Long / velké čítače | Zabraňte přetečení dlouhodobých čítačů.
IPAddress / síťová adresa | DNS jméno patří do hostname pole, ne sourceAddress.
TimeStamp / čas události | Doplňte formát a ověřte pásmo zdroje.

### Příklad časové značky

```properties
token[0].type=TimeStamp
token[0].format=EEE MMM dd HH:mm:ss yyyy
```

Pro Thu Sep 03 10:02:15 2026 vyžadují EEE a MMM locale rozpoznávající anglické názvy. MM znamená měsíc, mm minutu. HH je 24hodinový čas. yyyy je kalendářní rok; YYYY je týdenní rok a může překvapit kolem Nového roku. Pevný text ve formátu uzavřete do jednoduchých uvozovek. [S12]

### Prázdný token není automaticky chyba

V A<TAB><TAB>B je druhé pole prázdné. Zachycené prázdné pole a nepoužitá volitelná skupina jsou různé situace. Neposouvejte názvy dalších tokenů jen proto, abyste prázdnou hodnotu skryli. Porovnejte pole s pozicemi původních oddělovačů.

Seznam typů je pracovní výběr, nikoli úplný katalog. Pro nový typ ověřte tabulky tokenů a event polí ve veřejné příručce CE 24.3. [S2]

## 04 / Event mapping a severity

Zachycení textu ještě nevytváří normalizovanou událost. Mapování přiřazuje token nebo výsledek operace do pole schématu; typy musí odpovídat. Zařízení hlásící událost nemusí být jejím zdrojem ani cílem. [S5]

```properties
event.sourceUserName=User
event.sourceAddress=SourceIP
event.deviceHostName=ReportingHost
event.deviceCustomString1=Channel
event.deviceCustomString1Label=__stringConstant("Channel")
```

Pouze fragment: User, SourceIP, ReportingHost a Channel musí být deklarovány. __stringConstant vytváří konstantu; samotné jméno tokenu odkazuje na zachycenou hodnotu.

### Pole vybírejte podle významu události

Praktické cvičení: u zamítnutého spojení si napište, kdo spojení zahájil, kdo byl cílem a které zařízení jej zaznamenalo. Teprve potom mapujte role. První nalezenou adresu nepřiřazujte automaticky do sourceAddress. U Windows zachovejte provider/channel a event ID pro rozlišení rodin událostí.

### Závažnost normalizujte vědomě

Do event.deviceSeverity vložte původní úroveň a poté určete normalizovanou závažnost. Pravidla používají low, medium, high a veryhigh. Hodnoty mohou být seznamy nebo číselné rozsahy. Vyšší číslo výrobce nemusí znamenat vyšší závažnost. [S6]

```properties
event.deviceSeverity=Level
severity.map.high.if.deviceSeverity=ERROR
severity.map.medium.if.deviceSeverity=WARN
severity.map.low.if.deviceSeverity=INFO,DEBUG
```

Jde o ukázkovou politiku, nikoli univerzální Windows škálu. V testech zahrňte všechny pozorované hodnoty a zkontrolujte nemapované úrovně. Pravidla udržujte bez překryvů, aby byla snadno kontrolovatelná.

## 05 / Výběr submessage

submessage.token určuje token těla. submessage.messageid.token určuje směrovací token. Definice má jednu dokumentovanou hodnotu messageid; nejde o volný popisek. Po výběru definice se její patterns zkoušejí v pořadí a první úspěšný vyhrává. Definice bez messageid je výchozí pro jinak nedefinovaná ID. [S3]

Konfigurace | Význam
--- | ---
submessage.token=Message | Parsuje zachycenou hodnotu Message.
submessage.messageid.token=EventId | Podle EventId vybírá definici.
submessage[0].messageid=136 | Definice je určena pro hodnotu ID 136.
submessage[0].pattern.count=2 | Zkouší pattern[0], případně pattern[1].

### ID 1 a ID 2 mají shodný formát těla

Úplný příklad na další straně opakuje stejný pattern pod dvěma ID. Je to záměrně explicitní. Nepovažujte messageid=1,2 ani messageid=1|2 za dokumentovaný seznam ID: jeho podpora nebyla v prověřených zdrojích doložena.

Výchozí definice může sdílet regex mezi jinak neobslouženými ID, ale přijímá i další neznámá ID. Neomezuje výběr pouze na 1 a 2. Příklad ponechává deklarovaný směrovací token; nespoléhá na vynechání globálního nastavení messageid tokenu.

### Diagnostická otázka před úpravou regexu

Pokud Message obsahuje celou větu a současně slouží jako směrovací token, porovnejte celou tuto hodnotu s ID definice. Definice nazvaná Var1 nebude vybrána pro větu DROP. Když neexistuje odpovídající směrování, úprava regexu těla tento problém nevyřeší.

Editor může nezávisle zobrazit shodu nevybraného patternu. Jde o pomůcku při ladění, nikoli důkaz, že jej konektor zvolil. Skutečnou cestu a shodu vybraného patternu posuzujte odděleně.

## 06 / Úplný parser se submessages

Uložte jako training-submessages.sdkrfilereader.properties. Tento samostatný parser patří k training-submessages.log; nepřipojujte jej za předchozí úplný parser.

```properties
# Training example: exact IDs and one default
regex=id=(\\d+) body=(.*)
token.count=2
token[0].name=EventId
token[0].type=String
token[1].name=Message
token[1].type=String
event.deviceVendor=__stringConstant("ExampleVendor")
event.deviceProduct=__stringConstant("TrainingApp")
event.name=__stringConstant("Account activity")
event.deviceEventClassId=EventId
event.message=Message
submessage.messageid.token=EventId
submessage.token=Message
submessage.count=3

submessage[0].messageid=1
submessage[0].pattern.count=2
submessage[0].pattern[0].regex=user=(\\S+) src=(\\S+)
submessage[0].pattern[0].fields=event.sourceUserName,event.sourceAddress
submessage[0].pattern[0].types=String,IPAddress
submessage[0].pattern[1].regex=(anonymous)
submessage[0].pattern[1].fields=event.message

submessage[1].messageid=2
submessage[1].pattern.count=1
submessage[1].pattern[0].regex=user=(\\S+) src=(\\S+)
submessage[1].pattern[0].fields=event.sourceUserName,event.sourceAddress
submessage[1].pattern[0].types=String,IPAddress

# Default: intentionally no submessage[2].messageid
submessage[2].pattern.count=1
submessage[2].pattern[0].regex=(.*)
submessage[2].pattern[0].fields=event.message
submessage[2].pattern[0].extramappings=event.name=__stringConstant("Unclassified")
```

Poslední vzorek id=1 body=unexpected format záměrně neodpovídá patterns známého ID. Neznámé ID 9 použije default. Toto rozlišení je součást testu, nikoli chyba.

## 07 / Pole a operace submessage

Skupiny submessage se odkazují jako $1, $2 atd. fields určuje cílová event pole; types a formats popisují konverzi. mappings může místo přímého přiřazení použít operace, standardně oddělené svislítkem. [S2, kapitola Using Sub-Messages]

```properties
# Fragment: three captures; map address, port, then message
submessage[0].pattern[0].regex=(.*) Sender: (\\S+):(\\d+)
submessage[0].pattern[0].fields=event.sourceAddress,event.sourcePort,event.message
submessage[0].pattern[0].types=String,IPAddress,Integer
submessage[0].pattern[0].mappings=$2|$3|$1
```

Skupina 1 obsahuje text, skupina 2 adresu a skupina 3 port. Typy popisují tyto skupiny; mappings mění jejich přiřazení k uvedeným cílovým polím. Pokud změnu pořadí nepotřebujete, slaďte pořadí skupin s poli a mappings vynechte.

### Transformace ověřujte samostatně

Regex náhled dokazuje pouze extrakci. Nepotvrzuje úspěšnou konverzi adresy, výsledek operace ani zachování hodnoty pozdějším mapováním. Sepište očekávané surové skupiny i výsledná normalizovaná pole. Testujte vadné adresy a porty mimo požadovaný rozsah.

### Obecný pattern nesmí skrýt konkrétní

Vlastní cvičení: vložte (.*) před pattern určený pro DROP. Sledujte, že zprávu převezme obecný pattern. Vraťte konkrétní pattern před fallback a opakujte test. Zapisujte index vybrané cesty i hodnoty polí; zelená shoda sama nepotvrzuje správnou alternativu.

Nezaměňujte escape v properties, číslo regex skupiny a argument ArcSight operace. Uchovávejte editovatelný parser společně s testovacími logy, aby šly ověřit všechny vrstvy.

## 08 / Extra processors a více řádků

Extra processors propojují fáze parsování nebo obohacování. Vstup určuje field, typicky event pole, nikoli submessage.token. Použijte je, když událost kombinuje formáty, například hlavičku a strukturované tělo. [S7]

```properties
# Fragment; requires a separately deployed child parser
extraprocessor.count=1
extraprocessor[0].type=regex
extraprocessor[0].filename=training/body
extraprocessor[0].field=event.message
extraprocessor[0].flexagent=true
extraprocessor[0].clearfieldafterparsing=false
```

Fragment není úplné nasazení: training/body musí v daném konektoru odkazovat na existující navázaný parser. Před řetězením ověřte mazání vstupního pole a přepisování mapování. Popište vstupy, výstupy i selhání každého kroku. [S7]

### Multiline určuje hranice události

U podporovaných Regex Log File readerů určete začátek, případně konec události. Řádky se před parsováním spojí; dokumentovaný výchozí oddělovač je mezera. multiline.max.count omezuje velikost. Jednořádkový regex test toto chování readeru neověřuje. [S8]

```properties
# Fragment: start of a new timestamped event
multiline.starts.regex=\\d{4}-\\d{2}-\\d{2} .*
multiline.max.count=100
```

Praktický postup: testujte poslední událost na konci souboru, opožděné pokračování a chybějící konec. Problém hranic události neřešte jen uvolněním hlavního regexu.

## 09 / Příklad Windows události

Uvažujte 15polový záznam MSWinEventLog. V projektovém příkladu je token[12] Something (skupina 13), token[13] Message (skupina 14) a token[14] Size. Dodaná DROP událost má prázdné Something, ale neprázdné Message. Jména určuje parser; nejde o univerzální Windows názvy tokenů.

```properties
[DROP] Dropping received RCP messages from 192.0.2.10:3333.
```

Kontrola | Očekávané pozorování
--- | ---
Skupina 13 / token[12] | Prázdná hodnota mezi sousedními tabulátory
Skupina 14 / token[13] | Celá věta DROP
Token Info | Netft Trace Message včetně mezer
Token event ID | 136 při zachycení odpovídajícího pole

### Dvě nezávislé příčiny selhání

Kopie s dvojicemi EM SPACE neodpovídá hlavnímu regexu očekávajícímu tabulátory. Správně zachycené tělo zase neprojde směrováním, když je ID token Message, ale definice má ID Var1. Oddělovače a směrování opravujte samostatně. V produkci mezery automaticky nepřevádějte; prázdné sloupce mají význam.

### DROP fragment pouze pro tělo

```properties
submessage[0].pattern[0].regex=\\[DROP\\] Dropping received RCP messages from ((?:\\d{1,3}\\.){3}\\d{1,3}):(\\d{1,5})\\.
submessage[0].pattern[0].fields=event.sourceAddress,event.sourcePort
submessage[0].pattern[0].types=IPAddress,Integer
```

Fragment má dvě skupiny; neobsahuje vnější zachycení celé zprávy z některých dřívějších příkladů. Po změně skupin upravte fields i types. Vložte jej pouze pod zamýšlenou ID nebo default definici.

## 10 / Testování a nasazení

Následující postup je autorské doporučení, nikoli certifikační proces výrobce. Parser, surové vzorky, očekávané výsledky a verzi konektoru uchovávejte společně ve verzovacím systému.

Vrstva testu | Co zaznamenat
--- | ---
Properties | Dekódovaný regex; přesná jména a počty tokenů
Regex | Úplné shody i očekávané neshody
Submessage | Hodnotu ID, vybranou definici/pattern, skupiny
Konverze | Skutečně převedené adresy, porty a čas
Normalizace | Očekávaná event pole a severity
Reader / cíl | Hranice, kódování, čas a doručené události

### Výsledky přiloženého submessage logu

Řádek | Očekávaný výsledek
--- | ---
1 | ID 1 -> submessage[0].pattern[0]
2 | ID 1 -> submessage[0].pattern[1]
3 | ID 2 -> submessage[1].pattern[0]
4 | Neznámé ID 9 -> default submessage[2]
5 | Známé ID 1, jiné tělo -> neshoda patterns

Přidejte negativní vzorky: chybějící oddělovače, neúplné tělo, prázdná pole, cizí ID, dlouhé zprávy, diakritiku, změněné pořadí klíčů a vadný čas. Zahrňte i logy nepoužité při psaní regexu.

### Kontrola nasazení

Ověřte typ konektoru a jeho pravidla názvů i umístění. Příručka CE 24.3 uvádí vlastní parsery pod current/user/agent/flexagent a zvláštní názvy syslog parserů v podsložce syslog. File Reader parser není automaticky Syslog parser. Zálohujte stávající parser, nasaďte do testovacího konektoru, zkontrolujte agent.log a porovnejte cílové události. [S2, instalace a vlastní syslog parsery]

Lokální testy vzorků ověřují extrakci a směrování. Partial match ani úspěšný .NET náhled nezaručují konverze v Java runtime, operace, multiline hranice nebo doručení do cíle.

## 11 / Diagnostika a předání

Příznak | Co prověřit nejdřív
--- | ---
Neshoda hlavního regexu | Surové oddělovače, kódování, hranice a properties escapes
Prázdný submessage input | Je hlavní shoda úplná? Není vybraná skupina prázdná?
Žádné odpovídající ID/default | Porovnejte zachycené ID s konfigurací včetně mezer.
Náhled sedí, batch selže | Nezávislý náhled nemusí být skutečně vybraná cesta.
Shoda má chybná pole | Čísla skupin, pořadí mappings a význam rolí
Správný text, chybný čas | Formát, locale, pásmo a kalendářní rok
Jeden pattern zachytí vše | Obecné patterns přesuňte za konkrétní.
Lokálně funguje, v konektoru ne | Java, konverze, operace a nastavení readeru

### Co předat v repozitáři parseru

Přidejte properties soubor, umělé/anonymizované surové logy, očekávané cesty a pole, poznámky k instalaci, build konektoru a historii změn. Rozlište lokální kontroly od testů ve skutečném SmartConnectoru. Do příkladů nepatří hesla ani osobní údaje.

### Hranice této příručky

Jde o praktickou příručku Regex parserů. Sběr JSON/XML/databází/Kafka/REST, úplné katalogy operací, kategorizace a správa konektoru vyžadují vlastní manuály. Nepodporovanou či neověřenou syntaxi nevymýšlíme jen pro kratší příklad.

Přiložené konfigurace jsou vlastní výukové příklady. Očekávané skupiny a směrování ověřuje projektový ParserCore používající .NET. Přijímací test na SmartConnectoru je stále nutný. Zachovejte přiložené editovatelné zdroje, abyste nemuseli kód kopírovat z PDF.

Slovníček: capture = zachycená skupina; token = pojmenovaná typovaná hodnota; mapping = přiřazení do normalizovaného pole; message ID = hodnota pro směrování; pattern = regex těla; default = cesta pro jinak nedefinovaná ID; extra processor = navázaná fáze zpracování.

## Sources
[S1] OpenText: SmartConnector CE 25.1 documentation index: https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-25.1/
[S2] OpenText: Developer's Guide to ArcSight FlexConnectors, CE 24.3 (244 pages): https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-25.1/pdfdoc/flexconn_devguide/flexconn_devguide.pdf
[S3] OpenText: Using Sub-Messages for Multiple File Formats (8.4): https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-8.4/flexconn_devguide/Content/convertFlex/Sub_Messages.htm
[S4] OpenText: Token Declarations (8.4): https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-8.4/flexconn_devguide/Content/convertFlex/TokenDeclarations.htm
[S5] OpenText: Event Mapping (8.4): https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-8.4/flexconn_devguide/Content/convertFlex/Event-Mapping.htm
[S6] OpenText: Severity Mapping (8.4): https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-8.4/flexconn_devguide/Content/convertFlex/SeverityMapping.htm
[S7] OpenText: Extra Processors (8.4): https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-8.4/flexconn_devguide/Content/convertFlex/ExtraProcessors.htm
[S8] OpenText: Supported Regular Expressions / Multi-line Parsing (8.4): https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-8.4/flexconn_devguide/Content/convertFlex/Regular_Expressions.htm
[S9] OpenText: Parser Properties for all Regex FlexConnectors (8.4): https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-8.4/flexconn_devguide/Content/convertFlex/ConfigProperties-for-all-RegexFlexConnectors.htm
[S10] Oracle: java.util.Properties, Java SE 21: https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Properties.html
[S11] Oracle: java.util.regex.Pattern, Java SE 21: https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/regex/Pattern.html
[S12] Oracle: java.text.SimpleDateFormat, Java SE 21: https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/text/SimpleDateFormat.html
[S13] OpenText: SIEM SmartConnector 26.1 portal (version context only): https://docs.microfocus.com/doc/2097/26.1/home