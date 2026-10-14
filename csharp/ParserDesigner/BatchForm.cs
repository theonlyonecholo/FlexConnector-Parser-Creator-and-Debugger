using ParserCore;

namespace ParserDesigner;

/// <summary>Runs one immutable parser snapshot against many logs and loads selected details asynchronously.</summary>
internal sealed class BatchForm : Form
{
    private ParserEvaluator evaluator;
    public string ConfigurationSource => evaluator.Source;
    private readonly Button editSubmessages = new() { Text = "Edit submessage…", AutoSize = true };
    private readonly Button editParser = new() { Text = "Edit parser…", AutoSize = true };
    private readonly List<LogEntry> logs = [];
    private readonly List<int> visible = [];
    private ParserResult?[] results = [];
    private readonly DataGridView list = Grid();
    private readonly DataGridView captures = Grid();
    private readonly RichTextBox message = new() { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, WordWrap = false, Font = new Font("Consolas", 11) };
    private readonly TextBox description = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label summary = new() { Dock = DockStyle.Fill, Padding = new Padding(10), Text = "Open a log file. One line = one event, including empty lines." };
    private readonly ComboBox filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly ComboBox encoding = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly Button open = new() { Text = "Open logs…", AutoSize = true };
    private readonly Button run = new() { Text = "Test all", AutoSize = true };
    private readonly ProgressBar progress = new() { Width = 150, Height = 26 };
    private readonly System.Windows.Forms.Timer selectionTimer = new() { Interval = 120 };
    private readonly SemaphoreSlim detailWorker = new(1);
    private CancellationTokenSource? batchCancellation, detailCancellation;
    private ParserResult? selectedResult;
    private bool closing, refreshing;
    private int detailVersion;

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };

    public BatchForm(string source, string parserName)
    {
        evaluator = new(source);
        Text = "Log file test — " + parserName;
        Size = new Size(1440, 900);
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(14) };
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Absolute, 58));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        encoding.Items.AddRange(["UTF-8", "Latin-1"]);
        encoding.SelectedIndex = 0;
        filter.Items.AddRange(["All logs", "Matches only", "Problems only", "Untested"]);
        filter.SelectedIndex = 0;
        tools.Controls.AddRange([open, encoding, run]);
        var stop = new Button { Text = "Stop", AutoSize = true };
        stop.Click += (_, _) => batchCancellation?.Cancel();
        var previous = new Button { Text = "← Log", AutoSize = true };
        var next = new Button { Text = "Log →", AutoSize = true };
        previous.Click += (_, _) => Navigate(-1);
        next.Click += (_, _) => Navigate(1);
        tools.Controls.AddRange([stop, previous, next, filter, progress]);
        tools.SetFlowBreak(progress, true);
        tools.Controls.AddRange([editSubmessages, editParser, new Label { Text = "Changes return to the main configuration; use Save to write them to disk.", AutoSize = true, Padding = new Padding(6) }]);
        editSubmessages.Click += async (_, _) => await EditSubmessagesAsync();
        editParser.Click += async (_, _) => await EditParserAsync();
        root.Controls.Add(tools, 0, 0);
        root.Controls.Add(summary, 0, 1);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 8 };
        list.VirtualMode = true;
        foreach (string column in new[] { "Line", "Result", "Message ID", "Submessage / pattern", "Log" })
            list.Columns.Add(column, column);
        list.Columns[0].FillWeight = 40;
        list.Columns[3].FillWeight = 150;
        list.Columns[4].FillWeight = 170;
        foreach (DataGridViewColumn column in list.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        list.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex >= visible.Count)
                return;
            int i = visible[e.RowIndex];
            var result = results[i];
            e.Value = e.ColumnIndex switch
            {
                0 => logs[i].LineNumber,
                1 => StateLabel(result),
                2 => result?.MessageId ?? "",
                3 => result?.Route ?? "—",
                _ => logs[i].Text.Length > 200 ? logs[i].Text[..200] + "…" : logs[i].Text
            };
        };
        list.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex != 1 || e.RowIndex < 0 || e.RowIndex >= visible.Count)
                return;
            var result = results[visible[e.RowIndex]];
            e.CellStyle!.ForeColor = result is null ? Color.SlateGray : result.Success ? Color.SeaGreen : Color.Firebrick;
        };
        list.SelectionChanged += (_, _) => { if (!refreshing) { selectionTimer.Stop(); selectionTimer.Start(); } };
        selectionTimer.Tick += (_, _) => { selectionTimer.Stop(); _ = SelectLogAsync(); };
        split.Panel1.Controls.Add(list);
        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(8, 0, 0, 0) };
        detail.RowStyles.Add(new(SizeType.Absolute, 30));
        detail.RowStyles.Add(new(SizeType.Percent, 35));
        detail.RowStyles.Add(new(SizeType.Percent, 35));
        detail.RowStyles.Add(new(SizeType.Percent, 30));
        detail.ColumnStyles.Add(new(SizeType.Percent, 100));
        detail.Controls.Add(new Label { Text = "LOG DETAILS  ·  token and field colors match the table below", Dock = DockStyle.Fill }, 0, 0);
        detail.Controls.Add(message, 0, 1);
        foreach (string column in new[] { "Stage", "Token / field", "Position", "Length", "Value" })
            captures.Columns.Add(column, column);
        foreach (DataGridViewColumn column in captures.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        captures.SelectionChanged += (_, _) => PaintSelection();
        detail.Controls.Add(captures, 0, 2);
        detail.Controls.Add(description, 0, 3);
        split.Panel2.Controls.Add(detail);
        root.Controls.Add(split, 0, 2);
        Controls.Add(root);
        Theme.Apply(this);
        HelpUi.Describe(encoding, "batch", "Log file encoding for the next load. Choose UTF-8 or Latin-1 to match the source.");
        HelpUi.Describe(filter, "batch", "Show all logs, matches, problems or untested entries; the arrows navigate this selection.");
        HelpUi.Describe(list, "batch", "Each row is one log. Click to see captures, the message ID and the selected submessage/pattern.");
        HelpUi.Describe(message, "batch", "The original selected log; colors match tokens and fields in the table.");
        HelpUi.Describe(captures, "batch", "Captured token and field ranges. Click a row to highlight it.");
        HelpUi.Describe(description, "batch", "The result explanation and the order of attempted patterns. A regex match does not validate ArcSight transformations.");
        HelpUi.Describe(progress, "batch", "Number of completed tests; stopping leaves remaining rows untested.");
        Controls.Add(Theme.Header("LOG WORKBENCH", parserName + "  ·  Browse logs, edit the parser and compare results"));
        open.Click += async (_, _) => await LoadAsync();
        run.Click += async (_, _) => await RunAsync();
        filter.SelectedIndexChanged += (_, _) => RefreshList();
        Shown += (_, _) => split.SplitterDistance = split.Width / 2;
        FormClosing += (_, _) => { closing = true; batchCancellation?.Cancel(); detailCancellation?.Cancel(); selectionTimer.Stop(); };
        FormClosed += (_, _) => selectionTimer.Dispose();
    }
    private static string StateLabel(ParserResult? r) => r?.State switch
    {
        ParseState.Matched => "Match",
        ParseState.MainMismatch => "Main regex: mismatch",
        ParseState.NoSubmessage => "Missing submessage",
        ParseState.SubmessageMismatch => "Submessage: mismatch",
        ParseState.Error => "Error / timeout",
        _ => "Not tested"
    };
    private async Task LoadAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Log / text|*.log;*.txt|All files|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        open.Enabled = run.Enabled = editSubmessages.Enabled = editParser.Enabled = false;
        bool latin = encoding.SelectedIndex == 1;
        try
        {
            var loaded = await Task.Run(() => LogFile.Read(dialog.FileName, latin));
            if (closing)
                return;
            detailVersion++;
            detailCancellation?.Cancel();
            selectionTimer.Stop();
            logs.Clear();
            logs.AddRange(loaded);
            results = new ParserResult?[logs.Count];
            Text = "Log test — " + Path.GetFileName(dialog.FileName);
            RefreshList();
            await RunAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.DecoderFallbackException)
        {
            if (!closing)
                MessageBox.Show(this, ex.Message, "Load logs");
        }
        finally { if (!closing) open.Enabled = run.Enabled = editSubmessages.Enabled = editParser.Enabled = true; }
    }
    private async Task RunAsync()
    {
        if (logs.Count == 0)
            return;
        int preferred = SelectedLogIndex();
        var snapshot = evaluator;
        batchCancellation?.Dispose();
        batchCancellation = new();
        var cancellation = batchCancellation.Token;
        open.Enabled = run.Enabled = editSubmessages.Enabled = editParser.Enabled = false;
        results = new ParserResult?[logs.Count];
        RefreshList(preferred);
        progress.Maximum = logs.Count;
        progress.Value = 0;
        var updates = new Progress<int>(n => { if (!closing && !run.Enabled) { progress.Value = n; summary.Text = $"Testing {n:N0} / {logs.Count:N0} logs…"; } });
        try
        {
            var output = await Task.Run(() =>
            {
                var evaluated = new ParserResult?[logs.Count];
                for (int i = 0; i < logs.Count; i++)
                {
                    try
                    {
                        cancellation.ThrowIfCancellationRequested();
                        evaluated[i] = snapshot.Evaluate(logs[i].Text, false, cancellation);
                    }
                    catch (OperationCanceledException) { break; }
                    if ((i + 1) % 50 == 0)
                        ((IProgress<int>)updates).Report(i + 1);
                }
                return evaluated;
            });
            if (closing)
                return;
            results = output;
            progress.Value = results.Count(r => r is not null);
            RefreshList(preferred);
            selectionTimer.Stop();
            await SelectLogAsync();
        }
        finally { if (!closing) { open.Enabled = run.Enabled = editSubmessages.Enabled = editParser.Enabled = true; UpdateSummary(); } }
    }
    private void UpdateSummary()
    {
        int tested = results.Count(r => r is not null), matched = results.Count(r => r?.Success == true);
        summary.Text = $"{logs.Count:N0} LOGS    ·    {matched:N0} MATCHES    ·    {tested - matched:N0} PROBLEMS    ·    {logs.Count - tested:N0} UNTESTED\n" +
            "One line = one event. Results use the main regex and submessages from the configuration, with the .NET engine.";
    }
    private int SelectedLogIndex() => list.CurrentCell is { RowIndex: >= 0 } cell && cell.RowIndex < visible.Count ? visible[cell.RowIndex] : -1;
    private void RefreshList(int preferred = -1)
    {
        if (preferred < 0)
            preferred = SelectedLogIndex();
        detailVersion++;
        detailCancellation?.Cancel();
        selectionTimer.Stop();
        refreshing = true;
        selectedResult = null;
        list.RowCount = 0;
        visible.Clear();
        for (int i = 0; i < logs.Count; i++)
            if (filter.SelectedIndex switch
            {
                1 => results[i]?.Success == true,
                2 => results[i] is { Success: false },
                3 => results[i] is null,
                _ => true
            })
                visible.Add(i);
        list.RowCount = visible.Count;
        refreshing = false;
        list.Invalidate();
        UpdateSummary();
        if (visible.Count > 0)
        {
            int row = Math.Max(0, visible.IndexOf(preferred));
            list.CurrentCell = list.Rows[row].Cells[0];
            selectionTimer.Start();
        }
        else
        {
            detailVersion++;
            detailCancellation?.Cancel();
            message.Clear();
            captures.Rows.Clear();
            description.Clear();
            selectedResult = null;
        }
    }
    private void Navigate(int direction)
    {
        if (visible.Count == 0)
            return;
        int index = Math.Clamp((list.CurrentCell?.RowIndex ?? 0) + direction, 0, visible.Count - 1);
        list.CurrentCell = list.Rows[index].Cells[0];
    }
    private async Task SelectLogAsync()
    {
        int row = list.CurrentCell?.RowIndex ?? -1;
        if (row < 0 || row >= visible.Count)
            return;
        int index = visible[row];
        int version = ++detailVersion;
        detailCancellation?.Cancel();
        detailCancellation?.Dispose();
        detailCancellation = new();
        var token = detailCancellation.Token;
        message.Text = logs[index].Text;
        captures.Rows.Clear();
        selectedResult = null;
        description.Text = "Evaluating the selected log…";
        try
        {
            await detailWorker.WaitAsync(token);
            ParserResult detail;
            string text = logs[index].Text;
            var snapshot = evaluator;
            try
            {
                detail = await Task.Run(() => snapshot.Evaluate(text, true, token), token);
            }
            finally { detailWorker.Release(); }
            if (closing || version != detailVersion || token.IsCancellationRequested)
                return;
            selectedResult = detail;
            foreach (var c in detail.Captures)
            {
                int n = captures.Rows.Add(c.Stage, c.Name, c.Index, c.Length, c.Value);
                captures.Rows[n].Tag = c;
                if (c.Stage == "Token" || (c.Stage == "Submessage" && c.Name != "Entire match"))
                    captures.Rows[n].Cells[1].Style.BackColor = CaptureColor(c);
            }
            captures.ClearSelection();
            description.Text = $"Line {logs[index].LineNumber} — {StateLabel(detail)}\r\n{detail.Description}\r\nMessage ID: {detail.MessageId}\r\nSelected / available route: {detail.Route}\r\n\r\n" +
                string.Join("\r\n", detail.Attempts.Select(a => $"submessage[{a.Submessage}].pattern[{a.Pattern}]: {(a.Matched ? "MATCH" : "mismatch")}")) +
                "\r\n\r\nSubmessage fields are group labels; ArcSight functions and extra processors are not executed.";
            PaintSelection();
        }
        catch (OperationCanceledException) { }
    }
    private async Task ApplyParserAsync(string source)
    {
        var updated = new ParserEvaluator(source);
        detailVersion++;
        detailCancellation?.Cancel();
        selectionTimer.Stop();
        evaluator = updated;
        await RunAsync();
    }
    private async Task EditSubmessagesAsync()
    {
        open.Enabled = run.Enabled = editSubmessages.Enabled = editParser.Enabled = false;
        try
        {
            int index = SelectedLogIndex();
            string log = index >= 0 ? logs[index].Text : "";
            var snapshot = evaluator;
            // Resolve from this log rather than a potentially older asynchronous detail.
            ParserResult? currentResult = index >= 0 ? await Task.Run(() => snapshot.Evaluate(log)) : null;
            if (closing)
                return;
            int sub = currentResult?.UsedSubmessage ?? currentResult?.Attempts.FirstOrDefault()?.Submessage ?? -1;
            int pattern = currentResult?.UsedPattern ?? currentResult?.Attempts.FirstOrDefault()?.Pattern ?? -1;
            using var dialog = new SubmessageDialog(ConfigurationSource, log, sub, pattern);
            if (dialog.ShowDialog(this) == DialogResult.OK)
                await ApplyParserAsync(dialog.ResultSource);
        }
        catch (FormatException ex) { if (!closing) MessageBox.Show(this, ex.Message, "Submessages"); }
        finally { if (!closing) open.Enabled = run.Enabled = editSubmessages.Enabled = editParser.Enabled = true; }
    }
    private async Task EditParserAsync()
    {
        using var dialog = new ParserSourceDialog(ConfigurationSource);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            await ApplyParserAsync(dialog.ResultSource);
    }
    private void PaintSelection()
    {
        int start = message.SelectionStart;
        message.SelectAll();
        message.SelectionBackColor = Color.White;
        if (selectedResult is not null)
        {
            foreach (var c in selectedResult.Captures.Where(c => c.Name == "Entire match"))
                ColorSpan(c, c.Stage == "Submessage" ? Color.FromArgb(187, 247, 208) : Color.FromArgb(254, 240, 138));
            foreach (var c in selectedResult.Captures.Where(c => c.Stage == "Token").OrderByDescending(c => c.Length))
                ColorSpan(c, CaptureColor(c));
            foreach (var c in selectedResult.Captures.Where(c => c.Stage == "Submessage" && c.Name != "Entire match").OrderByDescending(c => c.Length))
                ColorSpan(c, CaptureColor(c));
            if (captures.SelectedRows.Count > 0 && captures.SelectedRows[0].Tag is ParserCapture active)
                ColorSpan(active, Color.FromArgb(147, 197, 253));
        }
        message.Select(Math.Min(start, message.TextLength), 0);
    }
    private Color CaptureColor(ParserCapture capture)
    {
        if (selectedResult is null)
            return Color.White;
        var keys = selectedResult.Captures.Where(c => c.Stage == "Token" || (c.Stage == "Submessage" && c.Name != "Entire match"))
            .Select(c => (c.Stage, c.Name)).Distinct().ToList();
        return TokenColors.For(keys.IndexOf((capture.Stage, capture.Name)));
    }
    private void ColorSpan(ParserCapture capture, Color color)
    {
        if (capture.Index < 0 || capture.Index + capture.Length > message.TextLength)
            return;
        message.Select(capture.Index, capture.Length);
        message.SelectionBackColor = color;
    }
}
