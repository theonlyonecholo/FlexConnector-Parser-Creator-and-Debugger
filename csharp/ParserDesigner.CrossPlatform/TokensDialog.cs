using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ParserCore;
using System.Text.RegularExpressions;

namespace ParserDesigner.CrossPlatform;

internal sealed class TokensDialog : Window
{
    private sealed record TokenRow(TokenDefinition Original, TextBox Name, ComboBox Type, TextBox Format, TextBlock Snippet);
    private readonly List<TokenRow> rows = [];
    private readonly CancellationTokenSource cancellation = new();

    public TokensDialog(string source, string pattern, string sample, RegexOptions options, bool entire, bool trim, int timeout)
    {
        Ui.Setup(this, "Tokens from regex", 1180, 760);
        MinWidth = 960;
        var draft = TokenGenerator.Draft(source, pattern);
        var table = new StackPanel { Spacing = 6 };
        table.Children.Add(Ui.Columns("100,*,150,2*,1.5*", Ui.Label("Group → token"), Ui.Label("Name"), Ui.Label("Type"), Ui.Label("Format"), Ui.Label("Message snippet")));
        foreach (var token in draft)
        {
            var name = Ui.Input(token.Name, "Unique token name referenced by event mappings. References are not renamed automatically.");
            var type = new ComboBox { ItemsSource = TokenFormats.Types.Append(token.Type).Distinct().ToArray(), SelectedItem = token.Type, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            Ui.Tip(type, "Choose the data type expected by the target ArcSight event field. Preview values are not converted.");
            var format = Ui.Input(token.Format, TokenFormats.Help(token.Type));
            type.SelectionChanged += (_, _) =>
            {
                string selected = type.SelectedItem?.ToString() ?? "String";
                if (string.IsNullOrEmpty(format.Text))
                    format.Text = TokenFormats.Suggestions(selected).FirstOrDefault() ?? "";
                Ui.Tip(format, TokenFormats.Help(selected));
            };
            var snippet = Ui.Label("Loading…");
            var row = new TokenRow(token, name, type, format, snippet);
            rows.Add(row);
            var builder = Ui.AsyncButton("Build…", async () =>
            {
                string? value = await new FormatDialog(type.SelectedItem?.ToString() ?? "String", format.Text ?? "").ShowDialog<string?>(this);
                if (value is not null)
                    format.Text = value;
            }, "Choose a format template or insert Java date/time symbols.");
            table.Children.Add(Ui.Columns("100,*,150,2*,1.5*", Ui.Label($"{token.Index + 1} → [{token.Index}]"), name, type, Ui.Columns("*,Auto", format, builder), snippet));
        }
        var apply = Ui.AsyncButton("Apply to configuration", () => Ui.Guard(this, async () =>
        {
            var tokens = rows.Select(r => new TokenDefinition(r.Original.Index, r.Name.Text ?? "", r.Type.SelectedItem?.ToString() ?? "", r.Format.Text ?? "")).ToArray();
            string updated = TokenGenerator.Apply(source, pattern, tokens);
            var oldNames = new PropertiesDocument(source).Values.Where(kv => kv.Key.StartsWith("token[", StringComparison.Ordinal) && kv.Key.EndsWith("].name", StringComparison.Ordinal)).Select(kv => kv.Value);
            string[] removed = oldNames.Except(tokens.Select(t => t.Name)).ToArray();
            if (removed.Length > 0 && await Ui.Ask(this, "Token references", "These names will no longer be declared: " + string.Join(", ", removed.Take(30)) + ". Existing event/submessage references are not renamed.", "Apply", "Cancel") != "Apply")
                return;
            Close(updated);
        }), "Write the regex and token declarations. Save the main configuration to write to disk.");
        Content = new Border
        {
            Padding = new Thickness(18),
            Child = Ui.Rows("Auto,*,Auto",
            Ui.Label($"{draft.Count} capturing groups. Group 1 → token[0]. Existing metadata is retained by index.\nSnippets show raw text from the first match; repeated groups show the last capture. Hover for the full preview.\nChanging token names does not update mapping or submessage references. Check Java/.NET group numbering."),
            new ScrollViewer { Content = table }, Ui.Bar(apply, Ui.Button("Cancel", () => Close()), Ui.Help(this, "tokens")))
        };
        Ui.HelpKey(this, "tokens");
        Closed += (_, _) => cancellation.Cancel();
        Opened += async (_, _) =>
        {
            try
            {
                var preview = await Task.Run(() => RegexDebugger.Analyze(pattern, sample, options, entire, trim, timeout, cancellation.Token));
                if (cancellation.IsCancellationRequested)
                    return;
                foreach (var row in rows)
                {
                    var capture = preview.Rows.LastOrDefault(c => c.Match == 1 && c.Group == row.Original.Index + 1 && c.Success);
                    string value = capture?.Value ?? "";
                    row.Snippet.Text = capture is null ? "— no capture" : value.Length == 0 ? "(empty capture)" : value[..Math.Min(120, value.Length)].Replace("\r", "\\r").Replace("\n", "\\n");
                    Ui.Tip(row.Snippet, capture is null ? preview.Error ?? preview.Status : $"Position {capture.Index}, length {capture.Length}\n" + value[..Math.Min(2000, value.Length)]);
                }
            }
            catch (OperationCanceledException) { }
        };
    }
}

internal sealed class FormatDialog : Window
{
    public FormatDialog(string type, string initial)
    {
        Ui.Setup(this, "Token format · " + type, 850, 600);
        var format = Ui.Input(initial, "Decoded ArcSight/Java format, not a .NET format string. Ctrl+Z undoes edits.");
        var presets = new ComboBox { ItemsSource = TokenFormats.Suggestions(type), PlaceholderText = "Select a template…", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is string value) format.Text = value; };
        var symbols = Ui.Bar();
        if (TokenFormats.IsDateTime(type))
            foreach (var (symbol, meaning) in TokenFormats.Symbols)
                symbols.Children.Add(Ui.Button(symbol + " · " + meaning, () =>
                {
                    int start = Math.Min(format.SelectionStart, format.SelectionEnd), end = Math.Max(format.SelectionStart, format.SelectionEnd);
                    string text = format.Text ?? "";
                    format.Text = text[..start] + symbol + text[end..];
                    format.CaretIndex = start + symbol.Length;
                    format.Focus();
                }, "Insert at the cursor, replacing selected text."));
        Content = new Border
        {
            Padding = new Thickness(18),
            Child = Ui.Rows("Auto,Auto,Auto,*,Auto",
            Ui.Label(TokenFormats.Help(type), 15), presets, format, new ScrollViewer { Content = symbols },
            Ui.Bar(Ui.Button("Apply format", () => Close(format.Text ?? "")), Ui.Button("Clear format", () => format.Text = ""), Ui.Button("Cancel", () => Close()), Ui.Help(this, "formats")))
        };
        Ui.HelpKey(this, "formats");
    }
}
