using System.Reflection;
using System.Runtime.ExceptionServices;
using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public class InspectorLayoutTests
{
    [Fact]
    public void PracticeExport_MainFormPublishesExplicitSubsetAndEnablesOpen()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var files = new PracticeExportTests();
                using var db = new AnalyzerDatabase(":memory:"); db.Initialize();
                string root = Path.GetDirectoryName(files.OutputPath)!;
                var paths = new AppPaths { DataDirectory = root, SettingsPath = Path.Combine(root, "settings"), DatabasePath = ":memory:", CollectionStatePath = Path.Combine(root, "collections") };
                using var main = new MainForm(paths, new AppSettings(), db);
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(MainForm).GetField("inspectorPlayId", flags)!.SetValue(main, (long?)1);
                var control = (PracticePreviewControl)typeof(MainForm).GetField("playInspectorPractice", flags)!.GetValue(main)!;
                using var host = new Form { ClientSize = new Size(650, 720), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
                host.Controls.Add(control); host.Show();
                control.Reset("Synthetic export");
                var series = files.Preview;
                using var choice = new PracticeExportSelectionForm(series);
                Assert.Single(choice.SelectedVariants);
                Assert.Equal("Reduced spacing", choice.SelectedVariants[0].Name);
                control.ShowPreview(series, PracticePreviewControl.FormatPreview(series));
                var work = (Task)typeof(MainForm).GetMethod("ExportPracticeToPathAsync", flags)!.Invoke(main, new object[] { series, choice.SelectedVariants, files.OutputPath })!;
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                while (!work.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(1); }
                Assert.True(work.IsCompletedSuccessfully, work.Exception?.ToString());
                Application.DoEvents(); // queued progress must not overwrite the published result
                var readout = control.Controls.OfType<TextBox>().Single();
                Assert.Contains("Exported 1", readout.Text);
                Assert.Contains("Slowdown variants were not included", readout.Text);
                var open = control.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == "Open package");
                Assert.True(open.Enabled);
                Assert.True(File.Exists(files.OutputPath));
                Assert.Empty(db.LoadPlays()); // exporting does not add or mutate analysis history
                host.Close();
                ((IDisposable)typeof(MainForm).GetField("collectionService", flags)!.GetValue(main)!).Dispose();
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Export UI test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void PracticePreview_MainFormWorkerCompletesRetriesAndDiscardsCanceledSelection()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "preview-ui-" + Guid.NewGuid() + ".osu");
            try
            {
                var document = BeatmapDocument.Parse(BeatmapDocumentTests.Map);
                File.WriteAllBytes(path, document.ToBytes());
                var identity = PracticePlannerTests.Identity(document, path: path);
                using var db = new AnalyzerDatabase(":memory:");
                db.Initialize();
                long id = db.Save(new PlayAnalysis { Beatmap = new BeatmapData { Path = path, Title = "Synthetic preview" }, Replay = new ReplayData { ReplayHash = "preview-ui", BeatmapHash = identity.BeatmapHash, TimestampUtc = DateTime.UtcNow } });
                var paths = new AppPaths { DataDirectory = Path.GetTempPath(), SettingsPath = path + ".settings", DatabasePath = ":memory:", CollectionStatePath = path + ".collections" };
                using var main = new MainForm(paths, new AppSettings(), db);
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var pageHost = (Panel)typeof(MainForm).GetField("playInspectorPageHost", flags)!.GetValue(main)!;
                var control = (PracticePreviewControl)typeof(MainForm).GetField("playInspectorPractice", flags)!.GetValue(main)!;
                var readout = control.Controls.OfType<TextBox>().Single();
                using var host = new Form { ClientSize = new Size(650, 720), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
                host.Controls.Add(pageHost.Parent!.Parent!);
                host.Show();
                typeof(MainForm).GetField("inspectorPlayId", flags)!.SetValue(main, (long?)id);
                control.Reset("Synthetic preview");
                typeof(MainForm).GetMethod("ShowInspectorPage", flags)!.Invoke(main, new object[] { "Practice" });
                Task Start() => (Task)typeof(MainForm).GetMethod("BuildPracticePreviewAsync", flags)!.Invoke(main, new object[] { PracticePitchPolicy.PreservePitch })!;
                void Pump(Task work)
                {
                    var timeout = System.Diagnostics.Stopwatch.StartNew();
                    while (!work.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(1); }
                    Assert.True(work.IsCompleted, "Preview worker did not finish.");
                    if (work.Exception is { } error) ExceptionDispatchInfo.Capture(error).Throw();
                    Assert.True(work.IsCompletedSuccessfully);
                }
                var first = Start();
                Assert.Contains("Reading the selected map", readout.Text);
                bool uiTick = false;
                host.BeginInvoke((Action)(() => uiTick = true));
                Pump(first);
                Assert.True(uiTick);
                Assert.Contains("PRACTICE PREVIEW · 4 variants", readout.Text);
                Assert.Equal(document.ToBytes(), File.ReadAllBytes(path));
                File.AppendAllText(path, "\n// changed");
                Pump(Start());
                Assert.Contains("has changed", readout.Text);
                File.WriteAllBytes(path, document.ToBytes());
                Pump(Start());
                Assert.Contains("PRACTICE PREVIEW", readout.Text);
                var canceled = Start();
                typeof(MainForm).GetMethod("ShowPlayInspector", flags)!.Invoke(main, new object?[] { null, false });
                Pump(canceled);
                Assert.Contains("Select an analyzed play", readout.Text);
                Assert.Single(db.LoadPlays());
                host.Close();
                ((IDisposable)typeof(MainForm).GetField("collectionService", flags)!.GetValue(main)!).Dispose();
            }
            catch (Exception e) { failure = e; }
            finally { File.Delete(path); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void Inspector_SubpagesFillAvailableBodyAfterSwitchAndResize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "analyzer-layout-" + Guid.NewGuid());
                var paths = new AppPaths { DataDirectory = root, SettingsPath = Path.Combine(root, "settings.json"), DatabasePath = ":memory:", CollectionStatePath = Path.Combine(root, "collections.json") };
                using var db = new AnalyzerDatabase(":memory:");
                db.Initialize();
                using var main = new MainForm(paths, new AppSettings(), db);
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var pageHost = (Panel)typeof(MainForm).GetField("playInspectorPageHost", flags)!.GetValue(main)!;
                var pages = (Dictionary<string, Control>)typeof(MainForm).GetField("playInspectorPages", flags)!.GetValue(main)!;
                var switchPage = typeof(MainForm).GetMethod("ShowInspectorPage", flags)!;
                // Host only the inspector; never show MainForm or start ingestion/watchers.
                Control inspector = pageHost.Parent!.Parent!;
                using var host = new Form { ClientSize = new Size(650, 720), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
                host.Controls.Add(inspector);
                host.Show();
                foreach (var size in new[] { new Size(650, 720), new Size(900, 900), new Size(500, 500) })
                {
                    host.ClientSize = size;
                    foreach (string key in new[] { "Summary", "Training", "Compare", "Errors", "Diagnosis", "Top errors", "Practice" })
                    {
                        switchPage.Invoke(main, new object[] { key });
                        host.PerformLayout();
                        Assert.Equal(pageHost.ClientRectangle, pages[key].Bounds);
                        Assert.True(pageHost.Height >= size.Height - 24, "Readouts should use the full inner height.");
                        if (key == "Top errors") continue;
                        if (key == "Practice")
                        {
                            var practiceText = pages[key].Controls.OfType<TextBox>().Single();
                            Assert.True(practiceText.Width >= pageHost.Width - 24);
                            Assert.True(practiceText.Height >= pageHost.Height - 130);
                            Assert.Contains("Select an analyzed play", practiceText.Text);
                            continue;
                        }
                        var layout = Assert.IsType<TableLayoutPanel>(pages[key].Controls[0]);
                        var text = Assert.IsType<TextBox>(layout.GetControlFromPosition(0, 1));
                        Assert.True(text.Width >= pageHost.Width - 24, $"{key}: width {text.Width}/{pageHost.Width}");
                        Assert.True(text.Height >= pageHost.Height - 54, $"{key}: height {text.Height}/{pageHost.Height}");
                    }
                    switchPage.Invoke(main, new object[] { "Overview" });
                    Assert.True(pageHost.Height < size.Height - 150, "Overview should retain its play header.");
                }
                host.Close();
                ((IDisposable)typeof(MainForm).GetField("collectionService", flags)!.GetValue(main)!).Dispose();
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI layout thread timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
