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
        Text = "Tokens from regex";
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
        foreach (string column in new[] { "Group → token", "Name" })
            table.Columns.Add(column, column);
        var types = new DataGridViewComboBoxColumn { Name = "Type", HeaderText = "Type", FlatStyle = FlatStyle.Flat };
        types.Items.AddRange(TokenFormats.Types.Concat(tokens.Select(t => t.Type)).Distinct(StringComparer.Ordinal).Cast<object>().ToArray());
        table.Columns.Add(types);
        table.Columns.Add("Format", "Format");
        table.Columns.Add("Snippet", "Message snippet");
        table.Columns[4].ReadOnly = true;
        table.Columns[4].FillWeight = 220;
        table.Columns[3].FillWeight = 200;
        table.Columns[0].ReadOnly = true;
        foreach (DataGridViewColumn column in table.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        foreach (var token in tokens)
            table.Rows.Add($"{token.Index + 1} → token[{token.Index}]", token.Name, token.Type, token.Format, "Loading…");
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
            Text = $"Found {tokens.Count} capturing groups. Group 0 and (?:…) do not create tokens.\n" +
            "Select a type from the list. Date / Time / TimeStamp / RegexToken suggest a default format when the field is empty.\n" +
            "Format builder… opens templates and format symbols. Existing formats are preserved when changing type; check them.\n" +
            "Apply also writes the current regex. Unused tokens are removed; event mapping and submessage references are not renamed.\n" +
            "Numbering follows .NET. Named groups may be numbered differently in Java / ArcSight.\n" +
            "Snippet: raw token text from the first match in the current Message; the last capture for a repeated group. No type conversion."
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(8) };
        var apply = new Button { Text = "Apply to configuration", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var design = new Button { Text = "Format builder…", AutoSize = true, Enabled = tokens.Count > 0 };
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
                if (removed.Length > 0 && MessageBox.Show(this, "These token names will no longer be declared:\n" +
                    string.Join(", ", removed.Take(30)) + "\n\nMapping and submessage references will remain unchanged. Continue?",
                    "Token changes", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
                ResultSource = updated;
                DialogResult = DialogResult.OK;
            }
            catch (FormatException ex) { MessageBox.Show(this, ex.Message, "Tokens", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        buttons.Controls.AddRange([apply, design, cancel]);
        Controls.Add(table);
        Controls.Add(info);
        Controls.Add(buttons);
        CancelButton = cancel;
        Theme.Apply(this);
        table.Columns[4].ToolTipText = "Text captured for the token in the first Message match. Hover over the cell for a longer snippet. Not written to the configuration.";
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
                    cell.Value = capture is null ? "— no capture" : text.Length == 0 ? "(empty capture)" : display.Length > 120 ? display[..120] + "…" : display;
                    cell.ToolTipText = capture is null ? preview.Status : $"Position {capture.Index}, length {capture.Length}\n" + (text.Length > 2000 ? text[..2000] + "…" : text);
                }
                if (preview.Error is not null || preview.Matches.Count == 0)
                    info.Text += "\nPreview: " + preview.Status;
            }
            catch (OperationCanceledException) { }
        };
        HelpUi.Describe(table, "tokens", "Each row corresponds to a regex group. Edit the name, choose a type and optional format; column headers explain their meaning.");
    }
}
