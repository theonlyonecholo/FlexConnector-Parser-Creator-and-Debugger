
namespace ParserDesigner;

internal sealed partial class MainForm
{
    /// <summary>Builds the command groups in parser authoring order.</summary>
    private ToolStrip CreateToolbar()
    {
        var files = new ToolStrip { Name = "MainToolbar", Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden, CanOverflow = true, Padding = new Padding(0, 2, 0, 2), RenderMode = ToolStripRenderMode.System, Font = new Font("Segoe UI", 10) };
        ToolStripDropDownButton Menu(string title)
        {
            var menu = new ToolStripDropDownButton(title) { AutoToolTip = false };
            menu.DropDown.ShowItemToolTips = true;
            files.Items.Add(menu);
            return menu;
        }
        void Item(ToolStripDropDownButton menu, string title, Action action, string help)
        {
            var item = new ToolStripMenuItem(title) { ToolTipText = help, AccessibleDescription = help };
            item.Click += (_, _) => Guard(action);
            menu.DropDownItems.Add(item);
        }
        var fileMenu = Menu("Soubor");
        Item(fileMenu, "Otevřít parser…", OpenFile, "Načte konfiguraci .properties a hlavní regex.");
        Item(fileMenu, "Uložit   Ctrl+S", () => SaveFile(false), "Zapíše konfiguraci na disk.");
        Item(fileMenu, "Uložit jako…", () => SaveFile(true), "Uloží parser do jiného souboru.");
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        Item(fileMenu, "Načíst zprávu…", LoadMessage, "Načte text do Message pro test jednoho vstupu.");
        var parserMenu = Menu("Parser");
        Item(parserMenu, "1. Tokeny z regexu…", GenerateTokens, "Pojmenování zachytávacích skupin, výběr typů a formátů.");
        Item(parserMenu, "2. Submessages…", EditSubmessages, "Varianty těla logu a pořadí patternů.");
        Item(parserMenu, "3. Event mapping…", EditEventMapping, "Přiřazení tokenů, konstant a výrazů do event polí.");
        Item(parserMenu, "4. Severity / processors…", EditAdvanced, "Normalizace závažnosti a navazující parsery.");
        parserMenu.DropDownItems.Add(new ToolStripSeparator());
        Item(parserMenu, "Regex → Konfigurace", WritePattern, "Escapuje pracovní regex a zapíše jej do properties.");
        Item(parserMenu, "Konfigurace → Regex", ReadPattern, "Načte dekódovaný regex z konfigurace.");
        var testMenu = Menu("Testování");
        Item(testMenu, "Test zprávy / Debug   F5", () => { tabs.SelectedIndex = 1; _ = AnalyzeAsync(); }, "Zobrazí shody a záchyty pracovního regexu.");
        Item(testMenu, "Test souboru logů…", OpenBatch, "Otestuje celý soubor a umožní procházet a ladit logy.");
        Item(testMenu, "Zastavit", Stop, "Zruší probíhající náhled.");
        files.Items.Add(new ToolStripSeparator());
        var save = new ToolStripButton("Uložit") { ToolTipText = "Uložit konfiguraci (Ctrl+S)" };
        save.Click += (_, _) => Guard(() => SaveFile(false));
        files.Items.Add(save);
        var batchTest = new ToolStripButton("Test logů…") { ToolTipText = "Test jednoho parseru proti souboru logů" };
        batchTest.Click += (_, _) => Guard(OpenBatch);
        files.Items.Add(batchTest);
        return files;
    }
}
