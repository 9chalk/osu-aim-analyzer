namespace OsuAimAnalyzer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            var paths = AppPaths.Create();
            Directory.CreateDirectory(paths.DataDirectory);

            Application.ThreadException += (_, e) => ReportCrash(paths.DataDirectory, e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) ReportCrash(paths.DataDirectory, ex);
            };

            var settings = AppSettings.Load(paths.SettingsPath);
            settings.FillDefaults();
            settings.Save(paths.SettingsPath);

            using var database = new AnalyzerDatabase(paths.DatabasePath);
            database.Initialize();

            Application.Run(new MainForm(paths, settings, database));
        }
        catch (Exception ex)
        {
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuAimAnalyzer");
            ReportCrash(fallback, ex);
        }
    }

    private static void ReportCrash(string directory, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string log = Path.Combine(directory, "crash.log");
            File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\r\n{ex}\r\n\r\n");
            MessageBox.Show($"osu! Aim Analyzer hit an error.\n\n{ex.Message}\n\nA crash log was written to:\n{log}",
                "osu! Aim Analyzer error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            try { MessageBox.Show(ex.ToString(), "osu! Aim Analyzer error", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
        }
    }
}
