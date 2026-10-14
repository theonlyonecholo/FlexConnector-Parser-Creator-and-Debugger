using ParserCore;

namespace ParserDesigner;

/// <summary>Edits severity rules and linked processor declarations as a cancellable source proposal.</summary>
internal sealed class AdvancedDialog : Form
{
    private readonly string original;
    private readonly UndoTextBox deviceSeverity = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, TextBox> rules = [];
    private readonly UndoTextBox processors = new()
    {
        Multiline = true,
        AcceptsTab = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 10),
        MaxLength = 0
    };
    private readonly ComboBox type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    public string ResultSource
    {
        get; private set;
    }

    public AdvancedDialog(string source)
    {
        original = ResultSource = source;
        Text = "Severity mapping / Extra processors";
        Size = new Size(900, 550);
        MinimumSize = new Size(800, 500);
        StartPosition = FormStartPosition.CenterParent;
        var doc = new PropertiesDocument(source);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var severityPage = new TabPage("Severity mapping");
        var extraPage = new TabPage("Extra processors");
        tabs.TabPages.AddRange([severityPage, extraPage]);
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        form.ColumnStyles.Add(new(SizeType.Absolute, 170));
        form.ColumnStyles.Add(new(SizeType.Percent, 100));
        void Field(string label, Control control)
        {
            int row = form.RowCount++;
            form.RowStyles.Add(new(SizeType.Absolute, 42));
            form.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            form.Controls.Add(control, 1, row);
        }
        deviceSeverity.Text = doc.Get("event.deviceSeverity");
        Field("event.deviceSeverity", deviceSeverity);
        foreach (string level in AdvancedConfiguration.SeverityLevels)
        {
            var edit = new UndoTextBox { Dock = DockStyle.Fill, Text = doc.Get(AdvancedConfiguration.SeverityKey(level)) };
            edit.ResetHistory();
            rules[level] = edit;
            Field(level, edit);
        }
        var help = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 100,
            Padding = new Padding(12),
            Text = "Hodnoty: OPEN,CLOSE nebo 400..599. Prázdné pravidlo se odstraní.\nZadávejte dekódované hodnoty. Pro náhled severity použijte v event.deviceSeverity název tokenu.\nPřekrývající se pravidla se v náhledu označí jako nejednoznačná."
        };
        severityPage.Controls.Add(form);
        severityPage.Controls.Add(help);
        foreach (var kv in doc.Values.Where(kv => AdvancedConfiguration.IsProcessorKey(kv.Key)))
            processors.AppendText(PropertiesDocument.Escape(kv.Key) + "=" + PropertiesDocument.Escape(kv.Value) + Environment.NewLine);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(5) };
        type.Items.AddRange(AdvancedConfiguration.ProcessorTypes);
        type.SelectedItem = "regex";
        tools.Controls.Add(type);
        var add = new Button { Text = "Přidat šablonu", AutoSize = true };
        add.Click += (_, _) => Guard(() =>
        {
            int index = AdvancedConfiguration.ValidateProcessors(new(processors.Text));
            processors.AppendText(AdvancedConfiguration.ProcessorTemplate(type.Text, index));
        });
        tools.Controls.Add(add);
        tools.SetFlowBreak(add, true);
        tools.Controls.Add(new Label { AutoSize = true, Text = "Indexy 0,1,… bez mezer. Počet se dopočítá. Navázané parsery se lokálně nespouštějí." });
        extraPage.Controls.Add(processors);
        extraPage.Controls.Add(tools);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(5) };
        var apply = new Button { Text = "Použít v konfiguraci", AutoSize = true };
        apply.Click += (_, _) => Guard(() =>
        {
            ResultSource = AdvancedConfiguration.Apply(original, deviceSeverity.Text,
                rules.ToDictionary(kv => kv.Key, kv => kv.Value.Text), processors.Text);
            DialogResult = DialogResult.OK;
        });
        var cancel = new Button { Text = "Zrušit", DialogResult = DialogResult.Cancel, AutoSize = true };
        bottom.Controls.AddRange([apply, cancel]);
        CancelButton = cancel;
        Controls.Add(tabs);
        Controls.Add(bottom);
        Theme.Apply(this);
        HelpUi.Describe(deviceSeverity, "severity", "Zdroj původní závažnosti: přímý název tokenu, například Status. ArcSight výrazy náhled nevykonává.");
        foreach (var rule in rules)
            HelpUi.Describe(rule.Value, "severity", "Hodnoty pro " + rule.Key + ": seznam OPEN,CLOSE nebo rozsah 400..599 včetně hranic. Prázdné pravidlo se odstraní.");
        HelpUi.Describe(type, "processors", "Typ navazujícího parseru nebo mapování, pro které tlačítko Přidat šablonu vytvoří blok konfigurace.");
        HelpUi.Describe(processors, "processors", "Bloky extraprocessor[n].*: type, filename, vstupní field a další parametry. F1 vysvětluje každý parametr. Soubory se lokálně nespouštějí.");
        deviceSeverity.ResetHistory();
        processors.ResetHistory();
    }

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Konfigurace", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
