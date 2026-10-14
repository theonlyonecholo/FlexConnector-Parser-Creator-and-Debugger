# ArcSight FlexConnector Parser Designer — C#

Windows aplikace v **C# / WinForms / .NET 10** ve složce `csharp`.
Publikační balíček obsahuje pouze C# řešení a jeho podpůrné soubory.

## Sestavení a spuštění

Pro sestavení potřebujete .NET 10 SDK, pro běh Windows Desktop Runtime 10.
Projekt nepoužívá externí NuGet balíčky. `NuGet.Config` vypíná vzdálené zdroje;
referenční balíčky .NET a Windows Desktop musí být součástí instalovaného SDK.
Otevřete `csharp/ArcSight.ParserDesigner.slnx` ve Visual Studiu podporujícím .NET 10,
nebo z kořene projektu spusťte:

```powershell
dotnet build csharp/ArcSight.ParserDesigner.slnx -c Release
dotnet run --project csharp/ParserCore.Tests/ParserCore.Tests.csproj -c Release
dotnet run --project csharp/ParserDesigner/ParserDesigner.csproj -c Release
```

Výstup: `csharp/ParserDesigner/bin/Release/net10.0-windows/ArcSight.ParserDesigner.exe`.
Při kopírování přeneste celou výstupní složku, nikoli pouze `.exe`.
Testy jsou konzolový program; při neúspěchu vracejí nenulový návratový kód.

## Highlight a debugger

Horní ovládání je rozdělené do nabídek **Soubor**, **Parser** a **Testování**.
Soubor obsahuje otevření/uložení parseru a načtení zprávy. Parser řadí akce
podle postupu: tokeny → submessages → event mapping → severity / processors;
obsahuje také přenos regexu do konfigurace a zpět. Testování nabízí test jedné
zprávy i souboru logů. Uložit a Test logů jsou navíc přímo na liště.
Při zmenšení okna nejsou akce skryté ve vodorovně posouvané řadě tlačítek.
Nastavení náhledu a akce testu se podle šířky zalamují; totéž platí pro
nástroje testu logů a editoru event mapping. Minimální hlavní okno je 960 × 750.

Ve všech hlavních oknech je dostupná česká **Nápověda (F1)** ve spodní liště.
F1 při práci v poli otevře související téma; najetí myší na ovládací prvek
zobrazí krátké vysvětlení. Popisy mají také hlavičky sloupců tabulek.
Nápověda obsahuje 11 témat s vyhledáváním v názvech i celém obsahu: první parser,
regex a debugger, properties, tokeny a typy, formáty, event mapping, submessages,
test logů, severity, extra processors a meze lokálního náhledu. Obsahuje příklady
zápisu a vysvětlení parametrů podle přiložené příručky včetně odkazů na stránky.
Funguje offline bez nutnosti mít PDF vedle spuštěné aplikace. Klávesa Esc ji zavře.

- Do **Message** vložte zprávu a do **Regex** dekódovaný výraz, například `\d+`.
  Náhled vždy používá aktuální pole Regex, i před zápisem do konfigurace.
- **Automaticky** vyhodnotí výraz po krátké pauze v psaní a obarví všechny shody
  žlutě; zachycené tokeny pak dostanou odlišné pastelové barvy. Stejné barvy jsou
  u názvů v Debug tabulce. Opakované záchyty jedné skupiny mají stejnou barvu;
  u vnořených skupin má přednost menší rozsah. Změna textu, neplatný regex
  a žádná shoda odstraní staré zvýraznění.
- **Test / Debug (F5)** otevře tabulku shod, skupin a jednotlivých opakovaných
  záchytů: číslo, název / token, úspěch, pozice, délka a hodnota. Skupina 0 je celá
  shoda; nezachycená volitelná skupina má pozici -1. Nulová shoda má pozici, ale
  nemá text k obarvení.
- Kliknutí na řádek nebo **Předchozí / Další záchyt** obarví vybraný rozsah modře.
  Pozice jsou UTF-16 indexy od nuly; při Trim se přepočítají do původního Message.
- **Kliknutí do zachytávací skupiny v poli Regex** zvýrazní její záchyty v Message
  modře. Funguje i pohyb kurzoru klávesnicí. U vnoření se vybere nejvnitřnější
  zachytávací skupina; opakované záchyty se zvýrazní ve všech nalezených shodách.
  Stavový řádek uvádí skupinu, token a počet záchytů, případně chybějící záchyt
  nebo pozice prázdných záchytů. Kliknutí mimo skupiny obnoví barvy tokenů.
  Při vypnutém automatickém náhledu nejprve spusťte F5. Mapování pozic podporuje
  běžné a pojmenované skupiny, lookaround, komentáře a inline příznaky;
  pro regexy s podmíněnými nebo balancing skupinami je vypnuté.
- **IgnoreCase**, **Multiline**, **Singleline**, **Celá zpráva** a **Trim mezery/tab**
  slouží k experimentování. Bez Celá zpráva se hledají dílčí shody.
  Přepínače náhledu se neexportují. Požadované příznaky zapište přímo do výrazu,
  například `(?i)`.
- Detail ukazuje chyby syntaxe s pozicí, čas běhu, nesoulad `token.count` a náhled
  severity první shody. Vyhodnocení běží na pozadí s timeoutem (výchozí 250 ms).
  **Zastavit** zruší požadavek; probíhající match doběhne nejpozději po timeoutu.
  Starší výsledky nepřepíší novější vstup.

Debugger prochází **výsledky a záchyty**, nikoli interní instrukce či backtracking.
Používá **.NET regex**, nikoli Java engine ArcSightu; syntaxe, Unicode pravidla
a číslování pojmenovaných skupin se mohou lišit. Nenahrazuje test v konektoru.

Limity: 250 000 znaků zprávy, 16 000 znaků regexu, 1 000 shod, 5 000 řádků
záchytů, celkem 2 000 000 znaků zachycených hodnot a přibližně 2 sekundy celkového
vyhodnocování plus právě běžící match. Zkrácení je označeno. Tabulka zobrazí
nejvýše 500 znaků jedné hodnoty; zvýraznění používá celou délku.

## Konfigurace

Textové editory podporují **Ctrl+Z** pro posledních **100 kroků** a **Ctrl+Y**
nebo **Ctrl+Shift+Z** pro obnovení. Stejné volby jsou v nabídce pravého tlačítka.
Historie je samostatná pro Message, Regex, konfiguraci a textová pole dialogů.
Zapsání vygenerovaných tokenů, severity, processorů nebo submessages do hlavní
konfigurace lze v tomto poli vrátit jedním krokem. Zvýrazňování textu historii
nemění; nová úprava po vrácení změn zruší větev pro obnovení.
Otevření jiného parseru historii konfigurace a regexu vymaže. V editoru
submessages začíná historie polí znovu při přepnutí na jiný uzel stromu.
Historie se neukládá na disk a nevztahuje se na strukturální operace ve stromu
nebo na tabulku návrhu tokenů; jejich použití lze vrátit v hlavní konfiguraci.

**Tokeny z regexu…** vytvoří návrh `token.count` a `token[n].name/type/format`
z aktuálního pole Regex. Zachytávající skupina 1 odpovídá `token[0]`;
nezachytávající skupiny `(?:…)` a skupina 0 se ignorují. Pro nové pojmenované
skupiny se použije jejich název, jinak Token1, Token2, …; kolize nových názvů
dostanou příponu. Výchozí typ je String. Existující název, typ, formát a vlastní
metadata se zachovají podle indexu. V náhledu lze názvy, typy a formáty upravit.
Sloupec **Ukázka z Message** ukazuje zachycený text tokenu z první shody aktuální
zprávy, u opakované skupiny poslední záchyt. Náhled přebírá nastavení hlavního
debuggeru. Prázdný záchyt a chybějící záchyt se zobrazují odlišně; delší text
uvidíte po najetí na buňku. Ukázka není typově převedená a nezapisuje se do parseru.
Sloupec **Typ** nabízí String, Integer, Long, Double, IPAddress, IPv6Address,
MacAddress, Date, Time, TimeStamp a RegexToken. Neznámý typ načtený ze souboru
se zachová v nabídce. Double příručka uvádí pro ESM domain field sets (str. 204).
Při výběru Date, Time, TimeStamp nebo RegexToken se do prázdného pole Formát
doplní výchozí návrh. Existující formát se nepřepisuje; při změně typu jej ověřte.

Vyberte řádek tokenu a **Návrh formátu…**. Dialog nabízí šablony a volnou úpravu
formátu. U data/času vloží tlačítka symboly a oddělovače na pozici kurzoru;
funguje Ctrl+Z. **Použít formát** přenese návrh do vybraného řádku, **Zrušit**
jej zahodí. **Bez formátu** vyprázdní pole; při exportu se vlastnost odstraní.
Používá se syntaxe ArcSight/Java z příručky (str. 196–197), například
`yyyy-MM-dd HH:mm:ss.SSS`, nikoli .NET formátovací řetězce. RegexToken používá
`s/{exp}/{subst}/`. Návrhář nevykonává typové převody ani substituce a neověřuje
shodu formátu s konkrétní hodnotou logu. Do pole zadávejte dekódovaný formát;
escapování pro properties provede export.

Použití zapíše i aktuální regex, odstraní tokeny nad novým počtem a upozorní
na zanikající názvy. Odkazy v event mappingu a submessages se nepřejmenovávají;
po změně struktury skupin je zkontrolujte. Duplicitní / prázdné názvy a prázdné
typy jsou odmítnuty. Číslování pochází z .NET, u pojmenovaných skupin ověřte
shodu s Java enginem konektoru.

**Otevřít .properties** načte celý parser. **Konfigurace → Regex** dekóduje hlavní
regex a načte Trim podle `trim.message`. V konfiguraci zapisujte `\\d`, v poli
Regex pouze `\d`. **Regex → Konfigurace** výraz escapuje a nahradí starší definice
klíče. **Uložit / Uložit jako…** při změněném poli Regex nabídne jeho zápis.
Automatické zápisy nahrazují existující klíč na původním místě a nepřidávají
úvodní prázdné řádky ani mezery. U nedokončeného pokračovacího řádku se přidá
jen nezbytný oddělovač, aby se nová vlastnost nestala součástí předchozí hodnoty.

Podporována je Java properties syntaxe: komentáře, oddělovače, pokračování řádků,
Unicode escapes, case-sensitive klíče a poslední definice duplicitního klíče.
Čtení používá Latin-1 nebo kódování podle BOM, nikoli automatickou detekci UTF-8
bez BOM. Export používá ASCII s Unicode escapes. Nezměněné ASCII bloky včetně
komentářů zůstávají zachovány; export není příslibem bajtově totožného souboru.
Ukládání nejprve zapisuje dočasný soubor ve stejném adresáři.

## Event mapping

**Event mapping…** v hlavním okně otevře tabulku přímých přiřazení `event.*`.
V horní nabídce vyberte cílové pole (nebo napište vlastní) a režim **Token**,
**Konstanta** či **Výraz**. Token nabídne deklarované názvy; Konstanta vytvoří
`__stringConstant("text")`; Výraz uloží dekódovanou ArcSight operaci.
**Přidat / aktualizovat** vloží návrh do tabulky nebo aktualizuje stejné pole.
Existující řádky lze upravit přímo v tabulce; **Odstranit mapování** odstraní výběr.
Rozpracovaný návrh nahoře je nutné nejprve přidat nebo vyprázdnit.

Editor kontroluje duplicitní pole a prázdné hodnoty. Upozorní na neznámý přímý
token, nesoulad typu tokenu s cílovým polem, chybějící `event.name` a konflikt
`requestUrl` s URI komponentami. Upozornění neblokují použití, protože výsledky
může doplňovat další část parseru. Katalog polí a typů vychází z přílohy E
přiložené příručky (str. 198–203); vlastní pole i výrazy se zachovají.

**Použít v konfiguraci** přenese tabulku do hlavního editoru, poté **Uložit**
zapíše soubor. Zrušit návrh zahodí. Ostatní části konfigurace včetně submessage
mappings zůstávají zachovány. Editor nevykonává ArcSight operace ani celkové
event mapping; kontrola typů se týká přímých tokenů. Kontextová nápověda: F1.

## Severity a extra processors

**Severity / processors…** otevře dva editory. **Použít v konfiguraci** zapíše
změny do hlavního textu; na disk je následně uložíte tlačítkem Uložit.

Severity podporuje veryhigh, high, medium, low, seznamy (`OPEN,CLOSE`) a číselné
rozsahy včetně hranic (`400..599`). Prázdné pravidlo se odstraní. Text se porovnává
přesně, překryvy se hlásí jako nejednoznačné bez odhadu priority. Náhled vyhodnotí
přímý odkaz `event.deviceSeverity` na zachycený token první shody. ArcSight výrazy
lze uložit, ale lokálně se neprovádějí.

Extra processors obsahují šablony pro delimited, json, keyvalue, map, ntsubparser,
regex, standardkeyvalue a xml. Kontroluje se typ, filename, boolean hodnoty
a souvislé indexy 0,1,2,…; `extraprocessor.count` se přepočítá při použití změn.
Vzorové cesty nahraďte skutečnými. Po odebrání processoru přečíslujte následující
indexy. Vlastní parametry a podmínky zůstávají zachovány. Navázané soubory se
**lokálně nespouštějí** a nekontroluje se jejich existence.

Obecné ArcSight operace, typové konverze, conditional mapping a skládání více
logových řádků se nesimulují. Jejich konfiguraci lze editovat a uložit.

## Test jednoho parseru proti souboru logů

**Submessages…** otevře editor podzpráv. Nastavte token těla podzprávy a token
message ID. Ve stromu můžete přidávat více submessages, každému přiřadit ID
nebo jej označit jako default a přidávat alternativní patterny. Šipky mění
pořadí vyhodnocování; počty a indexy se při použití přepočítají automaticky.
Každý pattern má regex, fields, types, formats, mappings a extramappings.
Regex zadávejte dekódovaný jako v hlavním poli Regex. Neznámé parametry se
zachovají. **Testovat log s těmito změnami** ověří aktuální návrh nad vybraným
logem ještě před použitím. Zrušit změny zahodí.
Kliknutí do skupiny regexu zvýrazní její pole ve vybraném celém logu a vypíše
název pole, pozici a délku. Pokud směrování vybere jiný pattern nebo log neodpovídá,
editor to oznámí. U opakované skupiny submessage se ukazuje poslední hodnota pole.

**Test souboru logů…** otevře Log Workbench s aktuální konfigurací.
Změněný regex lze před otevřením zapsat. Náhledové přepínače debuggeru se nepoužijí;
test vychází z konfigurace včetně `trim.message` a `trim.submessage`.

1. **Otevřít logy…** načte UTF-8 (s rozpoznáním BOM) nebo zvolenou Latin-1.
   Jeden řádek tvoří jeden log, včetně prázdných řádků. Zachovají se čísla řádků.
   Limity jsou 32 MB a 50 000 řádků. Multiline konfigurace události neskládá;
   takové události je třeba předem spojit do řádků.
2. Soubor se otestuje na pozadí. Průběh a **Zastavit** umožňují přerušení;
   nedokončené řádky zůstanou výslovně neotestované.
3. Přehled odlišuje shodu, neshodu hlavního regexu, chybějící submessage,
   neshodu submessage a chybu / timeout. Filtry zobrazí jen shody, problémy
   nebo neotestované logy. Šipky procházejí aktuální filtrovaný seznam.
4. Detail vybraného řádku zvýrazní tokeny a pole submessage odlišnými barvami
   se stejnou legendou v tabulce; vybraný token / pole se zvýrazní modře.
   Zobrazí message ID, dostupnou nebo vybranou
   cestu `submessage[n].pattern[m]`, zkoušené patterny a jejich výsledky.
5. **Upravit submessage…** otevře editor na použitém nebo prvním zkoušeném
   patternu vybraného logu. Přidejte alternativu pro odlišný formát logu
   a použijte změny; celý soubor se automaticky přetestuje. Výběr logu zůstane
   zachován, pokud jej aktuální filtr nadále zobrazuje.
6. **Upravit parser…** umožní upravit celou konfiguraci včetně hlavního regexu
   a tokenů. Po zavření testovacího okna se upravená konfigurace přenese do
   hlavního editoru; na disk ji zapíšete tlačítkem **Uložit**.

Routing používá `submessage.messageid.token` a `submessage.token`. Pro známé ID
se zkouší patterny v pořadí a vyhraje první shoda. Pouze neznámé ID použije
defaultní submessage bez vlastnosti `messageid`. Neshoda patternů známého ID
nepřechází na default. Regexy musí odpovídat celé zprávě / podzprávě.
`fields` jsou popisky zachycených skupin; transformační `mappings`,
`extramappings`, převody typů a extra processors se neprovádějí.

## Ukázky a ověření

Ověření zahrnuje Release sestavení, konzolové regresní testy a samostatný
`ParserDesigner.UiTests` pro skutečné WinForms ovládací prvky. UI test ověřuje
highlight, úpravy submessages, přetestování logů se zachováním výběru, filtry
a generování tokenů a ukládá náhledy do `build`.

- `examples/demo.sdkrfilereader.properties` + `examples/demo.log`: tři tokeny.
- `examples/severity-processors.sdkrfilereader.properties` + odpovídající `.log`:
  severity high, dva processory s ilustrativními odkazy na soubory.
- `examples/submessages.sdkrfilereader.properties` + `examples/submessages.log`:
  šest logů; čtyři shody, jedna neshoda submessage, jedna neshoda hlavního regexu.
- `ParserCore.Tests`: properties round trip, severity, processory, highlight rozsahy,
  opakované a nulové záchyty, Unicode pozice, příznaky, chyby, timeout a zrušení.

Ruční kontrola: `\d+` nad `abc12 x345` zvýrazní `12` a `345`. `(\d)+` nad `123`
zobrazí tři záchyty skupiny 1; procházení obarví jednotlivé číslice. Výraz `(`
odstraní highlight a zobrazí chybu. Ověřte také uložení a opětovné otevření parseru.

## Podklady

- `FlexConn_DevGuide.pdf`, strany 73–76, 86, 131–146 a 171–172.
- [Java Properties](https://docs.oracle.com/en/java/javase/26/docs/api/java.base/java/util/Properties.html).
- [.NET regex: timeout a skupiny](https://learn.microsoft.com/dotnet/standard/base-types/best-practices).
- [RichTextBox.SelectionBackColor](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.richtextbox.selectionbackcolor?view=windowsdesktop-10.0).
