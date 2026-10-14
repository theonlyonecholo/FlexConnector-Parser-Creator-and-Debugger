using System.Reflection;
using ParserCore;

internal static class Program
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static void Shortcut(Control editor, Keys keys)
    {
        object[] args = [default(Message), keys];
        bool handled = (bool)editor.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args)!;
        if (!handled)
            throw new Exception("Undo/redo shortcut not handled");
    }
    private static void PumpUntil(Func<bool> ready)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!ready() && timer.ElapsedMilliseconds < 5000)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        if (!ready())
            throw new Exception("UI did not become ready");
        Application.DoEvents();
    }
    private static void Capture(Form form, string file)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(file);
    }
    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
    [STAThread]
    private static int Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            string output = Path.Combine(root, "build");
            Directory.CreateDirectory(output);
            var assembly = Assembly.Load("ArcSight.ParserDesigner");
            using (var help = (Form)Activator.CreateInstance(assembly.GetType("ParserDesigner.HelpWindow")!, "submessages")!)
            {
                help.ShowInTaskbar = false;
                help.Opacity = 0;
                help.Show();
                Application.DoEvents();
                var helpBody = Field<RichTextBox>(help, "content");
                var helpSearch = Field<TextBox>(help, "search");
                var helpTopics = Field<ListBox>(help, "topics");
                if (!helpBody.Text.Contains("mappings.delimiter", StringComparison.Ordinal))
                    throw new Exception("Contextual submessage help missing");
                if (helpTopics.Items.Count != 11)
                    throw new Exception("Help topics missing");
                helpSearch.Text = "overrideeventmappings";
                if (helpTopics.Items.Count != 1 || !helpBody.Text.Contains("clearfieldafterparsing", StringComparison.Ordinal))
                    throw new Exception("Help must search topic content");
                helpSearch.Text = "no-such-help-topic-123";
                if (helpTopics.Items.Count != 0 || !helpBody.Text.StartsWith("Žádné téma", StringComparison.Ordinal))
                    throw new Exception("Empty help search should explain result");
                helpSearch.Clear();
                if (helpTopics.Items.Count != 11)
                    throw new Exception("Clearing help search should restore all topics");
                Capture(help, Path.Combine(output, "ui-help.png"));
                help.Close();
            }
            foreach (string type in new[] { "UndoTextBox", "UndoRichTextBox" })
            {
                using var editor = (TextBoxBase)Activator.CreateInstance(assembly.GetType("ParserDesigner." + type)!)!;
                editor.Multiline = true;
                _ = editor.Handle;
                editor.Text = "výchozí 😀\r\n";
                editor.GetType().GetMethod("ResetHistory")!.Invoke(editor, null);
                string initial = editor.Text;
                for (int i = 0; i < 105; i++)
                {
                    editor.Select(editor.TextLength, 0);
                    editor.SelectedText = "x";
                }
                for (int i = 0; i < 100; i++)
                    Shortcut(editor, Keys.Control | Keys.Z);
                if (editor.Text != initial + "xxxxx")
                    throw new Exception(type + ": 100 undo steps failed");
                Shortcut(editor, Keys.Control | Keys.Z);
                if (editor.Text != initial + "xxxxx")
                    throw new Exception("History exceeded 100 steps");
                for (int i = 0; i < 100; i++)
                    Shortcut(editor, Keys.Control | Keys.Y);
                if (editor.Text != initial + new string('x', 105))
                    throw new Exception(type + ": redo failed");
                Shortcut(editor, Keys.Control | Keys.Z);
                editor.Select(editor.TextLength, 0);
                editor.SelectedText = "nové";
                string branch = editor.Text;
                Shortcut(editor, Keys.Control | Keys.Y);
                if (editor.Text != branch)
                    throw new Exception("New edit must discard redo branch");
                editor.Select(0, 7);
                editor.SelectedText = "změna";
                Shortcut(editor, Keys.Control | Keys.Z);
                if (editor.Text != branch)
                    throw new Exception($"{type}: Replacement undo failed: expected [{branch}], actual [{editor.Text}]");
                Shortcut(editor, Keys.Control | Keys.Shift | Keys.Z);
                if (!editor.Text.StartsWith("změna", StringComparison.Ordinal))
                    throw new Exception("Ctrl+Shift+Z redo failed");
                editor.Text = "programový zápis";
                if (editor is RichTextBox richEditor)
                {
                    richEditor.SelectAll();
                    richEditor.SelectionBackColor = Color.Yellow;
                }
                Shortcut(editor, Keys.Control | Keys.Z);
                if (!editor.Text.StartsWith("změna", StringComparison.Ordinal))
                    throw new Exception("Formatting must not consume undo step");
                editor.GetType().GetMethod("ResetHistory")!.Invoke(editor, null);
                string reset = editor.Text;
                Shortcut(editor, Keys.Control | Keys.Z);
                if (editor.Text != reset)
                    throw new Exception("New document history not cleared");
            }
            using var main = (Form)Activator.CreateInstance(assembly.GetType("ParserDesigner.MainForm")!, true)!;
            main.ShowInTaskbar = false;
            main.Opacity = 0;
            main.Show();
            PumpUntil(() => Field<object?>(main, "result") is not null);
            if (!Field<TextBox>(main, "pattern").AccessibleDescription!.Contains("Dekódovaný", StringComparison.Ordinal))
                throw new Exception("Regex field description missing");
            if (main.Controls.OfType<StatusStrip>().Count() != 1)
                throw new Exception("Help entry missing or duplicated");
            bool f1OpenedRegex = false;
            Field<TextBox>(main, "pattern").Focus();
            using (var closeHelp = new System.Windows.Forms.Timer { Interval = 100 })
            {
                closeHelp.Tick += (_, _) =>
                {
                    var opened = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "HelpWindow");
                    if (opened is null)
                        return;
                    opened.Opacity = 0;
                    f1OpenedRegex = Field<RichTextBox>(opened, "content").Text.StartsWith("Regex, Message", StringComparison.Ordinal);
                    opened.Close();
                    closeHelp.Stop();
                };
                closeHelp.Start();
                typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [new KeyEventArgs(Keys.F1)]);
                closeHelp.Stop();
            }
            if (!f1OpenedRegex)
                throw new Exception("F1 must open help for the focused regex editor");
            var rich = Field<RichTextBox>(main, "message");
            rich.Select(1, 1);
            if (rich.SelectionBackColor.ToArgb() != Color.FromArgb(255, 235, 130).ToArgb())
                throw new Exception("Main highlight missing");
            rich.Select(rich.Text.IndexOf("404", StringComparison.Ordinal), 1);
            Color statusColor = rich.SelectionBackColor;
            rich.Select(rich.Text.IndexOf("Resource", StringComparison.Ordinal), 1);
            Color messageColor = rich.SelectionBackColor;
            if (statusColor == messageColor || statusColor == Color.White || messageColor == Color.White)
                throw new Exception("Tokens need distinct colors");
            var debugGrid = Field<DataGridView>(main, "grid");
            var statusRow = debugGrid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is DebugRow { Group: 1 });
            if (statusRow.Cells[2].Style.BackColor != statusColor)
                throw new Exception("Token legend differs from text color");
            Capture(main, Path.Combine(output, "ui-main.png"));
            Size largeMain = main.Size;
            main.Size = main.MinimumSize;
            Application.DoEvents();
            var mainToolbar = Descendants(main).OfType<ToolStrip>().Single(t => t.Name == "MainToolbar");
            if (mainToolbar.Items.OfType<ToolStripDropDownButton>().Count() != 3)
                throw new Exception("Main commands must be grouped into three menus");
            if (mainToolbar.Items.Cast<ToolStripItem>().Any(i => i.Placement != ToolStripItemPlacement.Main))
                throw new Exception("Toolbar commands must fit at minimum width");
            foreach (var flow in Descendants(main).OfType<FlowLayoutPanel>())
                if (flow.Controls.Cast<Control>().Any(c => c.Bottom > flow.ClientSize.Height || c.Right > flow.ClientSize.Width))
                    throw new Exception("Main actions clipped at minimum size");
            Capture(main, Path.Combine(output, "ui-main-compact.png"));
            main.Size = largeMain;
            Application.DoEvents();
            var regexEditor = Field<TextBox>(main, "pattern");
            regexEditor.SelectionStart = regexEditor.Text.IndexOf("\\d", StringComparison.Ordinal);
            typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(regexEditor,
                [new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)]);
            rich.Select(rich.Text.IndexOf("404", StringComparison.Ordinal), 1);
            if (rich.SelectionBackColor != Color.LightSkyBlue)
                throw new Exception("Click in regex group did not highlight token");
            if (!Field<Label>(main, "status").Text.Contains("Status", StringComparison.Ordinal))
                throw new Exception("Missing selected token name");
            regexEditor.SelectionStart = regexEditor.Text.IndexOf(".*", StringComparison.Ordinal);
            typeof(Control).GetMethod("OnKeyUp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(regexEditor, [new KeyEventArgs(Keys.Right)]);
            rich.Select(rich.Text.IndexOf("Resource", StringComparison.Ordinal), 1);
            if (rich.SelectionBackColor != Color.LightSkyBlue)
                throw new Exception("Second regex group not highlighted");
            rich.Select(rich.Text.IndexOf("404", StringComparison.Ordinal), 1);
            if (rich.SelectionBackColor != statusColor)
                throw new Exception("Previous active group not restored");
            Capture(main, Path.Combine(output, "ui-regex-group.png"));
            regexEditor.SelectionStart = 0;
            Invoke(main, "HighlightRegexGroup");
            rich.Select(rich.Text.IndexOf("Resource", StringComparison.Ordinal), 1);
            if (rich.SelectionBackColor != messageColor)
                throw new Exception("Leaving regex group must restore token colors");
            main.GetType().GetField("loading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, true);
            regexEditor.Text = @"(\d)+(?<Empty>)(x)?";
            rich.Text = "123 45";
            main.GetType().GetField("loading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, false);
            Invoke(main, "Schedule");
            PumpUntil(() => Field<DebugResult?>(main, "result") is not null);
            regexEditor.SelectionStart = 2;
            Invoke(main, "HighlightRegexGroup");
            foreach (int position in new[] { 0, 1, 2, 4, 5 })
            {
                rich.Select(position, 1);
                if (rich.SelectionBackColor != Color.LightSkyBlue)
                    throw new Exception("Repeated captures across matches must all highlight");
            }
            regexEditor.SelectionStart = regexEditor.Text.IndexOf("Empty", StringComparison.Ordinal);
            Invoke(main, "HighlightRegexGroup");
            if (!Field<Label>(main, "status").Text.Contains("prázdné záchyty", StringComparison.Ordinal))
                throw new Exception("Empty captures must be explained");
            regexEditor.SelectionStart = regexEditor.Text.IndexOf("(x)", StringComparison.Ordinal) + 1;
            Invoke(main, "HighlightRegexGroup");
            if (!Field<Label>(main, "status").Text.Contains("bez záchytu", StringComparison.Ordinal))
                throw new Exception("Unmatched group must be explained");
            var configEditor = Field<TextBox>(main, "source");
            string beforeGenerated = configEditor.Text;
            configEditor.Text = TokenGenerator.Apply(beforeGenerated, "(.*)", TokenGenerator.Draft(beforeGenerated, "(.*)"));
            Shortcut(configEditor, Keys.Control | Keys.Z);
            if (configEditor.Text != beforeGenerated)
                throw new Exception("Generated token configuration must undo in one step");
            main.GetType().GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, false);
            main.Close();

            string source = File.ReadAllText(Path.Combine(root, "examples/submessages.sdkrfilereader.properties"));
            using var subDialog = (Form)Activator.CreateInstance(assembly.GetType("ParserDesigner.SubmessageDialog")!,
                source, "id=LOGIN body=unrecognized login payload", 0, 1)!;
            subDialog.ShowInTaskbar = false;
            subDialog.Opacity = 0;
            subDialog.Show();
            Application.DoEvents();
            Descendants(subDialog).OfType<Button>().Single(b => b.Text == "+ Pattern").PerformClick();
            var subEdits = Field<Dictionary<string, TextBox>>(subDialog, "edits");
            subEdits["regex"].Text = "(unrecognized login payload)";
            subEdits["fields"].Text = "event.message";
            var subTree = Field<TreeView>(subDialog, "tree");
            subTree.SelectedNode = subTree.Nodes[1].Nodes[0];
            subTree.SelectedNode = subTree.Nodes[0].Nodes[2];
            if (subEdits["regex"].Text != "(unrecognized login payload)")
                throw new Exception("Switching patterns lost edits");
            Descendants(subDialog).OfType<Button>().Single(b => b.Text == "Testovat log s těmito změnami").PerformClick();
            PumpUntil(() => Field<TextBox>(subDialog, "result").Text.Contains("submessage[0].pattern[2]", StringComparison.Ordinal));
            subEdits["regex"].SelectionStart = 2;
            var highlightSub = (Task)subDialog.GetType().GetMethod("HighlightGroupAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(subDialog, null)!;
            PumpUntil(() => highlightSub.IsCompleted);
            highlightSub.GetAwaiter().GetResult();
            var subSample = Field<RichTextBox>(subDialog, "sample");
            subSample.Select(subSample.Text.IndexOf("unrecognized", StringComparison.Ordinal), 1);
            if (subSample.SelectionBackColor != Color.LightSkyBlue)
                throw new Exception("Submessage group must highlight original log offset");
            Capture(subDialog, Path.Combine(output, "ui-submessages.png"));
            Descendants(subDialog).OfType<Button>().Single(b => b.Text == "Použít v konfiguraci").PerformClick();
            string editedSource = (string)subDialog.GetType().GetProperty("ResultSource")!.GetValue(subDialog)!;
            subDialog.Close();
            using var batch = (Form)Activator.CreateInstance(assembly.GetType("ParserDesigner.BatchForm")!, source, "submessages.sdkrfilereader.properties")!;
            batch.ShowInTaskbar = false;
            batch.Opacity = 0;
            batch.Show();
            var logs = Field<List<LogEntry>>(batch, "logs");
            logs.AddRange(LogFile.Read(Path.Combine(root, "examples/submessages.log")));
            var run = (Task)batch.GetType().GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(batch, null)!;
            PumpUntil(() => run.IsCompleted);
            run.GetAwaiter().GetResult();
            if (Field<ParserResult?[]>(batch, "results").Count(r => r?.Success == true) != 4)
                throw new Exception("Batch UI must report four matches");
            PumpUntil(() => Field<object?>(batch, "selectedResult") is not null);
            var result = Field<ParserResult>(batch, "selectedResult");
            if (!result.Success || result.Route != "submessage[0].pattern[0]")
                throw new Exception("Wrong initial UI selection");
            var text = Field<RichTextBox>(batch, "message");
            text.Select(text.Text.IndexOf("alice", StringComparison.Ordinal), 1);
            Color aliceColor = text.SelectionBackColor;
            text.Select(text.Text.IndexOf("success", StringComparison.Ordinal), 1);
            if (text.SelectionBackColor == aliceColor)
                throw new Exception("Submessage fields need distinct colors");
            var captureGrid = Field<DataGridView>(batch, "captures");
            var aliceRow = captureGrid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is ParserCapture { Name: "event.sourceUserName" });
            if (aliceRow.Cells[1].Style.BackColor != aliceColor)
                throw new Exception("Submessage color legend mismatch");
            Capture(batch, Path.Combine(output, "ui-batch.png"));
            Size largeBatch = batch.Size;
            batch.Size = batch.MinimumSize;
            Application.DoEvents();
            foreach (var flow in Descendants(batch).OfType<FlowLayoutPanel>())
                if (flow.Controls.Cast<Control>().Any(c => c.Bottom > flow.ClientSize.Height || c.Right > flow.ClientSize.Width))
                    throw new Exception("Batch actions clipped at minimum size");
            Capture(batch, Path.Combine(output, "ui-batch-compact.png"));
            batch.Size = largeBatch;
            Application.DoEvents();
            var filter = Field<ComboBox>(batch, "filter");
            filter.SelectedIndex = 2;
            if (Field<DataGridView>(batch, "list").RowCount != 2)
                throw new Exception("Problem filter should show two logs");
            PumpUntil(() => Field<ParserResult?>(batch, "selectedResult")?.State == ParseState.SubmessageMismatch);
            filter.SelectedIndex = 3;
            Application.DoEvents();
            if (Field<DataGridView>(batch, "list").RowCount != 0 || text.TextLength != 0)
                throw new Exception("Empty filter did not clear detail");
            filter.SelectedIndex = 0;
            Field<DataGridView>(batch, "list").CurrentCell = Field<DataGridView>(batch, "list").Rows[3].Cells[0];
            var applyParser = (Task)batch.GetType().GetMethod("ApplyParserAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(batch, [editedSource])!;
            PumpUntil(() => applyParser.IsCompleted);
            applyParser.GetAwaiter().GetResult();
            PumpUntil(() => Field<ParserResult?>(batch, "selectedResult") is not null);
            if (Field<ParserResult?>(batch, "selectedResult")?.UsedPattern != 2)
                throw new Exception("Wrong retest detail pattern");
            if (Field<DataGridView>(batch, "list").CurrentCell!.RowIndex != 3)
                throw new Exception("Retest lost selected log");
            if (Field<ParserResult?[]>(batch, "results").Count(r => r?.Success == true) != 5)
                throw new Exception("Retest did not use new pattern");
            if ((string)batch.GetType().GetProperty("ConfigurationSource")!.GetValue(batch)! != editedSource)
                throw new Exception("Edited config not returned");
            Capture(batch, Path.Combine(output, "ui-batch-edited.png"));
            batch.Close();
            using var tokenDialog = (Form)Activator.CreateInstance(assembly.GetType("ParserDesigner.TokenDialog")!,
                "token.count=1\ntoken[0].name=Message", @"(\d+)(?:-)(?<Kind>[A-Z]+)", "404-ERROR")!;
            tokenDialog.ShowInTaskbar = false;
            tokenDialog.Opacity = 0;
            tokenDialog.Show();
            Application.DoEvents();
            var tokenTable = tokenDialog.Controls.OfType<DataGridView>().Single();
            PumpUntil(() => Convert.ToString(tokenTable.Rows[0].Cells[4].Value) == "404");
            if (Convert.ToString(tokenTable.Rows[1].Cells[4].Value) != "ERROR" || !tokenTable.Columns[4].ReadOnly)
                throw new Exception("Token snippet must show the captured text in a read-only column");
            if (tokenTable.Rows.Count != 2)
                throw new Exception("Token preview count incorrect");
            if (tokenTable.Columns[2] is not DataGridViewComboBoxColumn typeColumn || !typeColumn.Items.Contains("RegexToken"))
                throw new Exception("Token type dropdown missing");
            tokenTable.Rows[1].Cells[2].Value = "TimeStamp";
            if (Convert.ToString(tokenTable.Rows[1].Cells[3].Value) != "yyyy-MM-dd HH:mm:ss")
                throw new Exception("Timestamp format suggestion missing");
            tokenTable.Rows[1].Cells[3].Value = "yyyy-MM-dd'T'HH:mm:ss.SSSZ";
            tokenTable.Rows[1].Cells[2].Value = "Date";
            if (Convert.ToString(tokenTable.Rows[1].Cells[3].Value) != "yyyy-MM-dd'T'HH:mm:ss.SSSZ")
                throw new Exception("Changing type overwrote custom format");
            tokenTable.Rows[1].Cells[2].Value = "TimeStamp";
            tokenTable.Rows[0].Cells[1].Value = "Message";
            Capture(tokenDialog, Path.Combine(output, "ui-tokens.png"));
            tokenDialog.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().First().PerformClick();
            string generated = (string)tokenDialog.GetType().GetProperty("ResultSource")!.GetValue(tokenDialog)!;
            if (new PropertiesDocument(generated).Get("token.count") != "2")
                throw new Exception("Token dialog did not apply");
            if (new PropertiesDocument(generated).Get("token[1].format") != "yyyy-MM-dd'T'HH:mm:ss.SSSZ")
                throw new Exception("Token format export changed syntax");
            if (char.IsWhiteSpace(generated[0]) || generated.Contains("\r\n\r\n", StringComparison.Ordinal))
                throw new Exception("Generated configuration has extra leading or blank lines");
            tokenDialog.Close();
            foreach (var sampleCase in new[]
            {
                (Pattern: "(a)?()", Input: "", First: "— bez záchytu", Second: "(prázdný záchyt)"),
                (Pattern: @"(\d)+(x)?", Input: "123", First: "3", Second: "— bez záchytu"),
                (Pattern: "(a)(b)", Input: "xyz", First: "— bez záchytu", Second: "— bez záchytu")
            })
            {
                using var snippetDialog = (Form)Activator.CreateInstance(assembly.GetType("ParserDesigner.TokenDialog")!, "", sampleCase.Pattern, sampleCase.Input)!;
                snippetDialog.ShowInTaskbar = false;
                snippetDialog.Opacity = 0;
                snippetDialog.Show();
                var snippetTable = snippetDialog.Controls.OfType<DataGridView>().Single();
                PumpUntil(() => Convert.ToString(snippetTable.Rows[0].Cells[4].Value) != "Načítám…");
                if (Convert.ToString(snippetTable.Rows[0].Cells[4].Value) != sampleCase.First || Convert.ToString(snippetTable.Rows[1].Cells[4].Value) != sampleCase.Second)
                    throw new Exception("Snippet must distinguish missing, empty and repeated captures");
                snippetDialog.Close();
            }
            using var formatDialog = (Form)Activator.CreateInstance(assembly.GetType("ParserDesigner.TokenFormatDialog")!, "TimeStamp", "yyyy-MM-dd")!;
            formatDialog.ShowInTaskbar = false;
            formatDialog.Opacity = 0;
            formatDialog.Show();
            Application.DoEvents();
            var formatEdit = Field<TextBox>(formatDialog, "format");
            formatEdit.Select(formatEdit.TextLength, 0);
            Descendants(formatDialog).OfType<Button>().Single(b => b.Text == "HH — Hodina 0–23").PerformClick();
            if (formatEdit.Text != "yyyy-MM-ddHH")
                throw new Exception("Format builder must insert at caret");
            Shortcut(formatEdit, Keys.Control | Keys.Z);
            if (formatEdit.Text != "yyyy-MM-dd")
                throw new Exception("Format builder undo failed");
            var presets = Descendants(formatDialog).OfType<ComboBox>().Single();
            presets.SelectedItem = "yyyy-MM-dd'T'HH:mm:ssZ";
            if (formatEdit.Text != "yyyy-MM-dd'T'HH:mm:ssZ")
                throw new Exception("Format preset not selected");
            Capture(formatDialog, Path.Combine(output, "ui-token-format.png"));
            Descendants(formatDialog).OfType<Button>().Single(b => b.Text == "Použít formát").PerformClick();
            if ((string)formatDialog.GetType().GetProperty("ResultFormat")!.GetValue(formatDialog)! != "yyyy-MM-dd'T'HH:mm:ssZ")
                throw new Exception("Format builder result incorrect");
            formatDialog.Close();
            Console.WriteLine("PASS UI startup, highlights, submessages, filters, token generation and screenshots.");
            string mappingSource = "token.count=1\ntoken[0].name=Msg\ntoken[0].type=String\nevent.message=Msg\nevent.deviceVendor=__stringConstant(\"Old\")\ncustom.keep=true";
            using var mapping = (Form)Activator.CreateInstance(assembly.GetType("ParserDesigner.EventMappingDialog")!, mappingSource)!;
            mapping.ShowInTaskbar = false;
            mapping.Opacity = 0;
            mapping.Show();
            Application.DoEvents();
            var mappingTable = Field<DataGridView>(mapping, "table");
            var mappingTarget = Field<ComboBox>(mapping, "target");
            var mappingMode = Field<ComboBox>(mapping, "mode");
            var mappingValue = Field<ComboBox>(mapping, "value");
            if (mappingTable.Rows.Count != 2 || !mappingValue.Items.Contains("Msg"))
                throw new Exception("Existing mapping or token choices missing");
            mappingTarget.Text = "event.name";
            mappingMode.SelectedIndex = 1;
            mappingValue.Text = "Přihlášení";
            Descendants(mapping).OfType<Button>().Single(b => b.Text == "Přidat / aktualizovat").PerformClick();
            mappingTarget.Text = "event.deviceVendor";
            mappingValue.Text = "New";
            Descendants(mapping).OfType<Button>().Single(b => b.Text == "Přidat / aktualizovat").PerformClick();
            if (mappingTable.Rows.Count != 3)
                throw new Exception("Updating existing field created duplicate");
            mappingTable.Rows[0].Cells[1].Value = "__customOperation(Msg)";
            Capture(mapping, Path.Combine(output, "ui-event-mapping.png"));
            mapping.Size = mapping.MinimumSize;
            Application.DoEvents();
            foreach (var flow in Descendants(mapping).OfType<FlowLayoutPanel>())
                if (flow.Controls.Cast<Control>().Any(c => c.Bottom > flow.ClientSize.Height || c.Right > flow.ClientSize.Width))
                    throw new Exception("Mapping actions clipped at minimum size");
            Capture(mapping, Path.Combine(output, "ui-event-mapping-compact.png"));
            Descendants(mapping).OfType<Button>().Single(b => b.Text == "Použít v konfiguraci").PerformClick();
            var mappedDoc = new PropertiesDocument((string)mapping.GetType().GetProperty("ResultSource")!.GetValue(mapping)!);
            if (mappedDoc.Get("event.name") != "__stringConstant(\"Přihlášení\")" || mappedDoc.Get("event.deviceVendor") != "__stringConstant(\"New\")" || mappedDoc.Get("event.message") != "__customOperation(Msg)" || mappedDoc.Get("custom.keep") != "true")
                throw new Exception("Mapping editor export failed");
            mapping.Close();
            Console.WriteLine("PASS event mapping editor, constants, updates and export.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
