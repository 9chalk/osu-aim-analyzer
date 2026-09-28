using System.Reflection;
using System.Runtime.ExceptionServices;
using OsuAimAnalyzer;

namespace OsuAimAnalyzer.Tests;

public class InspectorLayoutTests
{
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
                    foreach (string key in new[] { "Summary", "Training", "Compare", "Errors", "Diagnosis", "Top errors" })
                    {
                        switchPage.Invoke(main, new object[] { key });
                        host.PerformLayout();
                        Assert.Equal(pageHost.ClientRectangle, pages[key].Bounds);
                        Assert.True(pageHost.Height >= size.Height - 24, "Readouts should use the full inner height.");
                        if (key == "Top errors") continue;
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
