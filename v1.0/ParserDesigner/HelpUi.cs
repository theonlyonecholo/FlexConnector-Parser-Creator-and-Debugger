using System.Runtime.CompilerServices;

namespace ParserDesigner;

/// <summary>Attaches contextual help, accessibility descriptions and a footer once per form.</summary>
internal static class HelpUi
{
    private sealed class State
    {
        public readonly ToolTip Tips = new() { AutoPopDelay = 20000, InitialDelay = 450, ReshowDelay = 100, ShowAlways = true };
        public readonly Dictionary<Control, string> Topics = [];
    }
    private static readonly ConditionalWeakTable<Form, State> States = new();
    public static string DefaultTopic(Form form) => form switch
    {
        EventMappingDialog => "mapping",
        TokenDialog => "tokens",
        TokenFormatDialog => "formats",
        SubmessageDialog => "submessages",
        BatchForm => "batch",
        AdvancedDialog => "severity",
        ParserSourceDialog => "properties",
        _ => "start"
    };
    public static void Describe(Control control, string topic, string text)
    {
        var form = control.FindForm();
        if (form is null || !States.TryGetValue(form, out var state))
            return;
        state.Tips.SetToolTip(control, text + "\nF1: podrobná nápověda.");
        control.AccessibleDescription = text;
        state.Topics[control] = topic;
    }
    public static void Install(Form form)
    {
        if (form is HelpWindow || States.TryGetValue(form, out _))
            return;
        var state = new State();
        States.Add(form, state);
        string topic = DefaultTopic(form);
        void Walk(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                string? tip = ButtonTip(c.Text);
                if (c is Button && tip is not null)
                    Describe(c, TopicForButton(c.Text, topic), tip);
                else if (c is Button && c.Text.Contains(" — ", StringComparison.Ordinal))
                    Describe(c, "formats", "Vloží tento symbol formátu na pozici kurzoru; označený text nahradí.");
                else if (c is TextBoxBase or ComboBox or TreeView or DataGridView or CheckBox or NumericUpDown)
                    Describe(c, topic, "Význam a postup použití najdete přes F1 v tématu „" + HelpTopics.All.Single(t => t.Id == topic).Title + "“.");
                if (c is DataGridView grid)
                    foreach (DataGridViewColumn column in grid.Columns)
                        column.ToolTipText = ColumnTip(column.HeaderText);
                Walk(c);
            }
        }
        Walk(form);
        var footer = new StatusStrip { SizingGrip = false };
        var help = new ToolStripButton("Nápověda (F1)") { ToolTipText = "Otevře českou nápovědu s vyhledáváním a příklady parseru." };
        help.Click += (_, _) => Show(form, topic);
        footer.Items.Add(help);
        footer.Items.Add(new ToolStripStatusLabel("Popisy polí: najeďte myší • F1 vysvětlí aktivní pole"));
        form.Controls.Add(footer);
        form.KeyPreview = true;
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.F1)
                return;
            e.Handled = e.SuppressKeyPress = true;
            Control? active = form.ActiveControl;
            while (active is ContainerControl container && container.ActiveControl is not null)
                active = container.ActiveControl;
            while (active is not null && !state.Topics.ContainsKey(active))
                active = active.Parent;
            Show(form, active is not null ? state.Topics[active] : topic);
        };
        form.FormClosed += (_, _) => state.Tips.Dispose();
    }
    private static void Show(Form owner, string topic)
    {
        using var help = new HelpWindow(topic);
        help.ShowDialog(owner);
    }
    private static string TopicForButton(string text, string fallback) => text switch
    {
        "Tokeny z regexu…" => "tokens",
        "Submessages…" or "Upravit submessage…" => "submessages",
        "Event mapping…" => "mapping",
        "Severity / processors…" => "severity",
        "Test souboru logů…" => "batch",
        "Návrh formátu…" => "formats",
        "Přidat šablonu" => "processors",
        "Test / Debug (F5)" or "◀ Předchozí záchyt" or "Další záchyt ▶" or "Načíst zprávu…" => "regex",
        "Konfigurace → Regex" or "Regex → Konfigurace" or "Uložit" or "Uložit jako…" or "Otevřít .properties" or "Upravit parser…" => "properties",
        _ => fallback
    };
    private static string? ButtonTip(string text) => text switch
    {
        "Event mapping…" => "Otevře editor přiřazení tokenů, konstant a výrazů do cílových event polí.",
        "Přidat / aktualizovat" => "Zapíše návrh do tabulky mapování; stejné cílové pole aktualizuje.",
        "Odstranit mapování" => "Odstraní vybraný řádek. Změna se zapíše do konfigurace až po použití dialogu.",
        "Otevřít .properties" => "Načte konfiguraci parseru a hlavní regex ze souboru. Před opuštěním změn nabídne uložení.",
        "Uložit" => "Zapíše konfiguraci na disk. Změněný pracovní regex nabídne nejprve zapsat do konfigurace.",
        "Uložit jako…" => "Uloží konfiguraci do zvoleného souboru.",
        "Konfigurace → Regex" => "Načte a dekóduje vlastnost regex z konfigurace do pracovního pole Regex.",
        "Regex → Konfigurace" => "Escapuje pracovní regex a zapíše jej do properties. Tokeny se tím nepřegenerují.",
        "Severity / processors…" => "Nastavení převodu závažnosti a odkazů na navazující parsery.",
        "Tokeny z regexu…" => "Vytvoří tokeny ze zachytávacích skupin; umožní zvolit názvy, typy a formáty.",
        "Submessages…" => "Definice variant těla logu podle message ID a pořadí patternů.",
        "Test souboru logů…" => "Vyhodnotí jeden parser nad řádky souboru; umožní procházení a ladění variant.",
        "Test / Debug (F5)" => "Vyhodnotí pracovní regex a zobrazí shody a opakované záchyty. Nejde o krokování interního backtrackingu.",
        "Zastavit" => "Požádá o zrušení testu. Běžící regex je omezen timeoutem.",
        "◀ Předchozí záchyt" or "Další záchyt ▶" => "Vybere sousední záchyt v Debug tabulce a zvýrazní jej v Message.",
        "Načíst zprávu…" => "Načte obsah textového souboru do Message pro test jednoho vstupu.",
        "Otevřít logy…" => "Načte soubor ve zvoleném kódování; každý řádek je samostatný log.",
        "Otestovat vše" => "Znovu vyhodnotí všechny načtené logy aktuálním parserem.",
        "← Log" or "Log →" => "Přejde na sousední log v aktuálně filtrovaném seznamu.",
        "Upravit submessage…" => "Otevře definici použitou nebo zkoušenou pro vybraný log. Po použití přetestuje soubor.",
        "Upravit parser…" => "Upraví celý text konfigurace; po použití se logy přepočítají.",
        "Použít v konfiguraci" => "Přenese návrh do hlavní konfigurace. Na disk jej následně zapíšete tlačítkem Uložit.",
        "Použít a přepočítat logy" => "Převezme návrh konfigurace do testovacího okna a znovu vyhodnotí soubor.",
        "Návrh formátu…" => "Pro vybraný token otevře šablony a skládání formátu podle jeho typu.",
        "Použít formát" => "Vrátí navržený formát do vybraného řádku tokenu.",
        "Bez formátu" => "Vyprázdní formát; při exportu tokenu se vlastnost format odstraní.",
        "Zrušit" => "Zavře tento dialog bez použití jeho návrhu.",
        "+ Submessage" => "Přidá definici pro další message ID s počátečním patternem.",
        "+ Pattern" => "Přidá alternativní regex do vybrané submessage.",
        "Odstranit" => "Odstraní vybraný pattern nebo celou submessage podle uzlu stromu.",
        "↑ Dříve" or "↓ Později" => "Posune definici nebo pattern v pořadí. První úspěšný pattern vyhrává.",
        "Testovat log s těmito změnami" => "Otestuje celý vybraný log aktuálním návrhem parseru včetně routingu submessages.",
        "Přidat šablonu" => "Připojí blok extra processoru zvoleného typu. Doplňte skutečnou cestu a parametry.",
        _ => null
    };
    private static string ColumnTip(string title) => title switch
    {
        "Typ" => "Datový typ tokenu; má odpovídat cílovému event poli. F1: seznam typů.",
        "Formát" => "Dekódovaný formát pro převod tokenu; například yyyy-MM-dd HH:mm:ss. Pomocník: Návrh formátu.",
        "Název" or "Název / token" => "Jméno používané jako odkaz v mapování. Barva odpovídá tokenu v textu.",
        "Skupina → token" => "Skupina 1 odpovídá token[0]; nezachytávací skupiny token nemají.",
        "Pozice" or "Pozice (0-based)" => "Pozice v původním textu: UTF-16 index od nuly; -1 znamená bez záchytu.",
        "Délka" => "Délka záchytu v jednotkách UTF-16. Nula je prázdný záchyt.",
        "Submessage / pattern" => "Použitá nebo dostupná definice. Detail ukáže jednotlivé pokusy.",
        "Message ID" => "Hodnota tokenu, podle níž se vybírá submessage.",
        "Výsledek" => "Rozlišuje shodu, neshodu hlavního regexu, chybějící submessage, neshodu těla a chybu.",
        "Řádek" => "Původní číslo řádku v souboru logů.",
        "Skupina" => "Číslo zachytávací skupiny. Skupina 0 je celá shoda.",
        "Záchyt" => "Pořadí opakovaného zachycení stejné skupiny.",
        "Shoda" => "Pořadí shody nalezené v Message.",
        "Úspěch" => "Zda tato skupina v dané shodě zachytila hodnotu.",
        "Hodnota" => "Zachycený text. Dlouhé hodnoty mohou být v tabulce zkrácené.",
        "Fáze" => "Zda záchyt pochází z hlavního regexu, tokenu nebo submessage.",
        "Token / pole" => "Jméno tokenu nebo cílového pole submessage; barva odpovídá záchytu.",
        _ => "Podrobnosti a příklady: F1."
    };
}

