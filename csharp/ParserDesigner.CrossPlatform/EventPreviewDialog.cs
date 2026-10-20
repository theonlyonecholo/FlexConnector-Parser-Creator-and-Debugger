using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using ParserCore;

namespace ParserDesigner.CrossPlatform;

/// <summary>Inspectable event stages, map dependencies and reusable expected-event tests.</summary>
internal sealed class EventPreviewDialog : Window
{
    private readonly string source;
    private readonly string? fileName;
    private readonly TextEditor log, fixture = Ui.Editor(), output = Ui.Editor(readOnly: true);
    private readonly TextBox root;
    private readonly TextBlock status = Ui.Label("Ready");
    private CancellationTokenSource? pending;
    private int revision;
    private bool closed;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal EventPreviewDialog(string source, string sample, string? dependencyRoot = null, string? fileName = null)
    {
        this.source = source;
        this.fileName = fileName;
        log = Ui.Editor(sample);
        root = Ui.Input(dependencyRoot ?? "", "Root used to resolve extraprocessor filename paths. Choose the local equivalent of the connector's fcp/flexagent root.");
        fixture.Text = JsonSerializer.Serialize(new ExpectedEventSuite(1,
            [new("Example — edit expected fields", sample, new() { ["event.name"] = "Expected name" })]), Json);
        Ui.Setup(this, "Event preview · dependencies · expected-field tests", 1200, 900);
        Content = new Border
        {
            Padding = new Thickness(16),
            Child = Ui.Rows("Auto,Auto,Auto,*,Auto",
                Ui.Label("Local supported-stage preview, not the ArcSight runtime. Unsupported steps are INCOMPLETE.\nUses the Configuration snapshot. Timestamps without offsets use UTC. Save fixtures to reuse field assertions."),
                Ui.Columns("Auto,*,Auto", Ui.Label("Dependency root"), root,
                    Ui.AsyncButton("Choose folder…", () => Ui.Guard(this, ChooseRootAsync))),
                Ui.Bar(Ui.AsyncButton("Evaluate event", () => Ui.Guard(this, EvaluateAsync)),
                    Ui.AsyncButton("Run expected tests", () => Ui.Guard(this, TestAsync)),
                    Ui.AsyncButton("Load tests…", () => Ui.Guard(this, LoadAsync)),
                    Ui.AsyncButton("Save tests…", () => Ui.Guard(this, SaveAsync)),
                    Ui.Button("Stop", () => pending?.Cancel())),
                Ui.Columns("*,*", new TabControl
                {
                    ItemsSource = new[]
                    {
                        new TabItem { Header = Ui.Label("Log"), Content = log },
                        new TabItem { Header = Ui.Label("Expected tests (JSON)"), Content = fixture },
                        new TabItem { Header = Ui.Label("Configuration snapshot"), Content = Ui.Editor(source, readOnly: true) }
                    }
                }, output), status)
        };
        void Invalidate()
        {
            pending?.Cancel();
            revision++;
            output.Text = "";
            status.Text = "Input changed. Evaluate or run tests again.";
        }
        log.TextChanged += (_, _) => Invalidate();
        fixture.TextChanged += (_, _) => Invalidate();
        root.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Invalidate(); };
        Closed += (_, _) => { closed = true; revision++; pending?.Cancel(); };
        Opened += async (_, _) => await Ui.Guard(this, EvaluateAsync);
    }
    private async Task ChooseRootAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new()
        {
            Title = "Choose map dependency root",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            root.Text = folders[0].TryGetLocalPath() ?? "";
    }
    private async Task RunAsync(Func<CancellationToken, (string Output, string Status)> work)
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = new();
        var cancellation = pending.Token;
        int version = ++revision;
        status.Text = "Evaluating…";
        try
        {
            var result = await Task.Run(() => work(cancellation), cancellation);
            if (closed || revision != version)
                return;
            output.Text = result.Output;
            status.Text = result.Status;
        }
        catch (OperationCanceledException) { if (!closed && revision == version) status.Text = "Cancelled"; }
        catch (Exception ex)
        {
            if (!closed && revision == version)
            {
                output.Text = ex.Message;
                status.Text = "INCOMPLETE · evaluation error";
            }
        }
    }
    internal Task EvaluateAsync()
    {
        string text = log.Text, directory = root.Text ?? "";
        return RunAsync(c =>
        {
            var result = EventPipeline.Evaluate(source, text, directory, fileName, c);
            return (JsonSerializer.Serialize(result, Json),
                $"{result.Family} · {result.ParseState} · {(result.Complete ? "SUPPORTED STAGES EVALUATED" : "INCOMPLETE")} · {result.Fields.Count} fields");
        });
    }
    internal Task TestAsync()
    {
        string tests = fixture.Text, directory = root.Text ?? "";
        return RunAsync(c =>
        {
            var results = EventExpectations.Run(source, tests, directory, c);
            return (JsonSerializer.Serialize(results, Json),
                $"{results.Count(r => r.Status == "PASS")} PASS · {results.Count(r => r.Status == "FAIL")} FAIL · {results.Count(r => r.Status == "INCOMPLETE")} INCOMPLETE");
        });
    }
    private async Task LoadAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Open expected-event tests",
            FileTypeFilter = [new("JSON fixtures") { Patterns = ["*.json"] }]
        });
        if (files.Count == 0)
            return;
        string path = files[0].TryGetLocalPath() ?? throw new IOException("Select a local file.");
        if (new FileInfo(path).Length > 4 * 1024 * 1024)
            throw new FormatException("Fixture limit is 4 MB.");
        string text = await File.ReadAllTextAsync(path);
        _ = EventExpectations.Load(text);
        if (!closed)
            fixture.Text = text;
    }
    private async Task SaveAsync()
    {
        string text = fixture.Text;
        _ = EventExpectations.Load(text);
        var file = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "Save expected-event tests",
            SuggestedFileName = "expected-events.json"
        });
        if (file is null)
            return;
        string path = file.TryGetLocalPath() ?? throw new IOException("Select a local file.");
        await File.WriteAllTextAsync(path, text);
    }
}
