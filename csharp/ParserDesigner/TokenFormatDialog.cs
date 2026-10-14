using ParserCore;

namespace ParserDesigner;

/// <summary>Builds decoded format strings from documented templates and editable date/time symbols.</summary>
internal sealed class TokenFormatDialog : Form
{
    private readonly UndoTextBox format = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 11) };
    public string ResultFormat => format.Text;
    public TokenFormatDialog(string type, string current)
    {
        Text = "Token format — " + type;
        Size = new Size(820, 650);
        MinimumSize = new Size(680, 560);
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 7 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 82, 36, 26, 72, 28 })
            root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 48));
        root.Controls.Add(new Label { Text = TokenFormats.Help(type), Dock = DockStyle.Fill }, 0, 0);
        var presets = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 650 };
        presets.Items.Add("Select a template…");
        presets.Items.AddRange(TokenFormats.Suggestions(type).Cast<object>().ToArray());
        presets.SelectedIndex = 0;
        presets.SelectedIndexChanged += (_, _) => { if (presets.SelectedIndex > 0) format.Text = presets.Text; };
        root.Controls.Add(presets, 0, 1);
        root.Controls.Add(new Label { Text = "Format — freely editable; enter the decoded value", Dock = DockStyle.Fill }, 0, 2);
        format.Text = current;
        format.ResetHistory();
        root.Controls.Add(format, 0, 3);
        root.Controls.Add(new Label { Text = TokenFormats.IsDateTime(type) ? "Click to insert a symbol at the cursor:" : "Source: FlexConn DevGuide, pp. 196–197. The builder does not convert values.", Dock = DockStyle.Fill }, 0, 4);
        var symbols = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        if (TokenFormats.IsDateTime(type))
            foreach (var item in TokenFormats.Symbols)
            {
                var button = new Button { Text = item.Symbol + " — " + item.Meaning, AutoSize = true };
                button.Click += (_, _) => { format.SelectedText = item.Symbol; format.Focus(); };
                symbols.Controls.Add(button);
            }
        root.Controls.Add(symbols, 0, 5);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var apply = new Button { Text = "Apply format", AutoSize = true, DialogResult = DialogResult.OK };
        var clear = new Button { Text = "Clear format", AutoSize = true };
        clear.Click += (_, _) => format.Clear();
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([apply, clear, cancel]);
        root.Controls.Add(actions, 0, 6);
        Controls.Add(root);
        CancelButton = cancel;
        Theme.Apply(this);
        HelpUi.Describe(format, "formats", "The resulting decoded token format. Edit the template; symbol buttons insert at the cursor. Ctrl+Z undoes an edit.");
        HelpUi.Describe(presets, "formats", "Selecting a template replaces the working format. Adjust it to the actual log value before applying.");
    }
}
