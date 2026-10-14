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
        Text = "Event mapping — assign tokens to event fields";
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
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Choose a target event field and a token, text constant or ArcSight expression. Add / update inserts the draft into the table.\nEdit existing mappings directly in the table. Apply to configuration transfers the table to .properties; then Save.\nThe editor checks direct token references and types; it does not evaluate ArcSight expressions or the resulting event locally." }, 0, 0);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        root.RowStyles[1].SizeType = SizeType.AutoSize;
        target.Items.AddRange(EventMapping.Fields.Keys.Order().Cast<object>().ToArray());
        mode.Items.AddRange(["Token", "Constant", "Expression"]);
        mode.SelectedIndex = 0;
        value.Items.AddRange(EventMapping.Tokens(source).Keys.Cast<object>().ToArray());
        void Input(string label, Control control)
        {
            var group = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 8, 8) };
            group.Controls.Add(new Label { Text = label, AutoSize = true });
            group.Controls.Add(control);
            tools.Controls.Add(group);
        }
        Input("Target field", target);
        Input("Value source", mode);
        Input("Token / value / expression", value);
        tools.SetFlowBreak(value.Parent!, true);
        var add = new Button { Text = "Add / update", AutoSize = true };
        add.Click += (_, _) => Guard(() =>
        {
            table.EndEdit();
            string field = target.Text.Trim();
            if (!EventMapping.IsDirectField(field))
                throw new FormatException("Choose or enter an event.fieldName field.");
            string expression = mode.SelectedIndex == 1 ? EventMapping.Constant(value.Text) : value.Text.Trim();
            if (expression.Length == 0)
                throw new FormatException("Choose a token or enter an expression.");
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
        var remove = new Button { Text = "Remove mapping", AutoSize = true };
        remove.Click += (_, _) => { if (table.CurrentRow is { } row) table.Rows.Remove(row); RefreshDiagnostics(); };
        tools.Controls.AddRange([add, remove]);
        root.Controls.Add(tools, 0, 1);
        root.Controls.Add(fieldInfo, 0, 2);
        target.TextChanged += (_, _) => fieldInfo.Text = EventMapping.Fields.TryGetValue(target.Text, out var type) ? "Target type: " + type + ". The token should have the same type. Constant uses __stringConstant; use the appropriate operation for other types." : "Custom fields can be entered manually. Enter a decoded expression, e.g. __useCurrentYear(Timestamp).";
        mode.SelectedIndexChanged += (_, _) =>
        {
            value.Items.Clear();
            if (mode.SelectedIndex == 0)
                value.Items.AddRange(EventMapping.Tokens(source).Keys.Cast<object>().ToArray());
            else if (mode.SelectedIndex == 2)
                value.Items.AddRange(["__stringConstant(\"text\")", "__useCurrentYear(Timestamp)"]);
        };
        table.Columns.Add("Field", "Target event field");
        table.Columns.Add("Expression", "Token / expression");
        table.Columns[1].FillWeight = 180;
        foreach (DataGridViewColumn column in table.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        foreach (var assignment in EventMapping.Load(source))
            table.Rows.Add(assignment.Field, assignment.Expression);
        table.CellEndEdit += (_, _) => RefreshDiagnostics();
        root.Controls.Add(table, 0, 3);
        root.Controls.Add(diagnostics, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var apply = new Button { Text = "Apply to configuration", AutoSize = true };
        apply.Click += (_, _) => Guard(() =>
        {
            table.EndEdit();
            if (target.Text.Length > 0 || value.Text.Length > 0)
                throw new FormatException("The draft above is not yet in the table. Use Add / update or clear both fields.");
            ResultSource = EventMapping.Apply(original, Assignments());
            DialogResult = DialogResult.OK;
        });
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([apply, cancel]);
        root.Controls.Add(actions, 0, 5);
        Controls.Add(root);
        CancelButton = cancel;
        Theme.Apply(this);
        HelpUi.Describe(target, "mapping", "Target event field. The list contains fields from Appendix E; custom fields can be entered manually.");
        HelpUi.Describe(mode, "mapping", "Token is a direct reference; Constant creates __stringConstant; Expression stores the entered ArcSight operation.");
        HelpUi.Describe(value, "mapping", "Choose a token or enter a constant or decoded expression. Insert it into the table with Add / update.");
        HelpUi.Describe(table, "mapping", "Direct event.* mappings. Double-click to edit a field or expression; Remove mapping deletes the selected row.");
        HelpUi.Describe(diagnostics, "mapping", "Checks direct references, target types and selected conflicts; expressions and their return types are not evaluated.");
        table.Columns[0].ToolTipText = "For example, event.sourceAddress. Select the field in the list above to see its type.";
        table.Columns[1].ToolTipText = "A token name or expression such as __stringConstant(\"text\"). Enter the decoded value.";
        fieldInfo.Text = "Select a target field to see its expected type.";
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
            diagnostics.Text = warnings.Count == 0 ? "No warnings for direct references and known types. Verify ArcSight expressions in the connector." : string.Join(Environment.NewLine, warnings);
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
