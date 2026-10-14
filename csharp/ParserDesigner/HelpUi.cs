using System.Runtime.CompilerServices;

namespace ParserDesigner;

/// <summary>Attaches contextual help, accessibility descriptions and a footer once per form.</summary>
internal static class HelpUi
{
    private sealed class State
    {
        public readonly ToolTip Tips = new() { AutoPopDelay = 20000, InitialDelay = 450, ReshowDelay = 100, ShowAlways = true };
        public readonly Dictionary<Control, string> Topics = [];
    }
    private static readonly ConditionalWeakTable<Form, State> States = new();
    public static string DefaultTopic(Form form) => form switch
    {
        EventMappingDialog => "mapping",
        TokenDialog => "tokens",
        TokenFormatDialog => "formats",
        SubmessageDialog => "submessages",
        BatchForm => "batch",
        AdvancedDialog => "severity",
        ParserSourceDialog => "properties",
        _ => "start"
    };
    public static void Describe(Control control, string topic, string text)
    {
        var form = control.FindForm();
        if (form is null || !States.TryGetValue(form, out var state))
            return;
        state.Tips.SetToolTip(control, text + "\nF1: detailed help.");
        control.AccessibleDescription = text;
        state.Topics[control] = topic;
    }
    public static void Install(Form form)
    {
        if (form is HelpWindow || States.TryGetValue(form, out _))
            return;
        var state = new State();
        States.Add(form, state);
        string topic = DefaultTopic(form);
        void Walk(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                string? tip = ButtonTip(c.Text);
                if (c is Button && tip is not null)
                    Describe(c, TopicForButton(c.Text, topic), tip);
                else if (c is Button && c.Text.Contains(" — ", StringComparison.Ordinal))
                    Describe(c, "formats", "Insert this format symbol at the cursor, replacing selected text.");
                else if (c is TextBoxBase or ComboBox or TreeView or DataGridView or CheckBox or NumericUpDown)
                    Describe(c, topic, "Press F1 for an explanation and usage instructions in the topic “" + HelpTopics.All.Single(t => t.Id == topic).Title + "“.");
                if (c is DataGridView grid)
                    foreach (DataGridViewColumn column in grid.Columns)
                        column.ToolTipText = ColumnTip(column.HeaderText);
                Walk(c);
            }
        }
        Walk(form);
        var footer = new StatusStrip { SizingGrip = false };
        var help = new ToolStripButton("Help (F1)") { ToolTipText = "Open searchable English help with parser examples." };
        help.Click += (_, _) => Show(form, topic);
        footer.Items.Add(help);
        footer.Items.Add(new ToolStripStatusLabel("Hover for field descriptions • F1 explains the active field"));
        form.Controls.Add(footer);
        form.KeyPreview = true;
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.F1)
                return;
            e.Handled = e.SuppressKeyPress = true;
            Control? active = form.ActiveControl;
            while (active is ContainerControl container && container.ActiveControl is not null)
                active = container.ActiveControl;
            while (active is not null && !state.Topics.ContainsKey(active))
                active = active.Parent;
            Show(form, active is not null ? state.Topics[active] : topic);
        };
        form.FormClosed += (_, _) => state.Tips.Dispose();
    }
    private static void Show(Form owner, string topic)
    {
        using var help = new HelpWindow(topic);
        help.ShowDialog(owner);
    }
    private static string TopicForButton(string text, string fallback) => text switch
    {
        "Tokens from regex…" => "tokens",
        "Submessages…" or "Edit submessage…" => "submessages",
        "Event mapping…" => "mapping",
        "Severity / processors…" => "severity",
        "Test log file…" => "batch",
        "Format builder…" => "formats",
        "Add template" => "processors",
        "Test / Debug (F5)" or "◀ Previous capture" or "Next capture ▶" or "Load message…" => "regex",
        "Configuration → Regex" or "Regex → Configuration" or "Save" or "Save as…" or "Open .properties" or "Edit parser…" => "properties",
        _ => fallback
    };
    private static string? ButtonTip(string text) => text switch
    {
        "Event mapping…" => "Open the editor for assigning tokens, constants and expressions to target event fields.",
        "Add / update" => "Insert the draft into the mapping table, updating an existing entry for the same target field.",
        "Remove mapping" => "Remove the selected row. The configuration changes only when you apply the dialog.",
        "Open .properties" => "Load the parser configuration and main regex from a file. Offers to save unsaved changes first.",
        "Save" => "Write the configuration to disk. If the working regex has changed, offers to write it into the configuration first.",
        "Save as…" => "Save the configuration to the selected file.",
        "Configuration → Regex" => "Load and decode the regex property from the configuration into the working Regex field.",
        "Regex → Configuration" => "Escape the working regex and write it to properties. This does not regenerate tokens.",
        "Severity / processors…" => "Configure severity normalization and references to downstream parsers.",
        "Tokens from regex…" => "Create tokens from capturing groups; choose names, types and formats.",
        "Submessages…" => "Define log body variants by message ID and pattern order.",
        "Test log file…" => "Evaluate one parser against file lines; browse and debug variants.",
        "Test / Debug (F5)" => "Evaluate the working regex and show matches and repeated captures. This does not step through internal backtracking.",
        "Stop" => "Request test cancellation. A running regex is bounded by its timeout.",
        "◀ Previous capture" or "Next capture ▶" => "Select the adjacent capture in the Debug table and highlight it in Message.",
        "Load message…" => "Load a text file into Message to test a single input.",
        "Open logs…" => "Load a file using the selected encoding; each line is a separate log.",
        "Test all" => "Evaluate all loaded logs again using the current parser.",
        "← Log" or "Log →" => "Go to the adjacent log in the currently filtered list.",
        "Edit submessage…" => "Open the definition used or attempted for the selected log. Applying changes retests the file.",
        "Edit parser…" => "Edit the entire configuration; applying changes retests the logs.",
        "Apply to configuration" => "Transfer the draft into the main configuration. Then use Save to write it to disk.",
        "Apply and retest logs" => "Apply the configuration draft to the test window and evaluate the file again.",
        "Format builder…" => "Open templates and the format builder for the selected token type.",
        "Apply format" => "Return the proposed format to the selected token row.",
        "Clear format" => "Clear the format; the format property is removed when the token is exported.",
        "Cancel" => "Close this dialog without applying its draft.",
        "+ Submessage" => "Add a definition for another message ID with an initial pattern.",
        "+ Pattern" => "Add an alternative regex to the selected submessage.",
        "Remove" => "Remove the selected pattern or the entire submessage, depending on the tree node.",
        "↑ Earlier" or "↓ Later" => "Move the definition or pattern in the order. The first successful pattern wins.",
        "Test log with these changes" => "Test the entire selected log against the current parser draft, including submessage routing.",
        "Add template" => "Append an extra processor block of the selected type. Supply the actual path and parameters.",
        _ => null
    };
    private static string ColumnTip(string title) => title switch
    {
        "Type" => "Token data type; it should match the target event field. F1: type reference.",
        "Format" => "Decoded token conversion format, such as yyyy-MM-dd HH:mm:ss. Use Format builder for help.",
        "Name" or "Name / token" => "The name used as a mapping reference. Its color matches the token in the text.",
        "Group → token" => "Group 1 corresponds to token[0]; noncapturing groups have no token.",
        "Position" or "Position (0-based)" => "Position in the original text: zero-based UTF-16 index; -1 means no capture.",
        "Length" => "Capture length in UTF-16 units. Zero is an empty capture.",
        "Submessage / pattern" => "The selected or available definition. Details show individual attempts.",
        "Message ID" => "The token value used to select a submessage.",
        "Result" => "Distinguishes a match, main regex mismatch, missing submessage, body mismatch and error.",
        "Line" => "The original line number in the log file.",
        "Group" => "Capturing group number. Group 0 is the entire match.",
        "Capture" => "The sequence number of a repeated capture within the same group.",
        "Match" => "The sequence number of the match found in Message.",
        "Success" => "Whether this group captured a value in this match.",
        "Value" => "Captured text. Long values may be truncated in the table.",
        "Stage" => "Whether the capture comes from the main regex, a token or a submessage.",
        "Token / field" => "Token or submessage target field name; its color matches the capture.",
        _ => "Details and examples: F1."
    };
}

