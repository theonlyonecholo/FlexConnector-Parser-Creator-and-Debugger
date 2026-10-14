using ParserCore;

namespace ParserDesigner;

/// <summary>Owns the editing session, shared controls and UI event wiring.</summary>
/// <remarks>Document actions, analysis, highlighting and toolbar creation live in separate partial files.</remarks>
internal sealed partial class MainForm : Form
{
    private readonly UndoRichTextBox message = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 11), DetectUrls = false, HideSelection = false, WordWrap = false };
    private readonly TextBox pattern = Editor();
    private readonly TextBox source = Editor();
    private readonly TextBox report = Editor();
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox automatic = new() { Text = "Automatic", Checked = true, AutoSize = true };
    private readonly CheckBox ignoreCase = new() { Text = "IgnoreCase (i)", AutoSize = true };
    private readonly CheckBox multiline = new() { Text = "Multiline (m)", AutoSize = true };
    private readonly CheckBox singleline = new() { Text = "Singleline (s)", AutoSize = true };
    private readonly CheckBox full = new() { Text = "Entire message", AutoSize = true };
    private readonly CheckBox trim = new() { Text = "Trim spaces/tabs", AutoSize = true };
    private readonly NumericUpDown timeout = new() { Minimum = 10, Maximum = 2000, Value = 250, Increment = 50, Width = 65 };
    private readonly DataGridView grid = new()
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
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 250 };
    private readonly SemaphoreSlim worker = new(1, 1);
    private CancellationTokenSource? pending;
    private DebugResult? result;
    private string analyzedText = "";
    private IReadOnlyList<RegexGroupRegion> groupRegions = [];
    private int? activeRegexGroup;
    private string? path;
    private bool dirty, regexDirty, loading, coloring, populating;
    private int revision;

    private static TextBox Editor() => new UndoTextBox()
    {
        Multiline = true,
        AcceptsTab = true,
        AcceptsReturn = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Both,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 10),
        MaxLength = 0
    };

    public MainForm()
    {
        pattern.WordWrap = true;
        pattern.ScrollBars = ScrollBars.Vertical;
        Text = "ArcSight Parser Designer — C#";
        Size = new Size(1380, 980);
        MinimumSize = new Size(960, 750);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(10) };
        foreach (int height in new[] { 38, 24, 120, 24, 96, 40, 40, 32 })
            layout.RowStyles.Add(new(SizeType.Absolute, height));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(layout);
        layout.Controls.Add(CreateToolbar(), 0, 0);

        layout.Controls.Add(new Label { Text = "Message — tokens have distinct colors; see the Debug table legend; selected captures are blue", Dock = DockStyle.Fill }, 0, 1);
        layout.Controls.Add(message, 0, 2);
        layout.Controls.Add(new Label { Text = @"Regex — click inside a group to highlight its captures in Message (or move the cursor with the keyboard)", Dock = DockStyle.Fill }, 0, 3);
        layout.Controls.Add(pattern, 0, 4);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        layout.RowStyles[5].SizeType = SizeType.AutoSize;
        options.Controls.AddRange([automatic, ignoreCase, multiline, singleline, full, trim,
            new Label { Text = "Timeout ms:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) }, timeout]);
        layout.Controls.Add(options, 0, 5);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        layout.RowStyles[6].SizeType = SizeType.AutoSize;
        AddButton(actions, "Test / Debug (F5)", () => { tabs.SelectedIndex = 1; _ = AnalyzeAsync(); });
        AddButton(actions, "Stop", Stop);
        AddButton(actions, "◀ Previous capture", () => Step(-1));
        AddButton(actions, "Next capture ▶", () => Step(1));
        layout.Controls.Add(actions, 0, 6);
        layout.Controls.Add(status, 0, 7);
        var configTab = new TabPage("Configuration .properties");
        configTab.Controls.Add(source);
        var debugTab = new TabPage("Regex debug");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(grid);
        report.ReadOnly = true;
        split.Panel2.Controls.Add(report);
        debugTab.Controls.Add(split);
        tabs.TabPages.AddRange([configTab, debugTab]);
        layout.Controls.Add(tabs, 0, 8);
        foreach (string name in new[] { "Match", "Group", "Name / token", "Capture", "Success", "Position (0-based)", "Length", "Value" })
            grid.Columns.Add(name, name);
        grid.Columns[7].FillWeight = 240;
        foreach (DataGridViewColumn column in grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        grid.SelectionChanged += (_, _) => { if (!populating) HighlightSelected(); };
        source.TextChanged += (_, _) => { if (!loading) { dirty = true; UpdateTitle(); Schedule(); } };
        pattern.TextChanged += (_, _) => { if (!loading) { regexDirty = true; UpdateTitle(); Schedule(); } };
        pattern.MouseUp += (_, _) => HighlightRegexGroup();
        pattern.KeyUp += (_, _) => HighlightRegexGroup();
        pattern.Enter += (_, _) => HighlightRegexGroup();
        message.TextChanged += (_, _) => { if (!coloring && !loading) Schedule(); };
        foreach (var option in new[] { automatic, ignoreCase, multiline, singleline, full, trim })
            option.CheckedChanged += (_, _) => Schedule();
        timeout.ValueChanged += (_, _) => Schedule();
        debounce.Tick += (_, _) => { debounce.Stop(); _ = AnalyzeAsync(); };
        FormClosing += (_, e) => { if (!CanLeave()) e.Cancel = true; else { debounce.Stop(); pending?.Cancel(); } };
        FormClosed += (_, _) => { debounce.Dispose(); pending?.Dispose(); };
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                tabs.SelectedIndex = 1;
                _ = AnalyzeAsync();
            }
            else if (e.Control && e.KeyCode == Keys.S)
            {
                e.SuppressKeyPress = true;
                Guard(() => SaveFile(false));
            }
        };
        loading = true;
        source.Text = "# FlexConnector Regex Configuration File\r\nregex=status=(\\\\d+) message=(.*)\r\ntoken.count=2\r\ntoken[0].name=Status\r\ntoken[0].type=String\r\ntoken[1].name=Message\r\ntoken[1].type=String\r\nevent.deviceSeverity=Status\r\nevent.message=Message\r\nseverity.map.high.if.deviceSeverity=400..599\r\n";
        pattern.Text = new PropertiesDocument(source.Text).Get("regex");
        message.Text = "status=404 message=Resource missing";
        loading = false;
        ((UndoTextBox)source).ResetHistory();
        ((UndoTextBox)pattern).ResetHistory();
        message.ResetHistory();
        Theme.Apply(this);
        HelpUi.Describe(message, "regex", "Original log for testing. Pastel colors distinguish tokens; blue marks the selected group or capture.");
        HelpUi.Describe(pattern, "regex", "Decoded regex: enter, for example, \\d. Click inside a group to see its text; long expressions wrap visually.");
        HelpUi.Describe(source, "properties", "The full parser configuration. Backslashes are doubled in properties. Apply changes this text; Save writes it to disk.");
        HelpUi.Describe(report, "limits", "Diagnostics: regex errors, preview limits, token consistency and severity preview.");
        HelpUi.Describe(grid, "regex", "Regex matches and captures. Click a row to highlight its range; column headers have their own descriptions.");
        HelpUi.Describe(automatic, "regex", "Update the preview after a typing pause. When disabled, press F5.");
        HelpUi.Describe(ignoreCase, "regex", "Ignore letter case in the preview only (flag i).");
        HelpUi.Describe(multiline, "regex", "Flag m: ^ and $ work with individual lines. This does not combine log lines.");
        HelpUi.Describe(singleline, "regex", "Flag s: the dot also matches a newline.");
        HelpUi.Describe(full, "regex", "Require the entire message to match; otherwise the preview also finds partial matches.");
        HelpUi.Describe(trim, "regex", "Remove leading and trailing spaces and tabs in the preview. Positions remain relative to the original text.");
        HelpUi.Describe(timeout, "limits", "Time limit per regex operation in milliseconds. On timeout, simplify the expression or adjust the limit.");
        Controls.Add(Theme.Header("PARSER STUDIO", "ArcSight FlexConnector  /  Configuration editor and regex debugger"));
        Shown += (_, _) => { split.SplitterDistance = Math.Max(100, split.Height * 2 / 3); Schedule(); };
    }

}
