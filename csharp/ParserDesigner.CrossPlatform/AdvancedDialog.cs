using Avalonia;
using Avalonia.Controls;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

internal sealed class AdvancedDialog : Window
{
    public AdvancedDialog(string source)
    {
        Ui.Setup(this, "Severity mapping / Extra processors", 1000, 760);
        var doc = new PropertiesDocument(source);
        var token = Ui.Input(doc.Get("event.deviceSeverity"), "Original device severity: use a direct token name for the local preview.");
        var rules = AdvancedConfiguration.SeverityLevels.ToDictionary(level => level, level => Ui.Input(doc.Get(AdvancedConfiguration.SeverityKey(level)), "Comma-separated values or inclusive numeric ranges, such as OPEN,CLOSE or 400..599."));
        var severity = Ui.Stack(Ui.Label("event.deviceSeverity"), token);
        foreach (var (level, input) in rules)
            severity.Children.Add(Ui.Columns("100,*", Ui.Label(level), input));
        severity.Children.Add(Ui.Label("Empty rules are removed. Overlaps are reported as ambiguous; text comparison is exact."));
        var extraDoc = new PropertiesDocument(source);
        extraDoc.RemoveWhere(key => !AdvancedConfiguration.IsProcessorKey(key));
        var processors = Ui.Editor(extraDoc.Source);
        Ui.Tip(processors, "extraprocessor[n].* blocks. Indices start at zero without gaps. Linked files are not executed or checked for existence.");
        var types = new ComboBox { ItemsSource = AdvancedConfiguration.ProcessorTypes, SelectedIndex = 0 };
        var template = Ui.AsyncButton("Add template", () => Ui.Guard(this, () =>
        {
            int count = AdvancedConfiguration.ValidateProcessors(new PropertiesDocument(processors.Text));
            Ui.Replace(processors, PropertiesDocument.JoinSources(processors.Text, AdvancedConfiguration.ProcessorTemplate(types.SelectedItem?.ToString() ?? "regex", count)));
            return Task.CompletedTask;
        }));
        var tabs = new TabControl
        {
            ItemsSource = new[] {
            new TabItem { Header = "Severity mapping", Content = severity },
            new TabItem { Header = "Extra processors", Content = Ui.Rows("Auto,*", Ui.Bar(types, template, Ui.Help(this, "processors")), processors) }
        }
        };
        Content = new Border
        {
            Padding = new Thickness(18),
            Child = Ui.Rows("*,Auto", tabs,
            Ui.Bar(Ui.AsyncButton("Apply to configuration", () => Ui.Guard(this, () =>
            {
                Close(AdvancedConfiguration.Apply(source, token.Text ?? "", rules.ToDictionary(kv => kv.Key, kv => kv.Value.Text ?? ""), processors.Text));
                return Task.CompletedTask;
            })), Ui.Button("Cancel", () => Close()), Ui.Help(this, "severity")))
        };
        Ui.HelpKey(this, "severity");
    }
}
