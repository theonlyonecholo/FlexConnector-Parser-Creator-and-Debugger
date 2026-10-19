using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

/// <summary>Creates an offline proposal without replacing the working parser until explicitly applied.</summary>
internal sealed class SuggestParserDialog : Window
{
    private readonly TextEditor samples = Ui.Editor();
    private readonly TextEditor proposal = Ui.Editor(readOnly: true);
    private readonly TextEditor report = Ui.Editor(readOnly: true);
    private string? generated;
    private int revision;
    private CancellationTokenSource? pending;
    public SuggestParserDialog(string sample)
    {
        Ui.Setup(this, "Suggest parser from logs · offline", 1200, 850);
        samples.Text = sample;
        var apply = Ui.Button("Use proposed parser", () => { if (generated is not null) Close(generated); },
            "Replace the working configuration with this draft. The existing file is not saved automatically.");
        apply.IsEnabled = false;
        samples.TextChanged += (_, _) => { revision++; pending?.Cancel(); generated = null; apply.IsEnabled = false; proposal.Text = ""; report.Text = "Samples changed. Generate a new proposal."; };
        var generate = Ui.AsyncButton("Generate and validate", () => Ui.Guard(this, async () =>
        {
            int current = ++revision;
            pending?.Cancel();
            pending = new CancellationTokenSource();
            var cancellation = pending.Token;
            string input = samples.Text;
            generated = null;
            apply.IsEnabled = false;
            report.Text = "Analyzing sample structures locally…";
            try
            {
                var result = await Task.Run(() => ParserSuggester.Generate(input, cancellation), cancellation);
                if (current != revision)
                    return;
                generated = result.Source;
                proposal.Text = result.Source;
                report.Text = result.Report;
                apply.IsEnabled = true;
            }
            catch (Exception ex)
            {
                if (current == revision)
                {
                    proposal.Text = "";
                    report.Text = ex.Message;
                }
            }
        }));
        Content = new Border
        {
            Padding = new Thickness(16),
            Child = Ui.Rows("Auto,*,Auto,2*,Auto",
                Ui.Label("Paste 2–200 similar logs, one event per line. Offline structural inference produces an editable draft.\nReview event mappings, severity scales and extra processors before deployment; they cannot always be inferred from examples."),
                samples, Ui.Bar(generate),
                Ui.Columns("*,*", Ui.Rows("Auto,*", Ui.Label("Proposed .properties"), proposal), Ui.Rows("Auto,*", Ui.Label("Coverage and assumptions"), report)),
                Ui.Bar(apply, Ui.Button("Cancel", () => Close()), Ui.Help(this, "assistance")))
        };
        Ui.HelpKey(this, "assistance");
        Closed += (_, _) => { revision++; pending?.Cancel(); };
    }
}
