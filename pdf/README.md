# FlexConnector practical handbook / Praktická příručka

Edition / vydání: 2026-10-19. Independent tutorial / nezávislý výukový text.

## English

- `FlexConnector_Handbook_EN.pdf`: 15-page English guide.
- `FlexConnector_Handbook_CS.pdf`: equivalent Czech guide.
- The `.md` files are editable text versions, with unwrapped code fragments.
- `examples/training-basic.sdkrfilereader.properties` and its `.log` file: a complete five-token parser and two matching events.
- `examples/training-submessages.sdkrfilereader.properties` and its `.log` file: exact-ID routing, two alternatives for ID 1, a shared pattern for ID 2 and a default. The fifth event deliberately fails its known-ID patterns.

Use files rather than copying long code lines from the PDF: PDF lines may wrap visually. These are two independent parsers, not fragments to concatenate. Configure the input, reader and source time zone in the target connector. Dates in the basic fixture have no offset.

The guide identifies the public CE 24.3 FlexConnector manual linked by the SmartConnector CE 25.1 documentation index, plus explicitly labelled 8.4 HTML references and Oracle Java syntax references. Newer product portals are not evidence that the parser guide has the same version. Sources and verification date are listed in each PDF. The old user-supplied PDF was not used as the content basis.

Validation performed: both PDFs rendered (all 30 pages), Czech glyphs and representative full-size pages visually checked, text bounds checked, hyperlinks present. The two basic events and all five submessage outcomes passed the project's .NET ParserCore checks. ArcSight transformations, Java runtime behavior, multiline readers and actual destination delivery were NOT executed in a SmartConnector.

## Česky

Obě PDF mají 15 stran a odpovídající obsah. Markdown soubory jsou editovatelné a obsahují nezalomené fragmenty kódu. Ve složce `examples` jsou dva samostatné úplné parsery se surovými logy. Nespojujte je do jednoho souboru. Pátý submessage log záměrně selže na patterns známého ID; nejde o chybu ukázky.

Kód kopírujte ze souborů, nikoli z vizuálně zalomených PDF řádků. V cílovém konektoru nastavte vstup, reader a časové pásmo zdroje. Čas základního příkladu nemá offset.

Zdrojem je veřejná příručka CE 24.3 odkazovaná dokumentací SmartConnector CE 25.1, výslovně označené HTML kapitoly 8.4 a reference Oracle. Novější verze produktového portálu neznamená stejně nové vydání příručky parserů. Datum ověření a odkazy jsou v každém PDF. Přiložený starý manuál nebyl použit jako základ obsahu.

Ověření: vykresleno všech 30 stran, vizuálně zkontrolována čeština a reprezentativní strany v plné velikosti, ověřeny hranice textu a odkazy. ParserCore na .NET potvrdil dva základní logy a všech pět očekávaných submessage výsledků. Operace ArcSight, Java runtime, multiline reader ani doručení do cíle nebyly ověřeny ve skutečném SmartConnectoru.
