using ParserCore;

namespace ParserDesigner;

/// <summary>Validates a whole parser source proposal before returning it to the batch workbench.</summary>
internal sealed class ParserSourceDialog : Form
{
    public string ResultSource
    {
        get; private set;
    }
    public ParserSourceDialog(string source)
    {
        ResultSource = source;
        Text = "Edit parser and retest logs";
        Size = new Size(1000, 750);
        MinimumSize = new Size(800, 550);
        StartPosition = FormStartPosition.CenterParent;
        var editor = new UndoTextBox
        {
            Text = source,
            Multiline = true,
            AcceptsTab = true,
            AcceptsReturn = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            MaxLength = 0,
            Font = new Font("Consolas", 10)
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        var apply = new Button { Text = "Apply and retest logs", AutoSize = true };
        apply.Click += (_, _) =>
        {
            try
            {
                _ = new ParserEvaluator(editor.Text);
                ResultSource = editor.Text;
                DialogResult = DialogResult.OK;
            }
            catch (FormatException ex) { MessageBox.Show(this, ex.Message, "Configuration", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([apply, cancel]);
        CancelButton = cancel;
        Controls.Add(editor);
        Controls.Add(buttons);
        Theme.Apply(this);
        HelpUi.Describe(editor, "properties", "The entire parser in properties syntax. Applying validates it for local testing and retests the logs; save to disk in the main window.");
        editor.ResetHistory();
    }
}
