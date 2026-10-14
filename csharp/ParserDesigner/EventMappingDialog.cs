using ParserCore;

namespace ParserDesigner;

/// <summary>Edits direct event assignments and presents advisory reference and type diagnostics.</summary>
internal sealed class EventMappingDialog : Form
{
    private readonly DataGridView table = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly ComboBox target = new() { Width = 270, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
    private readonly ComboBox mode = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox value = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox diagnostics = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label fieldInfo = new() { Dock = DockStyle.Fill };
    private readonly string original;
    public string ResultSource
    {
        get; private set;
    }

    public EventMappingDialog(string source)
    {
        original = ResultSource = source;
        Text = "Event mapping — přiřazení tokenů do události";
        Size = new Size(1150, 780);
        MinimumSize = new Size(950, 650);
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 74, 82, 44 })
            root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 115));
        root.RowStyles.Add(new(SizeType.Absolute, 48));
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Vyberte cílové event pole a token, textovou konstantu nebo ArcSight výraz. Přidat / aktualizovat zapíše návrh do tabulky.\nExistující mapování lze upravit přímo v tabulce. Použít v konfiguraci přenese tabulku do .properties; poté Uložit.\nEditor kontroluje odkazy a typy přímých tokenů; ArcSight výrazy ani výslednou událost lokálně nevyhodnocuje." }, 0, 0);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        root.RowStyles[1].SizeType = SizeType.AutoSize;
        target.Items.AddRange(EventMapping.Fields.Keys.Order().Cast<object>().ToArray());
        mode.Items.AddRange(["Token", "Konstanta", "Výraz"]);
        mode.SelectedIndex = 0;
        value.Items.AddRange(EventMapping.Tokens(source).Keys.Cast<object>().ToArray());
        void Input(string label, Control control)
        {
            var group = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 8, 8) };
            group.Controls.Add(new Label { Text = label, AutoSize = true });
            group.Controls.Add(control);
            tools.Controls.Add(group);
        }
        Input("Cílové pole", target);
        Input("Zdroj hodnoty", mode);
        Input("Token / hodnota / výraz", value);
        tools.SetFlowBreak(value.Parent!, true);
        var add = new Button { Text = "Přidat / aktualizovat", AutoSize = true };
        add.Click += (_, _) => Guard(() =>
        {
            table.EndEdit();
            string field = target.Text.Trim();
            if (!EventMapping.IsDirectField(field))
                throw new FormatException("Vyberte nebo zadejte pole event.nazevPole.");
            string expression = mode.SelectedIndex == 1 ? EventMapping.Constant(value.Text) : value.Text.Trim();
            if (expression.Length == 0)
                throw new FormatException("Vyberte token nebo zadejte výraz.");
            var row = table.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => Convert.ToString(r.Cells[0].Value) == field);
            if (row is null)
            {
                int i = table.Rows.Add(field, expression);
                row = table.Rows[i];
            }
            else
                row.Cells[1].Value = expression;
            table.CurrentCell = row.Cells[0];
            target.Text = "";
            value.Text = "";
            RefreshDiagnostics();
        });
        var remove = new Button { Text = "Odstranit mapování", AutoSize = true };
        remove.Click += (_, _) => { if (table.CurrentRow is { } row) table.Rows.Remove(row); RefreshDiagnostics(); };
        tools.Controls.AddRange([add, remove]);
        root.Controls.Add(tools, 0, 1);
        root.Controls.Add(fieldInfo, 0, 2);
        target.TextChanged += (_, _) => fieldInfo.Text = EventMapping.Fields.TryGetValue(target.Text, out var type) ? "Cílový typ: " + type + ". Token má mít stejný typ. Konstanta používá __stringConstant; pro jiný typ použijte odpovídající operaci." : "Vlastní pole lze zadat ručně. Výraz pište dekódovaný, např. __useCurrentYear(Timestamp).";
        mode.SelectedIndexChanged += (_, _) =>
        {
            value.Items.Clear();
            if (mode.SelectedIndex == 0)
                value.Items.AddRange(EventMapping.Tokens(source).Keys.Cast<object>().ToArray());
            else if (mode.SelectedIndex == 2)
                value.Items.AddRange(["__stringConstant(\"text\")", "__useCurrentYear(Timestamp)"]);
        };
        table.Columns.Add("Pole", "Cílové event pole");
        table.Columns.Add("Výraz", "Token / výraz");
        table.Columns[1].FillWeight = 180;
        foreach (DataGridViewColumn column in table.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        foreach (var assignment in EventMapping.Load(source))
            table.Rows.Add(assignment.Field, assignment.Expression);
        table.CellEndEdit += (_, _) => RefreshDiagnostics();
        root.Controls.Add(table, 0, 3);
        root.Controls.Add(diagnostics, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var apply = new Button { Text = "Použít v konfiguraci", AutoSize = true };
        apply.Click += (_, _) => Guard(() =>
        {
            table.EndEdit();
            if (target.Text.Length > 0 || value.Text.Length > 0)
                throw new FormatException("Návrh nahoře ještě není v tabulce. Použijte Přidat / aktualizovat nebo obě pole vyprázdněte.");
            ResultSource = EventMapping.Apply(original, Assignments());
            DialogResult = DialogResult.OK;
        });
        var cancel = new Button { Text = "Zrušit", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([apply, cancel]);
        root.Controls.Add(actions, 0, 5);
        Controls.Add(root);
        CancelButton = cancel;
        Theme.Apply(this);
        HelpUi.Describe(target, "mapping", "Cílové pole události. Nabídka obsahuje pole z přílohy E; vlastní pole lze napsat ručně.");
        HelpUi.Describe(mode, "mapping", "Token je přímý odkaz; Konstanta vytvoří __stringConstant; Výraz uloží zadanou ArcSight operaci.");
        HelpUi.Describe(value, "mapping", "Vyberte token nebo napište text konstanty či dekódovaný výraz. Vložte jej do tabulky tlačítkem Přidat / aktualizovat.");
        HelpUi.Describe(table, "mapping", "Přímá mapování event.*. Dvojklikem upravte pole či výraz; Odstranit mapování odstraní vybraný řádek.");
        HelpUi.Describe(diagnostics, "mapping", "Kontrola přímých odkazů, cílových typů a vybraných konfliktů; výrazy ani jejich návratové typy se nevyhodnocují.");
        table.Columns[0].ToolTipText = "Například event.sourceAddress. Typ zjistíte výběrem pole v horní nabídce.";
        table.Columns[1].ToolTipText = "Název tokenu nebo výraz jako __stringConstant(\"text\"). Zapisujte dekódovanou hodnotu.";
        fieldInfo.Text = "Vyberte cílové pole pro zobrazení očekávaného typu.";
        RefreshDiagnostics();
    }
    private EventAssignment[] Assignments() => table.Rows.Cast<DataGridViewRow>().Select(r => new EventAssignment(Convert.ToString(r.Cells[0].Value)?.Trim() ?? "", Convert.ToString(r.Cells[1].Value) ?? "")).ToArray();
    private void RefreshDiagnostics()
    {
        try
        {
            var assignments = Assignments();
            _ = EventMapping.Apply(original, assignments);
            var warnings = EventMapping.Warnings(original, assignments);
            diagnostics.Text = warnings.Count == 0 ? "Přímé odkazy a známé typy jsou bez upozornění. ArcSight výrazy ověřte v konektoru." : string.Join(Environment.NewLine, warnings);
        }
        catch (FormatException ex) { diagnostics.Text = ex.Message; }
    }
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (FormatException ex) { MessageBox.Show(this, ex.Message, "Event mapping", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
