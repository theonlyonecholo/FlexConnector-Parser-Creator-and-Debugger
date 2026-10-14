using ParserCore;

namespace ParserDesigner;

/// <summary>Reviews generated token declarations, types and formats before applying them to properties.</summary>
internal sealed class TokenDialog : Form
{
    public string ResultSource
    {
        get; private set;
    }
    public TokenDialog(string source, string pattern)
    {
        ResultSource = source;
        var tokens = TokenGenerator.Draft(source, pattern);
        Text = "Tokeny z regexu";
        Size = new Size(1000, 650);
        MinimumSize = new Size(740, 440);
        StartPosition = FormStartPosition.CenterParent;
        var table = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        foreach (string column in new[] { "Skupina → token", "Název" })
            table.Columns.Add(column, column);
        var types = new DataGridViewComboBoxColumn { Name = "Typ", HeaderText = "Typ", FlatStyle = FlatStyle.Flat };
        types.Items.AddRange(TokenFormats.Types.Concat(tokens.Select(t => t.Type)).Distinct(StringComparer.Ordinal).Cast<object>().ToArray());
        table.Columns.Add(types);
        table.Columns.Add("Formát", "Formát");
        table.Columns[3].FillWeight = 200;
        table.Columns[0].ReadOnly = true;
        foreach (DataGridViewColumn column in table.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        foreach (var token in tokens)
            table.Rows.Add($"{token.Index + 1} → token[{token.Index}]", token.Name, token.Type, token.Format);
        table.CurrentCellDirtyStateChanged += (_, _) => { if (table.IsCurrentCellDirty && table.CurrentCell?.ColumnIndex == 2) table.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        table.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 2)
                return;
            var row = table.Rows[e.RowIndex];
            if (string.IsNullOrEmpty(Convert.ToString(row.Cells[3].Value)))
                row.Cells[3].Value = TokenFormats.Suggestions(Convert.ToString(row.Cells[2].Value) ?? "").FirstOrDefault() ?? "";
        };
        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 160,
            Padding = new Padding(14),
            Text = $"Nalezeno {tokens.Count} zachytávajících skupin. Skupina 0 ani (?:…) nevytváří token.\n" +
            "Typ vyberte z nabídky. Date / Time / TimeStamp / RegexToken nabídnou výchozí formát, pokud je pole prázdné.\n" +
            "Návrh formátu… otevře šablony a skládání formátu. Existující formát se při změně typu zachová; zkontrolujte jej.\n" +
            "Použít zapíše i aktuální regex. Nepoužité tokeny se odstraní; event mapping a odkazy v submessages se nepřejmenovávají.\n" +
            "Číslování vychází z .NET. Pojmenované skupiny mohou být v Java / ArcSight číslované jinak."
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(8) };
        var apply = new Button { Text = "Použít v konfiguraci", AutoSize = true };
        var cancel = new Button { Text = "Zrušit", AutoSize = true, DialogResult = DialogResult.Cancel };
        var design = new Button { Text = "Návrh formátu…", AutoSize = true, Enabled = tokens.Count > 0 };
        design.Click += (_, _) =>
        {
            table.EndEdit();
            if (table.CurrentCell is null)
                return;
            var row = table.Rows[table.CurrentCell.RowIndex];
            using var dialog = new TokenFormatDialog(Convert.ToString(row.Cells[2].Value) ?? "String", Convert.ToString(row.Cells[3].Value) ?? "");
            if (dialog.ShowDialog(this) == DialogResult.OK)
                row.Cells[3].Value = dialog.ResultFormat;
        };
        apply.Click += (_, _) =>
        {
            try
            {
                table.EndEdit();
                var edited = table.Rows.Cast<DataGridViewRow>().Select((row, i) => new TokenDefinition(i,
                    Convert.ToString(row.Cells[1].Value) ?? "", Convert.ToString(row.Cells[2].Value) ?? "", Convert.ToString(row.Cells[3].Value) ?? "")).ToArray();
                var doc = new PropertiesDocument(source);
                var oldNames = doc.Values.Where(kv => kv.Key.StartsWith("token[", StringComparison.Ordinal) && kv.Key.EndsWith("].name", StringComparison.Ordinal)).Select(kv => kv.Value).ToArray();
                string[] removed = oldNames.Except(edited.Select(t => t.Name), StringComparer.Ordinal).ToArray();
                string updated = TokenGenerator.Apply(source, pattern, edited);
                if (removed.Length > 0 && MessageBox.Show(this, "Tyto názvy tokenů již nebudou deklarované:\n" +
                    string.Join(", ", removed.Take(30)) + "\n\nOdkazy v mapování a submessages zůstanou beze změny. Pokračovat?",
                    "Změna tokenů", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
                ResultSource = updated;
                DialogResult = DialogResult.OK;
            }
            catch (FormatException ex) { MessageBox.Show(this, ex.Message, "Tokeny", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        buttons.Controls.AddRange([apply, design, cancel]);
        Controls.Add(table);
        Controls.Add(info);
        Controls.Add(buttons);
        CancelButton = cancel;
        Theme.Apply(this);
        HelpUi.Describe(table, "tokens", "Každý řádek odpovídá skupině regexu. Upravte název, vyberte typ a případný formát; hlavičky sloupců vysvětlují význam.");
    }
}
