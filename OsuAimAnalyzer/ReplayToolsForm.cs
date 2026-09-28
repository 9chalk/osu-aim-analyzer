using System.Globalization;

namespace OsuAimAnalyzer;

public sealed class ReplayToolsForm : Form
{
    private readonly AppSettings settings;
    private readonly string settingsPath;
    private TuningConfig tuning;
    private readonly BeatmapResolver resolver;
    private readonly AimAnalyzer analyzer;

    private ReplayData? replay;
    private BeatmapData? map;
    private PlayAnalysis? analysis;
    private ReplayWorkspaceItem? currentItem;
    private double rawTunedProficiency;
    private double tunedProficiency;
    private PerformanceBreakdown? tunedPerformance;
    private List<ProficiencyBreakdown> tunedTransitions = new();
    private DifficultyWeightInfo[] difficultyWeights = Array.Empty<DifficultyWeightInfo>();
    private readonly Dictionary<string, ReplayWorkspaceItem> replayCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LocalReplayEntry> localReplayEntries = new();
    private readonly List<QuickCheckResult> quickResults = new();
    private BeatmapData? quickMap;
    private string quickMapPath = "";
    private string quickMapHash = "";

    private readonly TextBox osuPath = PathBox();
    private readonly TextBox songsPath = PathBox();
    private readonly TextBox replayPath = PathBox();
    private readonly CheckBox watchLatest = DarkCheck("Auto-load newest replay");
    private readonly CheckBox pureJumps = DarkCheck("Pure circle→circle only");
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Text = "Starting…" };

    private readonly Label mapLabel = BigValue(11);
    private readonly Label originalProf = BigValue();
    private readonly Label tunedProf = BigValue();
    private readonly Label originalPerf = BigValue();
    private readonly Label tunedPerf = BigValue();
    private readonly Label demandLabel = BigValue(12);
    private readonly DataGridView transitionGrid = new();
    private readonly DataGridView cacheGrid = new();
    private readonly DataGridView pcReplayGrid = new();
    private readonly ComboBox pcReplayRange = Combo("Latest 500", "7 days", "30 days", "All");
    private readonly Label cacheSummary = new() { AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(8, 7, 0, 0), Text = "0 replays cached" };

    private readonly Label quickMapLabel = new() { Dock = DockStyle.Fill, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 11f), TextAlign = ContentAlignment.MiddleLeft, Text = "No song-select map detected." };
    private readonly Label quickMapMeta = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9f), TextAlign = ContentAlignment.MiddleLeft, Text = "Open osu!stable song select and highlight a difficulty." };
    private readonly ComboBox quickMods = Combo("NM", "HD", "HR", "DT", "NC", "HDDT", "HDNC", "HRDT", "HRNC");
    private readonly NumericUpDown quickProfMin = Num(300, 1000, 600, 0);
    private readonly NumericUpDown quickProfMax = Num(300, 1000, 950, 0);
    private readonly NumericUpDown quickProfStep = Num(5, 200, 50, 0);
    private readonly TextBox quickMisses = new() { Width = 110, Text = "0,1,3,5", BackColor = Theme.Panel2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
    private readonly CheckBox quickAutoDetect = DarkCheck("Auto-detect song select");
    private readonly DataGridView quickGrid = new();
    private readonly Label quickSummary = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9.2f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly System.Windows.Forms.Timer quickTimer = new() { Interval = 1400 };
    private TabControl? mainTabs;
    private bool quickDetectBusy;

    private readonly DataGridView profBreakdown = new();
    private readonly DataGridView perfBreakdown = new();
    private readonly DebugPathControl debugPath = new() { Dock = DockStyle.Fill };
    private readonly DataGridView debugMetricGrid = new();
    private readonly NumericUpDown transitionPicker = Num(0, 100000, 0, 0);
    private readonly Label transitionCaption = new() { AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 10f), Padding = new Padding(4, 6, 8, 0) };

    private readonly Dictionary<string, NumericUpDown> editors = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? replayWatcher;
    private string? pendingWatchPath;
    private string? loadedReplayPath;
    private string? loadedReplaySignature;
    private CancellationTokenSource? replayLoadCts;
    private int replayLoadGeneration;
    private bool busy;
    private bool loadingEditors;

    public ReplayToolsForm(AppSettings settings, string settingsPath, BeatmapResolver resolver)
    {
        this.settings = settings;
        this.settingsPath = settingsPath;
        tuning = ProductionScoring.CreateProfile();
        this.resolver = resolver;
        analyzer = new AimAnalyzer(settings);

        Text = "Replay Tools · production scoring diagnostics";
        Width = 1450; Height = 930; MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        Controls.Add(BuildUi());
        Shown += async (_, _) => await InitializeAsync();
        FormClosed += (_, _) =>
        {
            replayWatcher?.Dispose();
            quickTimer.Stop();
            replayLoadCts?.Cancel();
            replayLoadCts?.Dispose();
            try { PullPathSettings(); } catch { }
        };
    }

    private Control BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var tabs = ToolkitUi.Tabs();
        mainTabs = tabs;
        tabs.ItemSize = new Size(180, 36);

        var replayTab = Page("Replay test"); replayTab.Controls.Add(BuildReplayTab());
        var quickTab = Page("Quick checker"); quickTab.Controls.Add(BuildQuickCheckerTab());
        var debugTab = Page("Debug visualizer"); debugTab.Controls.Add(BuildDebugTab());
        tabs.TabPages.Add(replayTab); tabs.TabPages.Add(quickTab); tabs.TabPages.Add(debugTab);
        root.Controls.Add(tabs, 0, 0);
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(10, 0, 10, 0) };
        bar.Controls.Add(status);
        root.Controls.Add(bar, 0, 1);
        return root;
    }

    private Control BuildReplayTab()
    {
        ConfigureTransitionGrid();
        ConfigureCacheGrid();
        ConfigurePcReplayGrid();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Background, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildSourcePanel(), 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 7, BackColor = Theme.Border };
        split.Panel1.Controls.Add(BuildReplayBrowser());
        split.Panel2.Controls.Add(BuildCurrentReplayPane());
        split.Resize += (_, _) =>
        {
            if (split.Width > 1000)
            {
                int desired = Math.Clamp((int)(split.Width * .38), 280, Math.Max(281, split.Width - 500 - split.SplitterWidth));
                ToolkitUi.SetSplitterDistanceSafe(split, desired);
            }
        };
        root.Controls.Add(split, 0, 1);
        return root;
    }

    private Control BuildReplayBrowser()
    {
        var tabs = ToolkitUi.Tabs();
        tabs.ItemSize = new Size(160, 34);

        var cached = Page("Cached test set");
        var cachedLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Theme.Background, Padding = new Padding(4) };
        cachedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        cachedLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var cachedActions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 6, 4, 3) };
        var remove = ToolkitUi.Button("Remove selected"); remove.Click += (_, _) => RemoveSelectedCached();
        var clear = ToolkitUi.Button("Clear cache"); clear.Click += (_, _) => { replayCache.Clear(); currentItem = null; RefreshCacheGrid(); ClearLoadedReplayStateForNewRequest(""); };
        cachedActions.Controls.Add(remove); cachedActions.Controls.Add(clear); cachedActions.Controls.Add(cacheSummary);
        cachedLayout.Controls.Add(cachedActions, 0, 0);
        cachedLayout.Controls.Add(ToolkitUi.Wrap(cacheGrid, "Imported / analyzed replays · click to switch instantly"), 0, 1);
        cached.Controls.Add(cachedLayout);

        var pc = Page("PC replays");
        var pcLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Theme.Background, Padding = new Padding(4) };
        pcLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        pcLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var pcActions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 6, 4, 3) };
        pcReplayRange.Width = 105;
        pcReplayRange.SelectedIndexChanged += async (_, _) => { if (IsHandleCreated) await RefreshPcReplayTableAsync(); };
        var refresh = ToolkitUi.Button("Refresh list"); refresh.Click += async (_, _) => await RefreshPcReplayTableAsync();
        var analyze = ToolkitUi.Button("Analyze selected"); analyze.Click += async (_, _) => await AnalyzeSelectedPcReplaysAsync();
        pcActions.Controls.Add(new Label { Text = "Show", AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(0, 8, 2, 0) });
        pcActions.Controls.Add(pcReplayRange); pcActions.Controls.Add(refresh); pcActions.Controls.Add(analyze);
        pcLayout.Controls.Add(pcActions, 0, 0);
        pcLayout.Controls.Add(ToolkitUi.Wrap(pcReplayGrid, "Local Data\\r replay headers · double-click to analyze"), 0, 1);
        pc.Controls.Add(pcLayout);

        tabs.TabPages.Add(cached);
        tabs.TabPages.Add(pc);
        return tabs;
    }

    private Control BuildCurrentReplayPane()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildScoreCards(), 0, 0);
        var summary = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(14, 8, 14, 8) };
        summary.Controls.Add(mapLabel);
        root.Controls.Add(summary, 0, 1);
        root.Controls.Add(ToolkitUi.Wrap(transitionGrid, "Every analyzed jump · difficulty-weighted · click one to send it to Debug"), 0, 2);
        return root;
    }

    private Control BuildSourcePanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, BackColor = Theme.Panel, Padding = new Padding(12, 9, 12, 8) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        osuPath.Text = settings.OsuDirectory; songsPath.Text = settings.SongsDirectory; replayPath.Text = settings.ReplayDirectory;
        AddPath(panel, 0, "osu!", osuPath); AddPath(panel, 2, "Songs", songsPath); AddPath(panel, 4, "Replays", replayPath);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(0, 6, 0, 0) };
        var latest = ToolkitUi.Button("Load latest replay"); latest.Click += async (_, _) => await LoadLatestAsync();
        var manual = ToolkitUi.Button("Import replay(s)…"); manual.Click += async (_, _) => await OpenReplayAsync();
        var reindex = ToolkitUi.Button("Reindex maps"); reindex.Click += async (_, _) => await ReindexAsync();
        watchLatest.Checked = false; watchLatest.CheckedChanged += (_, _) => ConfigureWatcher();
        pureJumps.Checked = settings.AnalyzeOnlyPureJumps;
        pureJumps.CheckedChanged += async (_, _) => { settings.AnalyzeOnlyPureJumps = pureJumps.Checked; SaveSettings(); if (replay != null && map != null) await ReanalyzeCurrentAsync(); };
        actions.Controls.Add(latest); actions.Controls.Add(manual); actions.Controls.Add(reindex); actions.Controls.Add(watchLatest); actions.Controls.Add(pureJumps);
        panel.Controls.Add(actions, 0, 1); panel.SetColumnSpan(actions, 6);
        return panel;
    }

    private Control BuildScoreCards()
    {
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, BackColor = Theme.Background, Padding = new Padding(0, 7, 0, 7) };
        for (int i = 0; i < 5; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        cards.Controls.Add(Card("LEGACY PROFICIENCY", originalProf), 0, 0);
        cards.Controls.Add(Card("CURRENT PROFICIENCY", tunedProf), 1, 0);
        cards.Controls.Add(Card("LEGACY AIM PERFORMANCE", originalPerf), 2, 0);
        cards.Controls.Add(Card("CURRENT AIM PERFORMANCE", tunedPerf), 3, 0);
        cards.Controls.Add(Card("MAP DEMAND", demandLabel), 4, 0);
        return cards;
    }

    private Control BuildQuickCheckerTab()
    {
        ConfigureQuickGrid();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Background, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var selected = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Panel, Padding = new Padding(14, 8, 10, 8) };
        selected.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selected.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
        selected.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        selected.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        selected.Controls.Add(quickMapLabel, 0, 0);
        selected.Controls.Add(quickMapMeta, 0, 1);
        var mapActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = Theme.Panel, Padding = new Padding(0, 5, 0, 0) };
        var detect = ToolkitUi.Button("Detect selected map"); detect.Click += async (_, _) => await DetectSelectedMapAsync(false);
        var choose = ToolkitUi.Button("Choose .osu…"); choose.Click += async (_, _) => await ChooseQuickMapAsync();
        var install = ToolkitUi.Button("Install song-select reader"); install.Click += (_, _) => InstallSongSelectReader();
        quickAutoDetect.Checked = true;
        quickAutoDetect.CheckedChanged += (_, _) => { if (quickAutoDetect.Checked) quickTimer.Start(); else quickTimer.Stop(); };
        mapActions.Controls.Add(detect); mapActions.Controls.Add(choose); mapActions.Controls.Add(install); mapActions.Controls.Add(quickAutoDetect);
        selected.Controls.Add(mapActions, 1, 0); selected.SetRowSpan(mapActions, 2);
        root.Controls.Add(selected, 0, 0);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 11, 8, 7) };
        quickMods.Width = 88;
        quickProfMin.Width = quickProfMax.Width = quickProfStep.Width = 66;
        controls.Controls.Add(LabelForQuick("Mods")); controls.Controls.Add(quickMods);
        controls.Controls.Add(LabelForQuick("Raw prof")); controls.Controls.Add(quickProfMin);
        controls.Controls.Add(LabelForQuick("to")); controls.Controls.Add(quickProfMax);
        controls.Controls.Add(LabelForQuick("step")); controls.Controls.Add(quickProfStep);
        controls.Controls.Add(LabelForQuick("Misses")); controls.Controls.Add(quickMisses);
        var run = ToolkitUi.Button("Run sweep"); run.Click += (_, _) => RunQuickSweep();
        var saveSelected = ToolkitUi.Button("Save selected tests"); saveSelected.Click += (_, _) => SaveQuickTests(false);
        var saveAll = ToolkitUi.Button("Save all tests"); saveAll.Click += (_, _) => SaveQuickTests(true);
        controls.Controls.Add(run); controls.Controls.Add(saveSelected); controls.Controls.Add(saveAll);
        root.Controls.Add(controls, 0, 1);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 7, BackColor = Theme.Border };
        split.Panel1.Controls.Add(ToolkitUi.Wrap(quickGrid, "Synthetic score sweep · no top-player replay required"));
        split.Panel2.Controls.Add(ToolkitUi.Wrap(quickSummary, "Selected-map stress-test summary"));
        split.Resize += (_, _) => { if (split.Height > 500) ToolkitUi.SetSplitterDistanceSafe(split, Math.Max(260, (int)(split.Height * .78))); };
        root.Controls.Add(split, 0, 2);

        quickTimer.Tick += async (_, _) =>
        {
            if (!quickAutoDetect.Checked || mainTabs?.SelectedTab?.Text != "Quick checker") return;
            await DetectSelectedMapAsync(true);
        };
        quickTimer.Start();
        return root;
    }

    private void ConfigureQuickGrid()
    {
        ToolkitUi.StyleDataGrid(quickGrid);
        quickGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        quickGrid.MultiSelect = true;
        quickGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        string[] names = { "Raw prof", "Final prof", "Miss", "Aim performance", "Base aim", "Cons ×", "Hard exec", "★", "AR", "Hard BPM", "Hard spacing", "Active sec" };
        int[] widths = { 72, 82, 48, 105, 78, 68, 76, 64, 58, 76, 92, 74 };
        for (int i = 0; i < names.Length; i++) { quickGrid.Columns.Add("q" + i, names[i]); quickGrid.Columns[i].Width = widths[i]; }
    }

    private static Label LabelForQuick(string text) => new() { Text = text, AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(3, 7, 2, 0), Margin = new Padding(2, 0, 2, 0) };

    private async Task DetectSelectedMapAsync(bool silent)
    {
        if (quickDetectBusy) return;
        if (!SelectedBeatmapReader.IsInstalled())
        {
            if (!silent) SetStatus("Song-select reader is not installed. Use Install song-select reader, or choose a .osu file manually.");
            return;
        }
        quickDetectBusy = true;
        try
        {
            PullPathSettings();
            var info = await SelectedBeatmapReader.TryReadAsync(settings.SongsDirectory);
            if (info == null) { if (!silent) SetStatus("Could not read a selected osu!stable difficulty."); return; }
            if (Path.GetFullPath(info.FullPath).Equals(quickMapPath, StringComparison.OrdinalIgnoreCase)) return;
            await LoadQuickMapAsync(info.FullPath, silent);
        }
        catch (Exception ex) { if (!silent) SetStatus("Song-select detection failed: " + ex.Message); }
        finally { quickDetectBusy = false; }
    }

    private async Task ChooseQuickMapAsync()
    {
        using var d = new OpenFileDialog { Filter = "osu! beatmap (*.osu)|*.osu", Multiselect = false };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        await LoadQuickMapAsync(d.FileName, false);
    }

    private async Task LoadQuickMapAsync(string path, bool silent)
    {
        if (!File.Exists(path)) return;
        try
        {
            string hash = await Task.Run(() => SelectedBeatmapReader.Md5(path));
            var m = resolver.Resolve(hash, refreshDbOnMiss: true);
            if (m == null)
            {
                m = BeatmapParser.Parse(path);
                m.Hash = hash;
            }
            quickMap = m; quickMapPath = path; quickMapHash = hash;
            quickMapLabel.Text = m.DisplayName;
            quickMapMeta.Text = $"Selected in osu! · AR {m.AR:0.0} · CS {m.CS:0.0} · {m.HitObjects.Count:N0} objects · hash {ShortHash(hash)}";
            quickResults.Clear(); quickGrid.Rows.Clear();
            quickSummary.Text = "Choose mods / proficiency / miss ranges, then Run sweep.";
            if (!silent) SetStatus("Quick Checker captured: " + m.DisplayName);
        }
        catch (Exception ex) { if (!silent) SetStatus("Could not load selected map: " + ex.Message); }
    }

    private void InstallSongSelectReader()
    {
        try
        {
            SelectedBeatmapReader.LaunchInstaller();
            SetStatus("Song-select reader installer opened. When it finishes, press Detect selected map.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not start reader setup", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void RunQuickSweep()
    {
        PullEditors();
        if (quickMap == null) { SetStatus("Quick Checker needs a selected map first."); return; }
        int mods = ParseQuickMods(quickMods.SelectedItem?.ToString() ?? "NM");
        double lo = (double)quickProfMin.Value, hi = (double)quickProfMax.Value, step = Math.Max(1, (double)quickProfStep.Value);
        if (hi < lo) (lo, hi) = (hi, lo);
        var misses = ParseMissList(quickMisses.Text);
        if (misses.Count == 0) misses.Add(0);

        quickResults.Clear(); quickGrid.Rows.Clear();
        for (double prof = lo; prof <= hi + .001; prof += step)
            foreach (int miss in misses)
                quickResults.Add(QuickCheckEngine.Simulate(quickMap, mods, prof, miss, tuning));
        RenderQuickResults();
        SetStatus($"Quick Checker finished · {quickResults.Count:N0} synthetic performance cases.");
    }

    private void RetuneQuickResults()
    {
        if (quickResults.Count == 0) return;
        foreach (var r in quickResults)
        {
            r.FinalProficiency = TuningScorer.RemapProficiency(r.InputRawProficiency, tuning);
            r.Performance = TuningScorer.Performance(r.Analysis, r.InputRawProficiency, tuning);
            r.AimPerformance = r.Performance.Score;
            r.BaseCapability = r.Performance.BaseCapabilityScore;
            r.ConsistencyMultiplier = r.Performance.ConsistencyMultiplier;
            r.HardExecution = r.Performance.HardExecutionProficiency;
            r.AimActiveSeconds = r.Performance.AimActiveSeconds;
        }
        RenderQuickResults();
    }

    private void RenderQuickResults()
    {
        quickGrid.Rows.Clear();
        foreach (var r in quickResults)
        {
            int row = quickGrid.Rows.Add(r.InputRawProficiency.ToString("0"), Humanize.TunedScoreShort(r.FinalProficiency, tuning), r.Misses,
                r.AimPerformance.ToString("0"), r.BaseCapability.ToString("0"), r.ConsistencyMultiplier.ToString("0.000×"),
                r.HardExecution.ToString("0"), r.Analysis.StarRating > 0 ? r.Analysis.StarRating.ToString("0.00") : "—",
                r.Analysis.EffectiveAr.ToString("0.0"), r.Performance.HardSectionBpm.ToString("0"), Humanize.Spacing(r.Performance.HardSectionSpacing, true), r.AimActiveSeconds.ToString("0"));
            quickGrid.Rows[row].Tag = r;
        }
        if (quickResults.Count == 0 || quickMap == null) return;
        var min = quickResults.MinBy(x => x.AimPerformance)!; var max = quickResults.MaxBy(x => x.AimPerformance)!;
        quickSummary.Text = $"{quickMap.DisplayName} · {ModUtils.ToShortString(max.Analysis.Replay.Mods)} · {quickResults.Count:N0} scenarios. Aim Performance spans {min.AimPerformance:0} → {max.AimPerformance:0}. " +
            $"Map demand: {max.Analysis.StarRating:0.00}★ ({max.Analysis.StarSource}), AR {max.Analysis.EffectiveAr:0.0}, ~{max.Performance.HardSectionBpm:0} hard-section BPM, {Humanize.Spacing(max.Performance.HardSectionSpacing, true)}, {max.AimActiveSeconds:0}s aim-active.";
    }

    private void SaveQuickTests(bool all)
    {
        var chosen = all
            ? quickResults.ToList()
            : quickGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as QuickCheckResult).Where(r => r != null).Select(r => r!).Distinct().ToList();
        if (chosen.Count == 0) { SetStatus("Select one or more synthetic test rows first."); return; }
        foreach (var result in chosen)
        {
            string key = $"synthetic:{quickMapHash}:{result.Analysis.Replay.Mods}:{result.InputRawProficiency:0.###}:{result.Misses}";
            var item = new ReplayWorkspaceItem
            {
                Key = key, Path = "[synthetic]", Signature = key, IsSynthetic = true,
                SyntheticLabel = $"SIM {result.InputRawProficiency:0} prof / {result.Misses} miss",
                Replay = result.Analysis.Replay, Beatmap = result.Analysis.Beatmap, Analysis = result.Analysis
            };
            item.Replay.TimestampUtc = DateTime.UtcNow;
            RetuneItem(item);
            replayCache[key] = item;
        }
        RefreshCacheGrid();
        SetStatus($"Saved {chosen.Count:N0} Quick Checker test case(s) to the main comparison table.");
    }

    private static List<int> ParseMissList(string text)
    {
        var result = new List<int>();
        foreach (var token in (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(token.Trim(), out int n) && n >= 0 && !result.Contains(n)) result.Add(n);
        result.Sort(); return result;
    }

    private static int ParseQuickMods(string s)
    {
        s = (s ?? "NM").ToUpperInvariant();
        int mods = 0;
        if (s.Contains("HD")) mods |= ModUtils.Hidden;
        if (s.Contains("HR")) mods |= ModUtils.HardRock;
        if (s.Contains("NC")) mods |= ModUtils.Nightcore | ModUtils.DoubleTime;
        else if (s.Contains("DT")) mods |= ModUtils.DoubleTime;
        if (s.Contains("HT")) mods |= ModUtils.HalfTime;
        return mods;
    }

    private Control BuildScoringTab()
    {
        ConfigureBreakdownGrids();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Background, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 9, 8, 5), WrapContents = false };
        var save = ToolkitUi.Button("Save tuning profile"); save.Click += (_, _) => { PullEditors(); tuning.Save(); SetStatus("Saved tuning profile."); };
        var reset = ToolkitUi.Button("Reset v20-ish defaults"); reset.Click += (_, _) => { tuning = TuningConfig.Defaults(); PushEditors(); Recalculate(); };
        var export = ToolkitUi.Button("Export profile…"); export.Click += (_, _) => ExportProfile();
        var import = ToolkitUi.Button("Import profile…"); import.Click += (_, _) => ImportProfile();
        top.Controls.Add(save); top.Controls.Add(reset); top.Controls.Add(export); top.Controls.Add(import);
        top.Controls.Add(new Label { Text = "Changes recalculate the loaded replay immediately.", AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(12, 8, 0, 0) });
        root.Controls.Add(top, 0, 0);

        var innerTabs = ToolkitUi.Tabs(); innerTabs.ItemSize = new Size(190, 34);
        var p = Page("Proficiency weights"); p.Controls.Add(BuildProficiencyLab());
        var a = Page("Aim performance weights"); a.Controls.Add(BuildPerformanceLab());
        innerTabs.TabPages.Add(p); innerTabs.TabPages.Add(a);
        root.Controls.Add(innerTabs, 0, 1);
        PushEditors();
        return root;
    }

    private Control BuildProficiencyLab()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 7, BackColor = Theme.Border };
        split.Panel1.Controls.Add(ToolkitUi.Wrap(BuildProfEditor(), "Live proficiency formula"));
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Theme.Background };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        right.Controls.Add(ToolkitUi.Wrap(profBreakdown, "Selected jump · component contribution"), 0, 0);
        right.Controls.Add(ToolkitUi.Wrap(BuildProfNotes(), "What the extra controls change"), 0, 1);
        split.Panel2.Controls.Add(right);
        split.Resize += (_, _) => { if (split.Width > 900) ToolkitUi.SetSplitterDistanceSafe(split, (int)(split.Width * .48)); };
        return split;
    }

    private Control BuildPerformanceLab()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 7, BackColor = Theme.Border };
        split.Panel1.Controls.Add(ToolkitUi.Wrap(BuildPerfEditor(), "Live aim-performance formula"));
        split.Panel2.Controls.Add(ToolkitUi.Wrap(perfBreakdown, "Current replay · demand contribution"));
        split.Resize += (_, _) => { if (split.Width > 900) ToolkitUi.SetSplitterDistanceSafe(split, (int)(split.Width * .52)); };
        return split;
    }

    private Control BuildProfEditor()
    {
        var host = EditorHost();
        AddSection(host, "TRANSITION COMPONENT WEIGHTS");
        AddEditor(host, "Landing / centering", nameof(TuningConfig.LandingWeight), .01m, 0, 3, 3, "How centered the cursor is at hit time.");
        AddEditor(host, "Arrival timing", nameof(TuningConfig.ArrivalWeight), .01m, 0, 3, 3, "Whether the cursor arrives and settles on time.");
        AddEditor(host, "Cursor straightness", nameof(TuningConfig.StraightnessWeight), .01m, 0, 3, 3, "Path efficiency and sideways curvature.");
        AddEditor(host, "Braking / deceleration", nameof(TuningConfig.DecelerationWeight), .01m, 0, 3, 3, "Smoothness of braking into the target.");
        AddEditor(host, "Stability / shake", nameof(TuningConfig.StabilityWeight), .01m, 0, 3, 3, "Post-arrival micro-corrections and wobble.");
        AddEditor(host, "Ideal computer path", nameof(TuningConfig.IdealPathWeight), .01m, 0, 3, 3, "Similarity to the synthetic minimum-jerk path.");
        AddEditor(host, "Context weight shifting", nameof(TuningConfig.ContextWeightStrength), .05m, 0, 3, 2, "0=fixed weights; 1=current BPM/spacing shifts; >1 exaggerates them.");
        AddSection(host, "PLAY AGGREGATION");
        AddEditor(host, "Mean", nameof(TuningConfig.MeanWeight), .01m, 0, 3, 3, "Weight of the average transition.");
        AddEditor(host, "Lower quartile", nameof(TuningConfig.LowerQuartileWeight), .01m, 0, 3, 3, "Punishes sustained weaker sections.");
        AddEditor(host, "Worst decile", nameof(TuningConfig.WorstDecileWeight), .01m, 0, 3, 3, "Punishes the worst 10% of aim transitions.");
        AddSection(host, "FINAL PROFICIENCY RESOLUTION CURVE");
        AddEditor(host, "Resolution strength", nameof(TuningConfig.ProficiencyResolutionStrength), .05m, 0, 1, 2, "0=old linear 0-1000 scale; 1=fully use the focus-band remap below.");
        AddEditor(host, "Focus band raw start", nameof(TuningConfig.ProficiencyFocusRawStart), 5m, 400, 900, 0, "Pre-remap proficiency where the high-resolution useful band begins.");
        AddEditor(host, "Map raw start to", nameof(TuningConfig.ProficiencyFocusMappedStart), 5m, 0, 900, 0, "Displayed score assigned to the raw focus-band start. Default: old 750 becomes 550.");
        AddEditor(host, "Focus band raw end", nameof(TuningConfig.ProficiencyFocusRawEnd), 5m, 600, 990, 0, "Pre-remap proficiency where the high-resolution band ends.");
        AddEditor(host, "Map raw end to", nameof(TuningConfig.ProficiencyFocusMappedEnd), 5m, 100, 999, 0, "Displayed score assigned to the raw focus-band end. Default keeps old 900 at 900.");
        AddSection(host, "HARD-SECTION / DIFFICULTY WEIGHTING");
        AddEditor(host, "Hard-section importance", nameof(TuningConfig.DifficultyWeightStrength), .05m, 0, 4, 2, "0=every jump counts equally; higher values make hard sections matter more.");
        AddEditor(host, "Spacing importance", nameof(TuningConfig.DifficultySpacingWeight), .05m, 0, 3, 2, "How strongly relative jump distance defines a hard section.");
        AddEditor(host, "BPM importance", nameof(TuningConfig.DifficultyBpmWeight), .05m, 0, 3, 2, "How strongly movement rate defines a hard section.");
        AddEditor(host, "Consecutive-jump importance", nameof(TuningConfig.DifficultyStreakWeight), .05m, 0, 3, 2, "Rewards sustained jump sections over isolated easy filler.");
        AddEditor(host, "Jump streak spacing threshold", nameof(TuningConfig.DifficultyStreakSpacingThreshold), 5m, 40, 500, 0, "CS4-normalized px required for a jump to continue a difficult streak.");
        AddEditor(host, "Full streak bonus at", nameof(TuningConfig.DifficultyStreakFullLength), 1m, 2, 32, 0, "Number of consecutive qualifying jumps that reaches the full streak bonus.");
        AddEditor(host, "Difficulty weight floor", nameof(TuningConfig.DifficultyWeightFloor), .05m, .1m, 1.5m, 2, "Minimum importance assigned to easy filler.");
        AddEditor(host, "Difficulty weight ceiling", nameof(TuningConfig.DifficultyWeightCeiling), .05m, 1m, 6m, 2, "Maximum importance any hard transition can receive.");
        AddSection(host, "UNDERAIM + CIRCLE-SIZE LENIENCY");
        AddEditor(host, "Inner underaim recovery", nameof(TuningConfig.UnderAimInnerRecovery), .01m, 0, 1, 2, "Forgiveness when still near target center.");
        AddEditor(host, "Mid underaim recovery", nameof(TuningConfig.UnderAimMidRecovery), .01m, 0, 1, 2, "Forgiveness around mid-radius.");
        AddEditor(host, "Outer underaim recovery", nameof(TuningConfig.UnderAimOuterRecovery), .01m, 0, 1, 2, "Forgiveness toward the outer target.");
        AddEditor(host, "Edge underaim recovery", nameof(TuningConfig.UnderAimEdgeRecovery), .01m, 0, 1, 2, "Forgiveness near the hit-circle edge.");
        AddEditor(host, "Barely-outside recovery", nameof(TuningConfig.UnderAimOutsideRecovery), .01m, 0, 1, 2, "Tiny exact-time forgiveness just outside.");
        AddEditor(host, "Large-circle strictness", nameof(TuningConfig.LargeCircleStrictness), .05m, 0, 2, 2, "How much big circles lose their extra centering leniency.");
        return host;
    }

    private Control BuildPerfEditor()
    {
        var host = EditorHost();
        AddSection(host, "DEMAND MODEL");
        AddEditor(host, "Sustained raw challenge", nameof(TuningConfig.PerformanceChallengeWeight), .01m, 0, 3, 3, "Relative weight inside the replay-telemetry demand estimate.");
        AddEditor(host, "SR demand-floor strength", nameof(TuningConfig.PerformanceStarWeight), .05m, 0, 2, 2, "0 ignores SR as a floor; 1 uses the full SR-derived floor; >1 exaggerates it. SR is no longer diluted inside the telemetry mean.");
        AddEditor(host, "SR demand exponent", nameof(TuningConfig.PerformanceStarDemandExponent), .05m, .25m, 3, 2, "Nonlinear mapping from star ratio to the independent SR demand floor. Default 1.15.");
        AddEditor(host, "BPM", nameof(TuningConfig.PerformanceBpmWeight), .01m, 0, 3, 3, "Hard-section movement-rate demand. Default 0.30.");
        AddEditor(host, "Spacing", nameof(TuningConfig.PerformanceSpacingWeight), .01m, 0, 3, 3, "Hard-section spacing. Lower default because spacing already increases cursor velocity.");
        AddEditor(host, "AR", nameof(TuningConfig.PerformanceArWeight), .01m, 0, 3, 3, "Preview/read-time pressure.");
        AddEditor(host, "Density", nameof(TuningConfig.PerformanceDensityWeight), .01m, 0, 3, 3, "Visible-object load during the hard section.");
        AddSection(host, "SUSTAINED AIM DEMAND SHAPE");
        AddEditor(host, "Velocity exponent", nameof(TuningConfig.PerformanceVelocityExponent), .05m, .10m, 4, 2, "Nonlinear cursor-speed scaling. 1.50 makes elite high-speed aim separate much more than linear velocity.");
        AddEditor(host, "Spacing exponent", nameof(TuningConfig.PerformanceSpacingExponent), .05m, 0, 2, 2, "Extra spacing effect after velocity already accounts for distance. Default 0.35.");
        AddEditor(host, "Demand curve", nameof(TuningConfig.PerformanceDemandCurve), .05m, .25m, 4, 2, "Raises combined map demand to this power. >1 expands high-end differences.");
        AddEditor(host, "Hard-section percentile", nameof(TuningConfig.PerformanceDemandPercentile), .01m, .50m, .99m, 2, "0.85 = identify roughly the hardest 15% of transition demand before streak filtering.");
        AddEditor(host, "Minimum hard streak", nameof(TuningConfig.PerformanceMinHardSectionStreak), 1m, 1, 24, 0, "Minimum consecutive high-demand transitions required to define sustained hard material.");
        AddSection(host, "EXECUTION MULTIPLIER");
        AddEditor(host, "Execution importance", nameof(TuningConfig.PerformanceExecutionWeight), .05m, 0, 3, 2, "How strongly execution quality on the selected hard aim material multiplies demand.");
        AddEditor(host, "Hard execution mean", nameof(TuningConfig.PerformanceHardExecutionMeanWeight), .05m, 0, 3, 2, "Weight of mean proficiency across the sustained hard-section transitions.");
        AddEditor(host, "Hard execution lower quartile", nameof(TuningConfig.PerformanceHardExecutionLowerQuartileWeight), .05m, 0, 3, 2, "Adds some punishment for locally weak/missed hard transitions without letting a few misses nuke the entire replay.");
        AddEditor(host, "Reference proficiency", nameof(TuningConfig.PerformanceReferenceProficiency), 5m, 400, 1000, 0, "At this hard-section execution proficiency, execution multiplier = 1.");
        AddEditor(host, "Execution curve", nameof(TuningConfig.PerformanceExecutionCurve), 5m, 30, 800, 0, "Smaller = hard-section execution changes performance more aggressively. v8 defaults to a softer 450 so execution cannot overpower a several-star demand gap as easily.");
        AddEditor(host, "Score scale", nameof(TuningConfig.PerformanceScale), 10m, 20, 2000, 0, "Pure display scale before the consistency bonus.");
        AddSection(host, "FULL-MAP CONSISTENCY BONUS");
        AddEditor(host, "Maximum consistency bonus", nameof(TuningConfig.PerformanceConsistencyMaxBonus), .01m, 0, 2, 2, "Bonus-only. v8 defaults to +65% maximum so a long sustained aim performance can meaningfully separate from a 20-30 second burst at similar peak demand.");
        AddEditor(host, "Duration reference (sec)", nameof(TuningConfig.PerformanceConsistencyDurationReferenceSeconds), 5m, 5, 600, 0, "Controls how quickly duration earns consistency credit. Around 90 sec gives short 30 sec aim maps much less bonus headroom than long maps.");
        AddEditor(host, "Duration curve", nameof(TuningConfig.PerformanceConsistencyDurationExponent), .05m, .20m, 3, 2, "Shape of the duration bonus ramp. 1.0 is a smooth exponential saturation.");
        AddEditor(host, "Miss tolerance", nameof(TuningConfig.PerformanceConsistencyMissTolerance), .25m, .25m, 20, 2, "Higher = misses reduce the bonus more slowly. Misses only remove consistency credit; they do not nuke base Aim Performance.");
        AddEditor(host, "Miss importance", nameof(TuningConfig.PerformanceConsistencyMissWeight), .05m, 0, 3, 2, "Relative importance of miss count in the consistency-quality bonus.");
        AddEditor(host, "Control coverage importance", nameof(TuningConfig.PerformanceConsistencyControlWeight), .05m, 0, 3, 2, "Relative importance of maintaining clean aim across the map. Hard transitions count more than filler.");
        AddEditor(host, "Control coverage floor", nameof(TuningConfig.PerformanceConsistencyControlFloor), 5m, 0, 950, 0, "Raw transition proficiency that contributes zero control-coverage quality.");
        AddEditor(host, "Control coverage full", nameof(TuningConfig.PerformanceConsistencyControlFull), 5m, 100, 1000, 0, "Raw transition proficiency that contributes full control-coverage quality.");
        AddSection(host, "REFERENCE DEMANDS = 1.00×");
        AddEditor(host, "Challenge reference", nameof(TuningConfig.RefChallenge), 5m, 20, 500, 0, "100 = reference sustained challenge. Mostly a readable scale anchor now.");
        AddEditor(host, "Velocity reference", nameof(TuningConfig.RefVelocity), 50m, 200, 5000, 0, "Cursor velocity that equals 1.00× before the velocity exponent.");
        AddEditor(host, "Star reference", nameof(TuningConfig.RefStar), .1m, 1, 15, 2, "Star rating reference.");
        AddEditor(host, "BPM reference", nameof(TuningConfig.RefBpm), 5m, 50, 500, 0, "BPM reference.");
        AddEditor(host, "Spacing reference", nameof(TuningConfig.RefSpacing), 5m, 30, 600, 0, "CS4-normalized spacing reference.");
        AddEditor(host, "AR reference", nameof(TuningConfig.RefAr), .1m, 1, 12, 2, "Effective AR reference.");
        AddEditor(host, "Density reference", nameof(TuningConfig.RefDensity), .1m, 1, 12, 2, "Visible-object reference.");
        return host;
    }

    private Control BuildProfNotes()
    {
        var box = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9.5f),
            Text = "Weights are normalized automatically, so doubling every component weight changes nothing. Change them relative to one another.\r\n\r\nThe final proficiency resolution curve is applied AFTER all mechanical scoring and map aggregation. Transition scores stay on the raw 0-1000 scale. With the defaults, raw 750 maps to 550, raw 900 stays 900, and 1000 stays 1000. This gives the common 750-900 region much more numerical resolution without changing what the replay analyzer measures.\r\n\r\nHard-section importance is separate: it changes how much each jump matters to the final map score. Spacing, BPM, and consecutive-jump streaks define relative difficulty inside that map; the transition table shows the resulting difficulty weight.\r\n\r\nAim Performance intentionally uses the PRE-REMAP mechanical proficiency so experimenting with display resolution does not silently alter the demand model.\r\n\r\nUse the Debug visualizer to check whether score changes line up with cursor behavior rather than just chasing numbers." };
        return box;
    }

    private Control BuildDebugTab()
    {
        ConfigureDebugGrid();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Background, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 7, 8, 4), WrapContents = false };
        var prev = ToolkitUi.Button("◀ Previous"); prev.Click += (_, _) => StepTransition(-1);
        var next = ToolkitUi.Button("Next ▶"); next.Click += (_, _) => StepTransition(1);
        transitionPicker.Width = 84; transitionPicker.ValueChanged += (_, _) => SelectTransition((int)transitionPicker.Value);
        top.Controls.Add(prev); top.Controls.Add(next); top.Controls.Add(new Label { Text="Index", AutoSize=true, ForeColor=Theme.Muted, Padding=new Padding(8,7,2,0) }); top.Controls.Add(transitionPicker); top.Controls.Add(transitionCaption);
        root.Controls.Add(top, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 7, BackColor = Theme.Border };
        split.Panel1.Controls.Add(ToolkitUi.Wrap(debugPath, "Player cursor vs ideal computer path"));
        split.Panel2.Controls.Add(ToolkitUi.Wrap(debugMetricGrid, "Selected jump · raw → adjusted → contribution"));
        split.Resize += (_, _) => { if (split.Width > 900) ToolkitUi.SetSplitterDistanceSafe(split, (int)(split.Width * .64)); };
        root.Controls.Add(split, 0, 1);
        return root;
    }

    private async Task InitializeAsync()
    {
        ConfigureWatcher();
        await RefreshPcReplayTableAsync();
        SetStatus("Ready · fixed production scoring profile. Import replays, browse PC replays, or use Quick checker.");
    }

    private async Task ReindexAsync()
    {
        if (busy) return;
        busy = true;
        PullPathSettings();
        try
        {
            var p = new Progress<string>(SetStatus);
            await resolver.RebuildAsync(p);
            SetStatus($"Map index ready · {resolver.CachedBeatmapCount:N0} maps.");
        }
        catch (Exception ex) { SetStatus("Map indexing failed: " + ex.Message); }
        finally { busy = false; }
    }

    private async Task LoadLatestAsync()
    {
        PullPathSettings();
        if (!Directory.Exists(settings.ReplayDirectory)) { SetStatus("Replay directory does not exist."); return; }
        var f = Directory.EnumerateFiles(settings.ReplayDirectory, "*.osr", SearchOption.TopDirectoryOnly)
            .Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
        if (f == null) { SetStatus("No .osr files found in replay directory."); return; }
        pendingWatchPath = null;
        await AddReplayFilesAsync(new[] { f.FullName }, selectLast: true, forceReload: false);
    }

    private async Task OpenReplayAsync()
    {
        using var d = new OpenFileDialog { Filter = "osu! replay (*.osr)|*.osr|All files (*.*)|*.*", Multiselect = true };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        pendingWatchPath = null;
        await AddReplayFilesAsync(d.FileNames, selectLast: true, forceReload: false);
    }

    private async Task LoadReplayAsync(string path, bool force = false)
        => await AddReplayFilesAsync(new[] { path }, selectLast: true, forceReload: force);

    private async Task AddReplayFilesAsync(IEnumerable<string> inputPaths, bool selectLast, bool forceReload)
    {
        var paths = inputPaths.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0) return;

        int generation = Interlocked.Increment(ref replayLoadGeneration);
        var nextCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref replayLoadCts, nextCts);
        previous?.Cancel(); previous?.Dispose();
        var token = nextCts.Token;
        ReplayWorkspaceItem? last = null;
        int done = 0;

        try
        {
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                SetStatus(paths.Count == 1
                    ? $"Loading {Path.GetFileName(path)}…"
                    : $"Importing replay {done + 1}/{paths.Count} · {Path.GetFileName(path)}…");
                var item = await AnalyzeReplayFileAsync(path, forceReload, token);
                token.ThrowIfCancellationRequested();
                if (generation != replayLoadGeneration) return;
                replayCache[item.Key] = item;
                last = item;
                done++;
                if (paths.Count == 1 || done % 5 == 0) RefreshCacheGrid();
            }

            RefreshCacheGrid();
            if (last != null && selectLast) SelectCacheItem(last);
            SetStatus(paths.Count == 1
                ? $"Loaded {last?.Beatmap.DisplayName} · {last?.Analysis.TransitionCount:N0} aim transitions."
                : $"Imported {done:N0} replay(s) into the test set · {replayCache.Count:N0} cached total.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation != replayLoadGeneration) return;
            SetStatus("Replay import failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Could not analyze replay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task<ReplayWorkspaceItem> AnalyzeReplayFileAsync(string path, bool forceReload, CancellationToken token)
    {
        string signature = ReplayFileSignature(path);
        if (!forceReload)
        {
            var byPath = replayCache.Values.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && x.Signature == signature);
            if (byPath != null) return byPath;
        }
        ReplayData r = await Task.Run(() => ReplayReader.Read(path), token);
        token.ThrowIfCancellationRequested();
        string key = !string.IsNullOrWhiteSpace(r.ReplayHash) ? r.ReplayHash : path;
        if (!forceReload && replayCache.TryGetValue(key, out var existing) && existing.Signature == signature)
            return existing;

        var m = resolver.Resolve(r.BeatmapHash, refreshDbOnMiss: false);
        if (m == null)
        {
            SetStatus($"Resolving local map · {ShortHash(r.BeatmapHash)}…");
            m = await resolver.ResolveFromSongsAsync(r.BeatmapHash, new Progress<string>(SetStatus), token);
        }
        token.ThrowIfCancellationRequested();
        if (m == null) throw new InvalidOperationException("Could not find the exact .osu difficulty matching this replay hash.");

        var a = await Task.Run(() => analyzer.Analyze(r, m), token);
        token.ThrowIfCancellationRequested();
        var item = new ReplayWorkspaceItem
        {
            Key = key,
            Path = path,
            Signature = signature,
            Replay = r,
            Beatmap = m,
            Analysis = a
        };
        RetuneItem(item);
        return item;
    }

    private void SelectCacheItem(ReplayWorkspaceItem item)
    {
        currentItem = item;
        replay = item.Replay;
        map = item.Beatmap;
        analysis = item.Analysis;
        loadedReplayPath = item.Path;
        loadedReplaySignature = item.Signature;
        tunedTransitions = item.TunedTransitions;
        difficultyWeights = item.DifficultyWeights;
        rawTunedProficiency = item.RawTunedProficiency;
        tunedProficiency = item.TunedProficiency;
        tunedPerformance = item.TunedPerformance;
        RenderCurrentReplay();
        HighlightCachedItem(item.Key);
    }

    private void RetuneItem(ReplayWorkspaceItem item)
    {
        item.TunedTransitions = item.Analysis.Transitions.Select(t => TuningScorer.Transition(t, tuning)).ToList();
        item.DifficultyWeights = TuningScorer.DifficultyWeights(item.Analysis.Transitions, tuning);
        item.RawTunedProficiency = TuningScorer.AggregateRaw(item.TunedTransitions.Select(x => x.Score).ToList(), item.DifficultyWeights, tuning);
        item.TunedProficiency = TuningScorer.RemapProficiency(item.RawTunedProficiency, tuning);
        item.TunedPerformance = TuningScorer.Performance(item.Analysis, item.RawTunedProficiency, tuning);
    }

    private void ClearLoadedReplayStateForNewRequest(string path)
    {
        currentItem = null;
        replay = null; map = null; analysis = null; tunedPerformance = null;
        tunedTransitions = new(); difficultyWeights = Array.Empty<DifficultyWeightInfo>(); rawTunedProficiency = 0; tunedProficiency = 0;
        loadedReplayPath = null; loadedReplaySignature = null;
        transitionGrid.Rows.Clear(); profBreakdown.Rows.Clear(); perfBreakdown.Rows.Clear(); debugMetricGrid.Rows.Clear(); debugPath.ClearData();
        originalProf.Text = tunedProf.Text = originalPerf.Text = tunedPerf.Text = demandLabel.Text = "—";
        mapLabel.Text = string.IsNullOrWhiteSpace(path) ? "No replay selected." : $"Loading {Path.GetFileName(path)}…";
        transitionCaption.Text = ""; transitionPicker.Maximum = 0; if (transitionPicker.Value != 0) transitionPicker.Value = 0;
    }

    private static string ReplayFileSignature(string path)
    {
        try { var f = new FileInfo(path); return $"{f.Length}:{f.LastWriteTimeUtc.Ticks}"; }
        catch { return ""; }
    }

    private static string ShortHash(string? hash)
    {
        hash ??= "";
        return hash.Length <= 8 ? hash : hash[..8] + "…";
    }

    private async Task ReanalyzeCurrentAsync()
    {
        if (replay == null || map == null) return;
        var updated = await Task.Run(() => analyzer.Analyze(replay, map));
        analysis = updated;
        if (currentItem != null)
        {
            currentItem.Analysis = updated;
            RetuneItem(currentItem);
        }
        Recalculate();
    }

    private void Recalculate()
    {
        PullEditors();
        foreach (var item in replayCache.Values) RetuneItem(item);
        RefreshCacheGrid();
        RetuneQuickResults();

        if (currentItem != null && replayCache.TryGetValue(currentItem.Key, out var refreshed))
        {
            currentItem = refreshed;
            replay = refreshed.Replay;
            map = refreshed.Beatmap;
            analysis = refreshed.Analysis;
            tunedTransitions = refreshed.TunedTransitions;
            difficultyWeights = refreshed.DifficultyWeights;
            rawTunedProficiency = refreshed.RawTunedProficiency;
            tunedProficiency = refreshed.TunedProficiency;
            tunedPerformance = refreshed.TunedPerformance;
            RenderCurrentReplay();
        }
        else if (analysis == null) ClearResults();
    }

    private void RenderCurrentReplay()
    {
        if (analysis == null) { ClearResults(); return; }
        if (tunedTransitions.Count != analysis.Transitions.Count)
        {
            tunedTransitions = analysis.Transitions.Select(t => TuningScorer.Transition(t, tuning)).ToList();
            difficultyWeights = TuningScorer.DifficultyWeights(analysis.Transitions, tuning);
            rawTunedProficiency = TuningScorer.AggregateRaw(tunedTransitions.Select(x => x.Score).ToList(), difficultyWeights, tuning);
            tunedProficiency = TuningScorer.RemapProficiency(rawTunedProficiency, tuning);
            tunedPerformance = TuningScorer.Performance(analysis, rawTunedProficiency, tuning);
        }

        bool synthetic = currentItem?.IsSynthetic == true;
        originalProf.Text = synthetic ? "SIM" : Humanize.LegacyScoreShort(analysis.Proficiency);
        tunedProf.Text = Humanize.TunedScoreShort(tunedProficiency, tuning) + $"\nraw {rawTunedProficiency:0}";
        originalPerf.Text = synthetic ? "SIM" : analysis.RawAimRating.ToString("0");
        tunedPerf.Text = tunedPerformance?.Score.ToString("0") ?? "—";
        demandLabel.Text = $"{analysis.StarRating:0.00}★ · AR {analysis.EffectiveAr:0.0}\n{analysis.MeanBpm:0} BPM · {analysis.SpacingP75:0}px";
        mapLabel.Text = synthetic
            ? $"SYNTHETIC TEST · {analysis.Beatmap.DisplayName} · {ModUtils.ToShortString(analysis.Replay.Mods)} · {currentItem?.SyntheticLabel}"
            : $"{analysis.Beatmap.DisplayName} · {ModUtils.ToShortString(analysis.Replay.Mods)} · {ScoreUtils.Grade(analysis.Replay.Count300, analysis.Replay.Count100, analysis.Replay.Count50, analysis.Replay.CountMiss)} · {ScoreUtils.Accuracy(analysis.Replay.Count300, analysis.Replay.Count100, analysis.Replay.Count50, analysis.Replay.CountMiss):0.00}%";

        transitionGrid.Rows.Clear();
        for (int i = 0; i < analysis.Transitions.Count; i++)
        {
            var t = analysis.Transitions[i]; var b = tunedTransitions[i];
            var dw = i < difficultyWeights.Length ? difficultyWeights[i] : new DifficultyWeightInfo(1, 0, 1);
            transitionGrid.Rows.Add(i, t.ObjectIndex, t.Bpm.ToString("0"), Humanize.Spacing(t.NormalizedSpacing, true), dw.JumpStreakLength, dw.Weight.ToString("0.00×"), t.ErrorClass,
                t.Proficiency.ToString("0"), b.Score.ToString("0"), (b.Score-t.Proficiency).ToString("+0;-0;0"),
                t.Straightness.ToString("0"), t.Landing.ToString("0"), t.Arrival.ToString("0"), t.Stability.ToString("0"), t.Deceleration.ToString("0"), ScoringConfig.EffectiveIdealPathMatch(t).ToString("0"));
        }
        transitionPicker.Maximum = Math.Max(0, analysis.Transitions.Count - 1);
        if (transitionPicker.Value > transitionPicker.Maximum) transitionPicker.Value = transitionPicker.Maximum;
        SelectTransition((int)transitionPicker.Value);
    }

    private void SelectTransition(int index)
    {
        if (analysis == null || analysis.Transitions.Count == 0) return;
        index = Math.Clamp(index, 0, analysis.Transitions.Count - 1);
        if (transitionPicker.Value != index) { transitionPicker.Value = index; return; }
        var t = analysis.Transitions[index];
        var b = index < tunedTransitions.Count ? tunedTransitions[index] : TuningScorer.Transition(t, tuning);
        var dw = index < difficultyWeights.Length ? difficultyWeights[index] : new DifficultyWeightInfo(1, 0, 1);
        transitionCaption.Text = $"Object {t.ObjectIndex} · {t.Bpm:0} BPM · {Humanize.Spacing(t.NormalizedSpacing, true)} · streak {dw.JumpStreakLength} · difficulty weight {dw.Weight:0.00}× · {t.ErrorClass}";
        if (currentItem?.IsSynthetic == true) debugPath.ClearData(); else debugPath.SetData(replay, map, t);
        UpdateDebugGrid(t, b, dw);
        if (index < transitionGrid.Rows.Count)
        {
            transitionGrid.ClearSelection(); transitionGrid.Rows[index].Selected = true;
            transitionGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, index - 3);
        }
    }

    private void UpdateProfBreakdown(TransitionMetric t, ProficiencyBreakdown b)
    {
        profBreakdown.Rows.Clear();
        AddProf("Landing / centering", b.LandingRaw, b.LandingEffective, b.LandingWeight);
        AddProf("Arrival timing", b.Arrival, b.Arrival, b.ArrivalWeight);
        AddProf("Straightness", b.Straightness, b.Straightness, b.StraightnessWeight);
        AddProf("Braking / decel", b.DecelerationRaw, b.DecelerationEffective, b.DecelerationWeight);
        AddProf("Stability / shake", b.Stability, b.Stability, b.StabilityWeight);
        AddProf("Ideal computer path", b.IdealPath, b.IdealPath, b.IdealPathWeight);
        void AddProf(string name, double raw, double effective, double w) => profBreakdown.Rows.Add(name, raw.ToString("0"), effective.ToString("0"), w.ToString("0.000"), (effective*w).ToString("0.0"));
    }

    private void UpdateDebugGrid(TransitionMetric t, ProficiencyBreakdown b, DifficultyWeightInfo difficulty)
    {
        debugMetricGrid.Rows.Clear();
        void Row(string metric, string raw, string adjusted, string note) => debugMetricGrid.Rows.Add(metric, raw, adjusted, note);
        Row("Current proficiency", t.Proficiency.ToString("0") + " legacy", b.Score.ToString("0"), (b.Score-t.Proficiency).ToString("+0;-0;0") + " vs legacy");
        Row("Map importance", difficulty.RelativeDemand.ToString("0.00") + " relative demand", difficulty.Weight.ToString("0.00×"), $"{difficulty.JumpStreakLength} consecutive jumps above threshold");
        Row("Landing", b.LandingRaw.ToString("0"), b.LandingEffective.ToString("0"), $"axial {t.AxialError:+0.00;-0.00;0.00}r · lateral {t.LateralError:+0.00;-0.00;0.00}r");
        Row("Arrival", t.Arrival.ToString("0"), t.Arrival.ToString("0"), "cursor arrival / settle timing");
        Row("Straightness", t.Straightness.ToString("0"), t.Straightness.ToString("0"), "path length + lateral curvature");
        Row("Stability", t.Stability.ToString("0"), t.Stability.ToString("0"), "shake / reversals near target");
        Row("Deceleration", b.DecelerationRaw.ToString("0"), b.DecelerationEffective.ToString("0"), "terminal speed + re-acceleration");
        Row("Ideal path", t.IdealPathMatch.ToString("0"), b.IdealPath.ToString("0"), "player trace vs minimum-jerk computer trace");
        Row("Aim tension", t.AimTension.ToString("0"), Humanize.TensionShort(t.AimTension), "inferred, not physiological measurement");
        Row("Error class", t.ErrorClass, t.ErrorClass, "classification retained even when underaim penalty is softened");
        Row("Demand", $"{t.Bpm:0} BPM", Humanize.Spacing(t.NormalizedSpacing, true), $"density {t.Density:0.0} · angle {t.Angle:0}° · challenge {t.Challenge:0}");
    }

    private void UpdatePerformanceBreakdown()
    {
        perfBreakdown.Rows.Clear();
        if (analysis == null || tunedPerformance == null) return;
        var b = tunedPerformance;
        Add("Sustained challenge", b.ChallengeValue, tuning.RefChallenge, b.ChallengeRatio, tuning.PerformanceChallengeWeight);
        perfBreakdown.Rows.Add("Star rating", $"{analysis.StarRating:0.00}★", $"{tuning.RefStar:0.00}★ ref", b.StarRatio.ToString("0.000×"), analysis.StarSource);
        perfBreakdown.Rows.Add("SR demand", "—", $"exp {tuning.PerformanceStarDemandExponent:0.00}", b.StarDemandIndex.ToString("0.000×"), $"floor strength {tuning.PerformanceStarWeight:0.00}");
        perfBreakdown.Rows.Add("SR demand floor", "—", "independent floor", b.StarFloorIndex.ToString("0.000×"), "final demand cannot fall below this");
        Add("Hard-section BPM", b.HardSectionBpm, tuning.RefBpm, b.BpmRatio, tuning.PerformanceBpmWeight);
        Add("Hard-section spacing", b.HardSectionSpacing, tuning.RefSpacing, b.SpacingRatio, tuning.PerformanceSpacingWeight);
        Add("Effective AR", analysis.EffectiveAr, tuning.RefAr, b.ArRatio, tuning.PerformanceArWeight);
        Add("Hard-section density", b.HardSectionDensity, tuning.RefDensity, b.DensityRatio, tuning.PerformanceDensityWeight);
        perfBreakdown.Rows.Add("Hard-section velocity", b.HardSectionVelocity.ToString("0"), tuning.RefVelocity.ToString("0"), (b.HardSectionVelocity / Math.Max(1, tuning.RefVelocity)).ToString("0.000×"), $"exp {tuning.PerformanceVelocityExponent:0.00}");
        perfBreakdown.Rows.Add("Hard material selected", b.HardSectionTransitions.ToString("N0") + " transitions", $"percentile {tuning.PerformanceDemandPercentile:0.00}", $"threshold {b.HardSectionThreshold:0.000}", $"min streak {tuning.PerformanceMinHardSectionStreak:0}");
        perfBreakdown.Rows.Add("Telemetry base", "—", "—", b.BaseDemandIndex.ToString("0.000×"), "challenge/BPM/spacing/AR/density before curve");
        perfBreakdown.Rows.Add("Final demand", "—", "max(telemetry, SR floor)", b.DemandIndex.ToString("0.000×"), $"telemetry power {tuning.PerformanceDemandCurve:0.00}");
        perfBreakdown.Rows.Add("Hard-section execution", b.HardExecutionProficiency.ToString("0"), tuning.PerformanceReferenceProficiency.ToString("0"), b.ExecutionMultiplier.ToString("0.000×"), $"mean w {tuning.PerformanceHardExecutionMeanWeight:0.00} · lower-Q w {tuning.PerformanceHardExecutionLowerQuartileWeight:0.00}");
        perfBreakdown.Rows.Add("Whole-map raw proficiency", rawTunedProficiency.ToString("0"), "not used for base aim", "—", "kept separate so misses/filler cannot globally nuke capability");
        perfBreakdown.Rows.Add("Base capability", "—", "—", b.BaseCapabilityScore.ToString("0"), "hard demand × hard-section execution");
        perfBreakdown.Rows.Add("Aim-active duration", b.AimActiveSeconds.ToString("0") + " sec", tuning.PerformanceConsistencyDurationReferenceSeconds.ToString("0") + " sec ref", b.DurationFactor.ToString("0.000"), "breaks / huge gaps capped out");
        perfBreakdown.Rows.Add("Miss consistency", b.MissCount.ToString() + " misses", tuning.PerformanceConsistencyMissTolerance.ToString("0.00") + " tolerance", b.MissConsistency.ToString("0.000"), "bonus-only: misses do not subtract base capability");
        perfBreakdown.Rows.Add("Control coverage", "difficulty-weighted", $"{tuning.PerformanceConsistencyControlFloor:0}→{tuning.PerformanceConsistencyControlFull:0}", b.ControlCoverage.ToString("0.000"), "easy filler counts less");
        perfBreakdown.Rows.Add("Consistency quality", "—", "—", b.ConsistencyQuality.ToString("0.000"), $"miss w {tuning.PerformanceConsistencyMissWeight:0.00} · control w {tuning.PerformanceConsistencyControlWeight:0.00}");
        perfBreakdown.Rows.Add("Consistency multiplier", "—", $"max +{tuning.PerformanceConsistencyMaxBonus*100:0}%", b.ConsistencyMultiplier.ToString("0.000×"), "duration × consistency quality");
        perfBreakdown.Rows.Add("Displayed proficiency", tunedProficiency.ToString("0"), "resolution curve", Humanize.TunedProficiencyTier(tunedProficiency, tuning), "does not affect aim performance");
        perfBreakdown.Rows.Add("FINAL", "—", "—", b.Score.ToString("0"), $"base {b.BaseCapabilityScore:0} × {b.ConsistencyMultiplier:0.000}");
        void Add(string n, double value, double reference, double ratio, double weight) => perfBreakdown.Rows.Add(n, value.ToString("0.00"), reference.ToString("0.00"), ratio.ToString("0.000×"), weight.ToString("0.000"));
    }

    private void StepTransition(int delta)
    {
        if (analysis == null || analysis.Transitions.Count == 0) return;
        int next = Math.Clamp((int)transitionPicker.Value + delta, 0, analysis.Transitions.Count - 1);
        transitionPicker.Value = next;
    }

    private void ConfigureWatcher()
    {
        replayWatcher?.Dispose(); replayWatcher = null;
        if (!watchLatest.Checked || !Directory.Exists(replayPath.Text)) return;
        try
        {
            replayWatcher = new FileSystemWatcher(replayPath.Text, "*.osr") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
            FileSystemEventHandler changed = (_, e) => QueueWatchedReplay(e.FullPath);
            replayWatcher.Created += changed; replayWatcher.Changed += changed; replayWatcher.Renamed += (_, e) => QueueWatchedReplay(e.FullPath);
        }
        catch { }
    }

    private void QueueWatchedReplay(string path)
    {
        try { path = Path.GetFullPath(path); } catch { }
        string sig = ReplayFileSignature(path);
        if (loadedReplayPath != null && path.Equals(loadedReplayPath, StringComparison.OrdinalIgnoreCase) && sig == loadedReplaySignature)
            return;

        pendingWatchPath = path;
        _ = Task.Run(async () =>
        {
            await Task.Delay(70);
            if (pendingWatchPath != path) return;
            try
            {
                bool ready = false;
                for (int i = 0; i < 12; i++)
                {
                    try { _ = ReplayReader.ReadHeader(path); ready = true; break; }
                    catch { await Task.Delay(35); }
                }
                if (!ready || pendingWatchPath != path) return;
                BeginInvoke(new Action(async () =>
                {
                    if (pendingWatchPath != path) return;
                    pendingWatchPath = null;
                    await LoadReplayAsync(path, force: false);
                }));
            }
            catch { }
        });
    }

    private void ConfigureCacheGrid()
    {
        ToolkitUi.StyleDataGrid(cacheGrid);
        cacheGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        cacheGrid.MultiSelect = false;
        string[] names = { "Map", "Mods", "Acc", "Miss", "Legacy", "Raw current", "Final prof", "Δ prof", "Legacy aim", "Base aim", "Cons ×", "Current aim", "Δ aim" };
        int[] widths = { 235, 52, 62, 44, 70, 76, 118, 62, 70, 72, 62, 78, 62 };
        for (int i = 0; i < names.Length; i++) { cacheGrid.Columns.Add("k" + i, names[i]); cacheGrid.Columns[i].Width = widths[i]; }
        cacheGrid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (cacheGrid.Rows[e.RowIndex].Tag is string key && replayCache.TryGetValue(key, out var item)) SelectCacheItem(item);
        };
        cacheGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (cacheGrid.Rows[e.RowIndex].Tag is string key && replayCache.TryGetValue(key, out var item)) SelectCacheItem(item);
        };
    }

    private void ConfigurePcReplayGrid()
    {
        ToolkitUi.StyleDataGrid(pcReplayGrid);
        pcReplayGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        pcReplayGrid.MultiSelect = true;
        pcReplayGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        string[] names = { "Time", "Map", "Mods", "Grade", "Miss", "Acc", "Player" };
        int[] widths = { 118, 300, 58, 52, 44, 66, 110 };
        for (int i = 0; i < names.Length; i++) { pcReplayGrid.Columns.Add("p" + i, names[i]); pcReplayGrid.Columns[i].Width = widths[i]; }
        pcReplayGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || pcReplayGrid.Rows[e.RowIndex].Tag is not LocalReplayEntry entry) return;
            await AddReplayFilesAsync(new[] { entry.Path }, selectLast: true, forceReload: false);
        };
    }

    private void RefreshCacheGrid()
    {
        string? selectedKey = currentItem?.Key;
        cacheGrid.Rows.Clear();
        foreach (var item in replayCache.Values.OrderByDescending(x => x.Replay.TimestampUtc))
        {
            double acc = ScoreUtils.Accuracy(item.Replay.Count300, item.Replay.Count100, item.Replay.Count50, item.Replay.CountMiss);
            double tunedAim = item.TunedPerformance?.Score ?? 0;
            double dProf = item.TunedProficiency - item.Analysis.Proficiency;
            double dAim = tunedAim - item.Analysis.RawAimRating;
            double baseAim = item.TunedPerformance?.BaseCapabilityScore ?? tunedAim;
            double consistency = item.TunedPerformance?.ConsistencyMultiplier ?? 1.0;
            string mapName = item.IsSynthetic ? $"SIM · {item.Beatmap.DisplayName} · {item.SyntheticLabel}" : item.Beatmap.DisplayName;
            int row;
            if (item.IsSynthetic)
            {
                row = cacheGrid.Rows.Add(mapName, ModUtils.ToShortString(item.Replay.Mods), "SIM", item.Replay.CountMiss,
                    "—", item.RawTunedProficiency.ToString("0"), Humanize.TunedScoreShort(item.TunedProficiency, tuning), "—",
                    "—", baseAim.ToString("0"), consistency.ToString("0.000×"), tunedAim.ToString("0"), "—");
                cacheGrid.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(31, 34, 43);
            }
            else
            {
                row = cacheGrid.Rows.Add(mapName, ModUtils.ToShortString(item.Replay.Mods), $"{acc:0.00}%", item.Replay.CountMiss,
                    item.Analysis.Proficiency.ToString("0"), item.RawTunedProficiency.ToString("0"), Humanize.TunedScoreShort(item.TunedProficiency, tuning), dProf.ToString("+0;-0;0"),
                    item.Analysis.RawAimRating.ToString("0"), baseAim.ToString("0"), consistency.ToString("0.000×"), tunedAim.ToString("0"), dAim.ToString("+0;-0;0"));
                cacheGrid.Rows[row].Cells[7].Style.ForeColor = dProf >= 0 ? Theme.Good : Theme.Bad;
                cacheGrid.Rows[row].Cells[12].Style.ForeColor = dAim >= 0 ? Theme.Good : Theme.Bad;
            }
            cacheGrid.Rows[row].Tag = item.Key;
        }
        cacheSummary.Text = $"{replayCache.Count:N0} test case{(replayCache.Count == 1 ? "" : "s")} cached";
        if (!string.IsNullOrWhiteSpace(selectedKey)) HighlightCachedItem(selectedKey);
    }

    private void HighlightCachedItem(string key)
    {
        foreach (DataGridViewRow row in cacheGrid.Rows)
        {
            bool match = row.Tag is string k && k.Equals(key, StringComparison.OrdinalIgnoreCase);
            row.Selected = match;
            if (match && row.Index >= 0) cacheGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, row.Index - 2);
        }
    }

    private void RemoveSelectedCached()
    {
        var keys = cacheGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as string).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        foreach (var key in keys) replayCache.Remove(key!);
        if (currentItem != null && !replayCache.ContainsKey(currentItem.Key)) ClearLoadedReplayStateForNewRequest("");
        RefreshCacheGrid();
    }

    private async Task RefreshPcReplayTableAsync()
    {
        PullPathSettings();
        if (!Directory.Exists(settings.ReplayDirectory)) { pcReplayGrid.Rows.Clear(); return; }
        string range = pcReplayRange.SelectedItem?.ToString() ?? "Latest 500";
        SetStatus("Reading local replay headers…");
        try
        {
            var rows = await Task.Run(() =>
            {
                IEnumerable<FileInfo> files = Directory.EnumerateFiles(settings.ReplayDirectory, "*.osr", SearchOption.TopDirectoryOnly)
                    .Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc);
                if (range == "Latest 500") files = files.Take(500);
                else if (range == "7 days") files = files.Where(f => f.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7));
                else if (range == "30 days") files = files.Where(f => f.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-30));

                var list = new List<LocalReplayEntry>();
                foreach (var f in files)
                {
                    try
                    {
                        var h = ReplayReader.ReadHeader(f.FullName);
                        if (h.Mode != 0) continue;
                        list.Add(new LocalReplayEntry { Path = f.FullName, Header = h, MapName = resolver.DisplayNameForHash(h.BeatmapHash) });
                    }
                    catch { }
                }
                return list;
            });

            localReplayEntries.Clear(); localReplayEntries.AddRange(rows);
            pcReplayGrid.Rows.Clear();
            foreach (var entry in rows)
            {
                var h = entry.Header;
                double acc = ScoreUtils.Accuracy(h.Count300, h.Count100, h.Count50, h.CountMiss);
                int ri = pcReplayGrid.Rows.Add(h.TimestampUtc.ToLocalTime().ToString("M/d h:mm tt"), entry.MapName, ModUtils.ToShortString(h.Mods),
                    ScoreUtils.Grade(h.Count300, h.Count100, h.Count50, h.CountMiss), h.CountMiss, $"{acc:0.00}%", h.PlayerName);
                pcReplayGrid.Rows[ri].Tag = entry;
            }
            SetStatus($"Local replay table ready · {rows.Count:N0} replay headers.");
        }
        catch (Exception ex) { SetStatus("Could not refresh local replay list: " + ex.Message); }
    }

    private async Task AnalyzeSelectedPcReplaysAsync()
    {
        var paths = pcReplayGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.Tag as LocalReplayEntry).Where(x => x != null).Select(x => x!.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0) return;
        await AddReplayFilesAsync(paths, selectLast: true, forceReload: false);
    }

    private void ConfigureTransitionGrid()
    {
        ToolkitUi.StyleDataGrid(transitionGrid); transitionGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        string[] names = { "#","Object","BPM","Spacing","Streak","Diff wt","Error","Legacy","Current prof","Δ","Straight","Landing","Arrival","Stability","Braking","Ideal path" };
        int[] widths = { 42,60,55,120,52,58,76,70,78,52,65,65,65,65,65,72 };
        for(int i=0;i<names.Length;i++){ transitionGrid.Columns.Add("c"+i,names[i]); transitionGrid.Columns[i].Width=widths[i]; }
        transitionGrid.CellClick += (_, e) => { if (e.RowIndex >= 0) transitionPicker.Value = Math.Min(transitionPicker.Maximum, e.RowIndex); };
    }

    private void ConfigureBreakdownGrids()
    {
        ToolkitUi.StyleDataGrid(profBreakdown); profBreakdown.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach(var s in new[]{"Component","Raw","Effective","Weight","Contribution"}) profBreakdown.Columns.Add(Guid.NewGuid().ToString(),s);
        ToolkitUi.StyleDataGrid(perfBreakdown); perfBreakdown.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach(var s in new[]{"Demand component","Map value","Reference","Ratio / score","Weight"}) perfBreakdown.Columns.Add(Guid.NewGuid().ToString(),s);
    }

    private void ConfigureDebugGrid()
    {
        ToolkitUi.StyleDataGrid(debugMetricGrid); debugMetricGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        foreach(var t in new[]{("Metric",145),("Raw",100),("Adjusted / read",145),("Meaning",330)}) { debugMetricGrid.Columns.Add(Guid.NewGuid().ToString(),t.Item1); debugMetricGrid.Columns[debugMetricGrid.Columns.Count-1].Width=t.Item2; }
    }

    private FlowLayoutPanel EditorHost() => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Panel, Padding = new Padding(12) };
    private void AddSection(FlowLayoutPanel host, string text) => host.Controls.Add(new Label { Text=text, AutoSize=true, ForeColor=Theme.Accent, Font=new Font("Segoe UI Semibold",9f), Margin=new Padding(0,10,0,4) });
    private void AddEditor(FlowLayoutPanel host, string label, string prop, decimal step, decimal min, decimal max, int decimals, string hint)
    {
        var row = new TableLayoutPanel { Width=620, Height=38, ColumnCount=3, BackColor=Theme.Panel, Margin=new Padding(0,1,0,1) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        row.Controls.Add(new Label { Text=label, Dock=DockStyle.Fill, ForeColor=Theme.Text, TextAlign=ContentAlignment.MiddleLeft },0,0);
        // Start every editor at a legal value. Several performance-reference controls
        // have minima above zero (e.g. 400 proficiency, 50 BPM), so constructing them
        // with Value=0 makes WinForms throw before the form can open.
        var n = Num(min, max, min, decimals); n.Increment=step; n.Width=78; n.ValueChanged += (_,_)=> { if(!loadingEditors){ PullEditors(); Recalculate(); } }; editors[prop]=n; row.Controls.Add(n,1,0);
        row.Controls.Add(new Label { Text=hint, Dock=DockStyle.Fill, ForeColor=Theme.Muted, TextAlign=ContentAlignment.MiddleLeft, AutoEllipsis=true },2,0);
        host.Controls.Add(row);
    }

    private void PushEditors()
    {
        loadingEditors = true;
        try
        {
            foreach (var kv in editors)
            {
                var p = typeof(TuningConfig).GetProperty(kv.Key); if (p == null) continue;
                double value = Convert.ToDouble(p.GetValue(tuning), CultureInfo.InvariantCulture);
                kv.Value.Value = Math.Clamp((decimal)value, kv.Value.Minimum, kv.Value.Maximum);
            }
        }
        finally { loadingEditors = false; }
    }

    private void PullEditors()
    {
        if (loadingEditors) return;
        foreach (var kv in editors)
        {
            var p = typeof(TuningConfig).GetProperty(kv.Key); if (p == null || !p.CanWrite) continue;
            p.SetValue(tuning, (double)kv.Value.Value);
        }
    }

    private void ExportProfile()
    {
        PullEditors(); using var d=new SaveFileDialog{Filter="JSON tuning profile (*.json)|*.json",FileName="aim-tuning-profile.json"}; if(d.ShowDialog(this)!=DialogResult.OK)return;
        File.WriteAllText(d.FileName,System.Text.Json.JsonSerializer.Serialize(tuning,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
    private void ImportProfile()
    {
        using var d=new OpenFileDialog{Filter="JSON tuning profile (*.json)|*.json"}; if(d.ShowDialog(this)!=DialogResult.OK)return;
        try{ tuning=System.Text.Json.JsonSerializer.Deserialize<TuningConfig>(File.ReadAllText(d.FileName))??new TuningConfig(); PushEditors(); Recalculate(); }catch(Exception ex){MessageBox.Show(this,ex.Message,"Invalid tuning profile");}
    }

    private void AddPath(TableLayoutPanel panel, int col, string label, TextBox box)
    {
        panel.Controls.Add(new Label { Text=label, Dock=DockStyle.Fill, ForeColor=Theme.Muted, TextAlign=ContentAlignment.MiddleLeft },col,0);
        var wrap=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,BackColor=Theme.Panel}; wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,72));
        wrap.Controls.Add(box,0,0); var b=ToolkitUi.Button("Browse"); b.AutoSize=false; b.Width=66; b.Click+=(_,_)=>Browse(box); wrap.Controls.Add(b,1,0); panel.Controls.Add(wrap,col+1,0);
    }

    private void Browse(TextBox box){ using var d=new FolderBrowserDialog{SelectedPath=Directory.Exists(box.Text)?box.Text:""}; if(d.ShowDialog(this)==DialogResult.OK){box.Text=d.SelectedPath;PullPathSettings();ConfigureWatcher();} }
    private void PullPathSettings(){ settings.OsuDirectory=osuPath.Text.Trim(); settings.SongsDirectory=songsPath.Text.Trim(); settings.ReplayDirectory=replayPath.Text.Trim(); SaveSettings(); }
    private void SaveSettings()=>settings.Save(settingsPath);
    private void SetStatus(string s){ if(InvokeRequired){BeginInvoke(new Action<string>(SetStatus),s);return;} status.Text=s; }
    private void ClearResults(){ originalProf.Text=tunedProf.Text=originalPerf.Text=tunedPerf.Text=demandLabel.Text="—"; transitionGrid.Rows.Clear(); profBreakdown.Rows.Clear(); perfBreakdown.Rows.Clear(); debugMetricGrid.Rows.Clear(); debugPath.SetData(null,null,null); }

    private static TabPage Page(string title)=>new(title){BackColor=Theme.Background,Padding=new Padding(0)};
    private static TextBox PathBox()=>new(){Dock=DockStyle.Fill,BackColor=Theme.Panel2,ForeColor=Theme.Text,BorderStyle=BorderStyle.FixedSingle};
    private static CheckBox DarkCheck(string text)=>new(){Text=text,AutoSize=true,ForeColor=Theme.Text,Margin=new Padding(8,6,4,4)};
    private static ComboBox Combo(params string[] items)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Height = 28, BackColor = Theme.Panel2, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
        c.Items.AddRange(items);
        if (c.Items.Count > 0) c.SelectedIndex = 0;
        return c;
    }

    private static NumericUpDown Num(decimal min, decimal max, decimal value, int decimals)
    {
        if (max < min) (min, max) = (max, min);
        var n = new NumericUpDown
        {
            DecimalPlaces = decimals,
            BackColor = Theme.Panel2,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Width = 70
        };

        // NumericUpDown starts with Minimum=0, Maximum=100, Value=0. Establish a
        // temporary range containing zero first, then apply the requested range, then
        // clamp the value. This is safe even when the requested minimum is > 0.
        n.Minimum = Math.Min(min, 0m);
        n.Maximum = Math.Max(max, 0m);
        n.Minimum = min;
        n.Maximum = max;
        n.Value = Math.Clamp(value, min, max);
        return n;
    }
    private static Label BigValue(float size=18)=>new(){Dock=DockStyle.Fill,ForeColor=Theme.Text,Font=new Font("Segoe UI Semibold",size),TextAlign=ContentAlignment.MiddleLeft,Text="—"};
    private static Control Card(string title,Label value){var p=new Panel{Dock=DockStyle.Fill,BackColor=Theme.Panel,Margin=new Padding(0,0,8,0),Padding=new Padding(12,10,12,8)};p.Resize+=(_,_)=>ToolkitUi.RoundControl(p,10);p.Controls.Add(value);p.Controls.Add(new Label{Text=title,Dock=DockStyle.Top,Height=20,ForeColor=Theme.Muted,Font=new Font("Segoe UI Semibold",8f)});return p;}
}
