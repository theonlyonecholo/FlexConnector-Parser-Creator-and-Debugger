using System.Text;
using System.Text.RegularExpressions;
using ParserCore;

namespace ParserDesigner;

internal sealed partial class MainForm
{
    /// <summary>Invalidates the old preview immediately and debounces the next analysis.</summary>
    private void Schedule()
    {
        if (loading)
            return;
        revision++;
        pending?.Cancel();
        debounce.Stop();
        result = null;
        groupRegions = [];
        activeRegexGroup = null;
        populating = true;
        grid.Rows.Clear();
        populating = false;
        report.Clear();
        PaintHighlights(null);
        status.Text = automatic.Checked ? "Waiting for typing to finish…" : "The preview is out of date. Run Test / Debug.";
        if (automatic.Checked)
            debounce.Start();
    }
    private void Stop()
    {
        debounce.Stop();
        revision++;
        pending?.Cancel();
        status.Text = "Evaluation stopped.";
    }
    /// <summary>Evaluates an input snapshot and publishes it only if its revision is still current.</summary>
    private async Task AnalyzeAsync()
    {
        debounce.Stop();
        pending?.Cancel();
        pending?.Dispose();
        pending = new CancellationTokenSource();
        CancellationToken token = pending.Token;
        int run = ++revision;
        string text = message.Text, expression = pattern.Text, config = source.Text;
        bool whole = full.Checked, trimInput = trim.Checked;
        int milliseconds = (int)timeout.Value;
        var options = RegexOptions.None;
        if (ignoreCase.Checked)
            options |= RegexOptions.IgnoreCase;
        if (multiline.Checked)
            options |= RegexOptions.Multiline;
        if (singleline.Checked)
            options |= RegexOptions.Singleline;
        status.Text = "Evaluating regex…";
        try
        {
            await worker.WaitAsync(token);
            DebugResult analysis;
            try
            {
                analysis = await Task.Run(() => RegexDebugger.Analyze(expression, text, options, whole, trimInput, milliseconds, token), token);
            }
            finally { worker.Release(); }
            if (IsDisposed || run != revision || token.IsCancellationRequested)
                return;
            result = analysis;
            groupRegions = analysis.Error is null ? RegexGroupMap.Build(expression) : [];
            analyzedText = text;
            PropertiesDocument? doc = null;
            string configError = "";
            try
            {
                doc = new(config);
            }
            catch (FormatException ex) { configError = "Configuration error: " + ex.Message; }
            populating = true;
            grid.Rows.Clear();
            foreach (DebugRow row in analysis.Rows)
            {
                string name = row.Name;
                if (row.Group > 0 && doc is not null && doc.Get($"token[{row.Group - 1}].name") is string tokenName && tokenName.Length > 0)
                    name += " / " + tokenName;
                int i = grid.Rows.Add(row.Match, row.Group, name, row.Capture, row.Success ? "yes" : "no", row.Index, row.Length,
                    row.Value.Length > 500 ? row.Value[..500] + "…" : row.Value);
                grid.Rows[i].Tag = row;
                if (row.Group > 0)
                    grid.Rows[i].Cells[2].Style.BackColor = TokenColors.For(row.Group - 1);
            }
            grid.ClearSelection();
            populating = false;
            status.Text = analysis.Status + $" {analysis.Milliseconds:F1} ms";
            var detail = new StringBuilder();
            detail.AppendLine("Engine: .NET. Positions are zero-based UTF-16 indices. Debug navigates results and repeated captures, not internal backtracking.");
            detail.AppendLine("ArcSight uses Java regex; named groups and other constructs may have different semantics or numbering.");
            detail.AppendLine("The i/m/s, Entire message and Trim switches affect the preview only; they are not saved to the parser.");
            if (analysis.Error is not null)
                detail.AppendLine(analysis.Error + (analysis.ErrorOffset is int offset ? $"\r\nError position in regex: {offset}" : ""));
            if (configError.Length > 0)
                detail.AppendLine(configError);
            if (doc is not null)
            {
                if (doc.Get("regex") != expression)
                    detail.AppendLine("The Regex field differs from the configuration. Saving will offer to write the changed regex.");
                if (analysis.Matches.Count > 0)
                {
                    int groupCount = analysis.Rows.Where(r => r.Match == 1 && r.Group > 0).Select(r => r.Group).Distinct().Count();
                    if (!int.TryParse(doc.Get("token.count"), out int count) || count != groupCount)
                        detail.AppendLine($"Warning: token.count={doc.Get("token.count")}; groups shown in the first match={groupCount}.");
                    string severityToken = doc.Get("event.deviceSeverity");
                    if (severityToken.Length > 0)
                    {
                        DebugRow? severity = analysis.Rows.LastOrDefault(r => r.Match == 1 && r.Group > 0 && r.Success && doc.Get($"token[{r.Group - 1}].name") == severityToken);
                        try
                        {
                            detail.AppendLine(severity is null ? "Severity: cannot evaluate the expression; a direct token reference is required." : AdvancedConfiguration.PreviewSeverity(doc, severity.Value));
                        }
                        catch (FormatException ex) { detail.AppendLine("Severity: " + ex.Message); }
                    }
                }
                detail.AppendLine("Extra processors: " + doc.Get("extraprocessor.count") + " — linked files are not executed locally.");
            }
            report.Text = detail.ToString();
            PaintHighlights(null);
            if (pattern.Focused)
                HighlightRegexGroup();
        }
        catch (OperationCanceledException) { }
    }
}
