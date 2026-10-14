using ParserCore;
using System.Text.RegularExpressions;

namespace ParserDesigner;

/// <summary>Reviews generated token declarations, types and formats before applying them to properties.</summary>
internal sealed class TokenDialog : Form
{
    public string ResultSource
    {
        get; private set;
    }
    public TokenDialog(string source, string pattern) : this(source, pattern, "") { }

    public TokenDialog(string source, string pattern, string sample)
        : this(source, pattern, sample, RegexOptions.None, false, false, 250) { }

    public TokenDialog(string source, string pattern, string sample, RegexOptions options, bool fullEvent, bool trimInput, int timeoutMs)
    {
        ResultSource = source;
        var tokens = TokenGenerator.Draft(source, pattern);
        Text = "Tokeny z regexu";
        Size = new Size(1200, 700);
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
        table.Columns.Add("Snippet", "Ukázka z Message");
        table.Columns[4].ReadOnly = true;
        table.Columns[4].FillWeight = 220;
        table.Columns[3].FillWeight = 200;
        table.Columns[0].ReadOnly = true;
        foreach (DataGridViewColumn column in table.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        foreach (var token in tokens)
            table.Rows.Add($"{token.Index + 1} → token[{token.Index}]", token.Name, token.Type, token.Format, "Načítám…");
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
            Height = 180,
            Padding = new Padding(14),
            Text = $"Nalezeno {tokens.Count} zachytávajících skupin. Skupina 0 ani (?:…) nevytváří token.\n" +
            "Typ vyberte z nabídky. Date / Time / TimeStamp / RegexToken nabídnou výchozí formát, pokud je pole prázdné.\n" +
            "Návrh formátu… otevře šablony a skládání formátu. Existující formát se při změně typu zachová; zkontrolujte jej.\n" +
            "Použít zapíše i aktuální regex. Nepoužité tokeny se odstraní; event mapping a odkazy v submessages se nepřejmenovávají.\n" +
            "Číslování vychází z .NET. Pojmenované skupiny mohou být v Java / ArcSight číslované jinak.\n" +
            "Ukázka: surový text tokenu z první shody aktuálního Message, poslední záchyt opakované skupiny. Bez typových převodů."
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
        table.Columns[4].ToolTipText = "Text zachycený pro token v první shodě Message. Nájezd na buňku zobrazí delší ukázku. Nezapisuje se do konfigurace.";
        var previewCancellation = new CancellationTokenSource();
        FormClosed += (_, _) => { previewCancellation.Cancel(); previewCancellation.Dispose(); };
        Shown += async (_, _) =>
        {
            var cancellation = previewCancellation.Token;
            try
            {
                var preview = await Task.Run(() => RegexDebugger.Analyze(pattern, sample, options, fullEvent, trimInput, timeoutMs, cancellation), cancellation);
                if (IsDisposed || cancellation.IsCancellationRequested)
                    return;
                foreach (var token in tokens)
                {
                    var capture = preview.Rows.LastOrDefault(r => r.Match == 1 && r.Group == token.Index + 1 && r.Success);
                    var cell = table.Rows[token.Index].Cells[4];
                    string text = capture?.Value ?? "";
                    string display = text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
                    cell.Value = capture is null ? "— bez záchytu" : text.Length == 0 ? "(prázdný záchyt)" : display.Length > 120 ? display[..120] + "…" : display;
                    cell.ToolTipText = capture is null ? preview.Status : $"Pozice {capture.Index}, délka {capture.Length}\n" + (text.Length > 2000 ? text[..2000] + "…" : text);
                }
                if (preview.Error is not null || preview.Matches.Count == 0)
                    info.Text += "\nNáhled: " + preview.Status;
            }
            catch (OperationCanceledException) { }
        };
        HelpUi.Describe(table, "tokens", "Každý řádek odpovídá skupině regexu. Upravte název, vyberte typ a případný formát; hlavičky sloupců vysvětlují význam.");
    }
}
