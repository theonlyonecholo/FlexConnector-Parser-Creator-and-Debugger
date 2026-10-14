
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
        var fileMenu = Menu("File");
        Item(fileMenu, "Open parser…", OpenFile, "Load the .properties configuration and main regex.");
        Item(fileMenu, "Save   Ctrl+S", () => SaveFile(false), "Write the configuration to disk.");
        Item(fileMenu, "Save as…", () => SaveFile(true), "Save the parser to another file.");
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        Item(fileMenu, "Load message…", LoadMessage, "Load text into Message to test a single input.");
        var parserMenu = Menu("Parser");
        Item(parserMenu, "1. Tokens from regex…", GenerateTokens, "Name capturing groups and select their types and formats.");
        Item(parserMenu, "2. Submessages…", EditSubmessages, "Log body variants and pattern order.");
        Item(parserMenu, "3. Event mapping…", EditEventMapping, "Assign tokens, constants and expressions to event fields.");
        Item(parserMenu, "4. Severity / processors…", EditAdvanced, "Severity normalization and downstream parsers.");
        parserMenu.DropDownItems.Add(new ToolStripSeparator());
        Item(parserMenu, "Regex → Configuration", WritePattern, "Escape the working regex and write it to properties.");
        Item(parserMenu, "Configuration → Regex", ReadPattern, "Load the decoded regex from the configuration.");
        var testMenu = Menu("Testing");
        Item(testMenu, "Test message / Debug   F5", () => { tabs.SelectedIndex = 1; _ = AnalyzeAsync(); }, "Show matches and captures for the working regex.");
        Item(testMenu, "Test log file…", OpenBatch, "Test an entire file and browse and debug its logs.");
        Item(testMenu, "Stop", Stop, "Cancel the running preview.");
        files.Items.Add(new ToolStripSeparator());
        var save = new ToolStripButton("Save") { ToolTipText = "Save configuration (Ctrl+S)" };
        save.Click += (_, _) => Guard(() => SaveFile(false));
        files.Items.Add(save);
        var batchTest = new ToolStripButton("Test logs…") { ToolTipText = "Test one parser against a log file" };
        batchTest.Click += (_, _) => Guard(OpenBatch);
        files.Items.Add(batchTest);
        return files;
    }
}
