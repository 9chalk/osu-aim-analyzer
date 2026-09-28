using System.Globalization;
using System.Text;

namespace OsuAimAnalyzer;

public sealed class MainForm : Form
{
    private readonly AppPaths paths;
    private readonly AppSettings settings;
    private readonly AnalyzerDatabase database;
    private readonly BeatmapResolver resolver;
    private readonly CollectionRuleService collectionService;
    private ReplayProcessor? processor;

    private readonly GraphControl graph = new() { Dock = DockStyle.Fill };
    private readonly DataGridView playsGrid = new();
    private readonly CheckBox followLatestPlay = DarkCheck("Follow newest play");
    private readonly PlayPerformanceCard playInspectorHero = new() { Dock = DockStyle.Fill };
    private readonly SongTimelineControl playInspectorTimeline = new() { Dock = DockStyle.Fill };
    private readonly AimErrorProfileControl playInspectorErrorProfile = new() { Dock = DockStyle.Fill };
    private readonly AimCauseProfileControl playInspectorCauseProfile = new() { Dock = DockStyle.Fill };
    private static TextBox InspectorInsightBox() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = new Font("Segoe UI", 9.25f), TabStop = false
    };
    private readonly TextBox playInspectorOverview = InspectorInsightBox();
    private readonly TextBox playInspectorTraining = InspectorInsightBox();
    private readonly TextBox playInspectorComparison = InspectorInsightBox();
    private readonly TextBox playInspectorErrorsText = InspectorInsightBox();
    private readonly TextBox playInspectorDiagnosis = InspectorInsightBox();
    private readonly DataGridView playInspectorHistory = new();
    private readonly NumericUpDown playInspectorErrorCount = Num(3, 30, 10);
    private readonly Button playInspectorErrors = ToolkitUi.Button("Top errors");
    private readonly Button playInspectorAdvanced = ToolkitUi.Button("Advanced");
    private readonly Panel playInspectorPageHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Panel playInspectorTopErrorsHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Panel playInspectorAdvancedHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Dictionary<string, Control> playInspectorPages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> playInspectorNavButtons = new(StringComparer.OrdinalIgnoreCase);
    private string playInspectorPage = "Overview";
    private AdvancedDiagnosticsForm? embeddedDiagnosticsForm;
    private ErrorVisualizerForm? embeddedErrorVisualizer;
    private int embeddedErrorCount = -1;
    private long? inspectorPlayId;
    private long? inspectorRenderedPlayId;
    private long? inspectorDiagnosisRenderedPlayId;
    private string inspectorBackgroundKey = "";
    private int dataRevision;
    private long dataFingerprint;
    private int diagnosticsRevision = -1, trainingRevision = -1, aimAnalysisRevision = -1;
    private string diagnosticsSignature = "", trainingSignature = "", aimAnalysisSignature = "";
    private readonly ToolStripStatusLabel statusLabel = new("Starting…");
    private readonly ToolStripStatusLabel indexLabel = new("Beatmaps: —");
    private readonly ComboBox timeRange = Combo("Latest session", "Today", "7 days", "30 days", "All time");
    private readonly ComboBox xAxis = Combo("Time", "Star rating", "AR", "BPM", "Spacing", "Density", "Aim challenge", "Accuracy", "Miss count");
    private readonly ComboBox yAxis = Combo("Proficiency", "Play aim performance", "Aim rating", "PP estimate", "Straightness", "Landing", "Arrival", "Stability", "Deceleration", "Ideal path match", "Aim tension");
    private readonly ComboBox grouping = Combo("Play points", "Daily average");
    private readonly ComboBox gradeFilter = Combo("Any grade", "SS", "S", "A", "B", "C", "D");
    private readonly ComboBox tableSort = Combo("Time newest", "Aim performance highest", "PP estimate highest", "Proficiency highest", "Aim tension highest", "Misses lowest", "Grade highest");
    private readonly CheckBox trendProficiency = DarkCheck("Prof");
    private readonly CheckBox trendAim = DarkCheck("Aim perf");
    private readonly CheckBox trendPp = DarkCheck("PP est");
    private readonly CheckBox trendTension = DarkCheck("Tension×10");

    private readonly NumericUpDown bpmMin = Num(0, 500, 0), bpmMax = Num(0, 500, 500);
    private readonly NumericUpDown spacingMin = Num(0, 600, 0), spacingMax = Num(0, 600, 600);
    private readonly NumericUpDown starMin = Num(0, 20, 0, 1), starMax = Num(0, 20, 20, 1);
    private readonly NumericUpDown densityMin = Num(0, 20, 0, 1), densityMax = Num(0, 20, 20, 1);
    private readonly NumericUpDown missMin = Num(0, 999, 0), missMax = Num(0, 999, 999);

    private readonly Label cardPlays = CardValue();
    private readonly Label cardTransitions = CardValue();
    private readonly Label cardProficiency = CardValue();
    private readonly Label cardAimRating = CardValue();
    private readonly Label cardPp = CardValue();
    private readonly Label cardWeakest = CardValue(13);
    private readonly Label cardZone = CardValue(13);
    private static TextBox InsightBox() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9.5f), TabStop = false
    };
    private readonly TextBox diagnosticOverview = InsightBox();
    private readonly TextBox diagnosticMechanics = InsightBox();
    private readonly TextBox diagnosticDemand = InsightBox();
    private readonly TextBox diagnosticAction = InsightBox();


    private readonly DataGridView diagnosticGrid = new();
    private readonly DataGridView trainingGrid = new();
    private readonly DataGridView trainingMapGrid = new();
    private readonly DataGridView trainingMapListGrid = new();
    private readonly TextBox trainingDetail = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9.5f), TabStop = false
    };
    private readonly ComboBox diagnosticRange = Combo("All time", "30 days", "7 days", "Today");
    private readonly ComboBox diagnosticPreset = Combo(DiagnosticsEngine.PresetNames);
    private readonly Label diagnosticPresetHelp = new() { AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(12, 7, 0, 0) };
    private readonly NumericUpDown diagnosticMinSamples = Num(10, 1000, 40);
    private readonly ComboBox trainingDimension = Combo("Spacing", "BPM", "Density");
    private readonly ComboBox trainingRange = Combo("All time", "30 days", "7 days");
    private readonly ComboBox trainingPreset = Combo(DiagnosticsEngine.PresetNames);
    private readonly Label trainingPresetHelp = new() { AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(12, 7, 0, 0) };
    private readonly NumericUpDown trainingLeniency = Num(1, 10, 5);
    private readonly TextBox trainingQuota = InsightBox();
    private readonly ComboBox aimAnalysisRange = Combo("All time", "30 days", "7 days", "Today", "Latest session");
    private readonly ComboBox aimAnalysisGrouping = Combo("Play by play", "Daily average");
    private readonly RadarChartControl aimLifetimeRadar = new() { Dock = DockStyle.Fill, CenterLabel = "aim fingerprint" };
    private readonly AimErrorProfileControl aimLifetimeErrorProfile = new() { Dock = DockStyle.Fill };
    private readonly AimCauseProfileControl aimLifetimeCauseProfile = new() { Dock = DockStyle.Fill };
    private readonly AimMetricCardControl aimCenteringMetric = new(AimMetricKind.Centering) { Dock = DockStyle.Fill };
    private readonly AimMetricCardControl aimArrivalMetric = new(AimMetricKind.Arrival) { Dock = DockStyle.Fill };
    private readonly AimMetricCardControl aimStraightnessMetric = new(AimMetricKind.Straightness) { Dock = DockStyle.Fill };
    private readonly AimMetricCardControl aimStabilityMetric = new(AimMetricKind.Stability) { Dock = DockStyle.Fill };
    private readonly AimMetricCardControl aimBrakingMetric = new(AimMetricKind.Braking) { Dock = DockStyle.Fill };
    private readonly AimMetricCardControl aimPathMetric = new(AimMetricKind.IdealPath) { Dock = DockStyle.Fill };
    private readonly AimMetricCardControl aimRawSkillMetric = new(AimMetricKind.RawAim) { Dock = DockStyle.Fill };
    private readonly GraphControl aimPerformanceHistory = new() { Dock = DockStyle.Fill };
    private readonly GraphControl aimProficiencyHistory = new() { Dock = DockStyle.Fill };
    private readonly GraphControl aimErrorHistory = new() { Dock = DockStyle.Fill };
    private readonly GraphControl aimErrorMixHistory = new() { Dock = DockStyle.Fill };
    private readonly GraphControl aimCauseHistory = new() { Dock = DockStyle.Fill };
    private readonly GraphControl aimMechanicsHistory = new() { Dock = DockStyle.Fill };
    private readonly DataGridView aimMechanicGrid = new();
    private readonly TextBox aimAnalysisSummary = InsightBox();
    private readonly Label aimPlayCountCard = CardValue();
    private readonly Label aimProfCard = CardValue();
    private readonly Label aimPerformanceCard = CardValue();
    private readonly Label aimErrorCard = CardValue();
    private readonly Label aimDominantErrorCard = CardValue(12);
    private readonly ComboBox aimContextRange = Combo("All time", "30 days", "7 days", "Today", "Latest session");
    private readonly ListBox aimContextCauseList = new();
    private readonly DataGridView aimContextMetricGrid = new();
    private readonly DataGridView aimContextMapGrid = new();
    private readonly TextBox aimContextSummary = InsightBox();
    private readonly Label aimContextCauseCard = CardValue(13);
    private readonly Label aimContextRateCard = CardValue(13);
    private readonly Label aimContextMapsCard = CardValue(13);
    private readonly ListBox aimContextCategoryList = new();
    private readonly DiagnosisCoachControl aimContextDiagnosis = new() { Dock = DockStyle.Fill };
    private readonly DataGridView aimContextDiagnosisGrid = new();
    private readonly ComboBox insightsRange = Combo("All time", "90 days", "30 days", "7 days");
    private readonly ComboBox insightsCategory = Combo("All", "Improvement", "Training response", "Mod effects", "Aim quirks", "Strengths", "Weaknesses", "Anomalies");
    private readonly ListBox insightsList = new();
    private readonly RichTextBox insightsDetail = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10f) };
    private readonly GraphControl insightsGraph = new() { Dock = DockStyle.Fill };
    private readonly Label insightsCountCard = CardValue(13);
    private readonly Label insightsTopCard = CardValue(13);
    private List<PlayerInsight> currentInsights = new();
    private int insightsRevision = -1;
    private string insightsSignature = "";
    private LifetimeAimAnalysisData? currentAimAnalysisData;
    private readonly List<string> aimContextCauseKeys = new();
    private readonly List<string> aimContextCategoryKeys = new();
    private readonly Dictionary<string, AimErrorContextResult> aimContextResultCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AimCategoryDiagnosis> aimContextDiagnosisCache = new(StringComparer.OrdinalIgnoreCase);
    private bool syncingAimContextRange;
    private bool aimAdvancedVisible;
    private TabControl? mainTabs;
    private TabControl? aimAnalysisTabs;
    private ReplayToolsForm? replayToolsForm;
    private List<DiagnosticCohort> currentCohorts = new();
    private List<TrainingRecommendation> currentTraining = new();

    private readonly DataGridView collectionRulesGrid = new();
    private readonly CheckBox autoCollectionCheck = DarkCheck("Route new plays into collections automatically");
    private readonly CheckBox collectionExclusiveCheck = DarkCheck("Exclusive ranges · move a map out of other analyzer-managed bands when its latest proficiency changes");
    private readonly Label collectionDbInfo = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9f), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox collectionActivity = InsightBox();
    private readonly Label collectionStatus = new() { AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 9.5f), Padding = new Padding(0, 5, 0, 0) };
    private readonly ComboBox collectionBackfillRange = Combo("Today", "7 days", "30 days", "90 days", "All time");
    private readonly Label collectionBackfillPreview = new() { AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(10, 7, 0, 0) };

    private readonly TextBox osuPath = PathBox();
    private readonly TextBox songsPath = PathBox();
    private readonly TextBox replayPath = PathBox();
    private readonly CheckBox watchCheck = DarkCheck("Watch Data/r automatically");
    private readonly CheckBox pureJumpCheck = DarkCheck("Analyze pure circle→circle jumps only");
    private readonly NumericUpDown sessionGap = Num(10, 180, 45);
    private readonly ComboBox replayBackfill = Combo("12 hours", "3 days", "7 days", "30 days", "90 days", "All time");

    private List<PlayRow> allPlays = new();
    private bool busy;

    public MainForm(AppPaths paths, AppSettings settings, AnalyzerDatabase database)
    {
        this.paths = paths;
        this.settings = settings;
        this.database = database;
        resolver = new BeatmapResolver(settings);
        collectionService = new CollectionRuleService(settings, paths);

        Text = "osu! Aim Analyzer v31";
        Width = 1540; Height = 980; MinimumSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        UpdateStyles();
        followLatestPlay.Checked = true;

        Controls.Add(BuildTabs());
        Controls.Add(BuildStatusBar());
        LoadSettingsIntoUi();
        ConfigureGrid();
        WireEvents();
        Shown += async (_, _) => await InitializeAsync();
        FormClosed += (_, _) =>
        {
            ClearEmbeddedInspectorTools();
            try { replayToolsForm?.Close(); } catch { }
            processor?.Dispose();
            collectionService.Dispose();
            playInspectorHero.SetBackgroundImage(null);
        };
    }

    private Control BuildTabs()
    {
        var tabs = ToolkitUi.Tabs();
        mainTabs = tabs;
        var dash = new TabPage("Dashboard") { BackColor = Theme.Background };
        var aimAnalysis = new TabPage("Aim analysis") { BackColor = Theme.Background };
        var insights = new TabPage("Insights") { BackColor = Theme.Background };
        var replayTools = new TabPage("Replay tools") { BackColor = Theme.Background };
        var collections = new TabPage("Collections") { BackColor = Theme.Background };
        var settingsTab = new TabPage("Settings / Import") { BackColor = Theme.Background };
        dash.Controls.Add(BuildDashboard());
        // Diagnostics and Training remain intentionally hidden. Their engines and UI builders
        // remain in source so they can be brought back later without losing the underlying work.
        aimAnalysis.Controls.Add(BuildAimAnalysisTab());
        insights.Controls.Add(BuildInsightsTab());
        replayToolsForm = new ReplayToolsForm(settings, paths.SettingsPath, resolver)
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            Dock = DockStyle.Fill
        };
        replayTools.Controls.Add(replayToolsForm);
        replayToolsForm.Show();
        collections.Controls.Add(BuildCollectionsTab());
        settingsTab.Controls.Add(BuildSettings());
        tabs.TabPages.Add(dash); tabs.TabPages.Add(aimAnalysis); tabs.TabPages.Add(insights); tabs.TabPages.Add(replayTools); tabs.TabPages.Add(collections); tabs.TabPages.Add(settingsTab);
        tabs.SelectedIndexChanged += (_, _) =>
        {
            string name = tabs.SelectedTab?.Text ?? "";
            if (name == "Aim analysis") RefreshAimAnalysisTab();
            else if (name == "Insights") RefreshInsightsTab();
            else if (name == "Collections") RefreshCollectionTab();
        };
        return tabs;
    }

    private Control BuildDashboard()
    {
        // The dashboard used to reserve a fixed percentage for the graph, which made the
        // bottom panes cramped on 1080p displays.  A SplitContainer lets the user decide
        // how much space the trend chart deserves for the current task.
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Background, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildFilters(), 0, 0);
        root.Controls.Add(BuildCards(), 0, 1);

        root.Controls.Add(BuildDashboardWorkspace(), 0, 2);
        return root;
    }

    private Control BuildDashboardWorkspace()
    {
        // v22: the recent/selected play is a full-height right rail.  This is the space that
        // used to belong to the right side of the trend chart plus the old small inspector.
        // It leaves enough vertical room for the performance card and all three aligned curves.
        var outer = CreateDashboardSplitter(Orientation.Vertical);
        // Keep construction-time minimums small; WinForms validates them against the
        // SplitContainer's tiny pre-layout size. The initial ratio still gives the right rail
        // the intended width once the form has real dimensions.
        outer.Panel1MinSize = 40;
        outer.Panel2MinSize = 40;

        var left = CreateDashboardSplitter(Orientation.Horizontal);
        left.Panel1MinSize = 40;
        left.Panel2MinSize = 40;
        left.Panel1.Controls.Add(ToolkitUi.Wrap(graph, "Trend · drag the divider below to resize"));
        left.Panel2.Controls.Add(ToolkitUi.Wrap(playsGrid, "Analyzed plays · click to inspect · double-click for full diagnostics"));
        SetInitialSplit(left, 0.48);

        outer.Panel1.Controls.Add(left);
        outer.Panel2.Controls.Add(BuildPlayInspector());
        SetInitialSplit(outer, 0.55);
        return outer;
    }

    private Control BuildPlayInspector()
    {
        // Keep the hero on Overview. Readout/tool pages use the full inner height.
        ConfigurePlayInspectorHistoryGrid();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Background,
            Padding = new Padding(0, 0, 0, 8), Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(playInspectorHero, 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background,
            Margin = new Padding(0, 7, 0, 0), Padding = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(BuildPlayInspectorNavigation(), 0, 0);
        body.Controls.Add(playInspectorPageHost, 1, 0);
        root.Controls.Add(body, 0, 1);

        playInspectorPages.Clear();
        playInspectorPages["Overview"] = BuildInspectorOverviewPage();
        playInspectorPages["Summary"] = ToolkitUi.Wrap(playInspectorOverview, "Plain-English run summary");
        playInspectorPages["Training"] = ToolkitUi.Wrap(playInspectorTraining, "Training readout for this play");
        playInspectorPages["Compare"] = ToolkitUi.Wrap(playInspectorComparison, "Comparison with nearby attempts");
        playInspectorPages["Errors"] = ToolkitUi.Wrap(playInspectorErrorsText, "Error interpretation");
        playInspectorPages["Diagnosis"] = ToolkitUi.Wrap(playInspectorDiagnosis, "Objective diagnosis · this run vs your similar-map history");
        playInspectorPages["Top errors"] = BuildInspectorTopErrorsPage();
        playInspectorPages["Advanced"] = playInspectorAdvancedHost;

        foreach (var page in playInspectorPages.Values)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            page.Margin = Padding.Empty;
            playInspectorPageHost.Controls.Add(page);
        }

        playInspectorErrors.Enabled = false;
        playInspectorAdvanced.Enabled = false;
        playInspectorOverview.Text = "Finish a play, or click one in the table, to inspect it here.";
        playInspectorHero.SetPlay(null);
        playInspectorTimeline.Clear();
        playInspectorCauseProfile.Clear();
        playInspectorErrorProfile.Clear();
        ShowInspectorPage("Overview");
        return root;
    }

    private Control BuildPlayInspectorNavigation()
    {
        // v28: keep the rail purely navigational.  The old bottom footer made the
        // Follow-latest checkbox and error-count spinner look like a detached UI artifact.
        // Follow latest now sits with the navigation, while Top-error count lives on the
        // Top errors page where it is actually relevant.
        var nav = new Panel
        {
            Dock = DockStyle.Fill, BackColor = Theme.Panel,
            Padding = new Padding(7, 9, 7, 8), Margin = Padding.Empty
        };
        nav.Resize += (_, _) => ToolkitUi.RoundControl(nav, 9);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = Theme.Panel, Margin = Padding.Empty, Padding = Padding.Empty
        };

        Button AddNav(string key, string text)
        {
            var b = ToolkitUi.Button(text);
            b.AutoSize = false; b.Width = 90; b.Height = 34; b.Margin = new Padding(0, 0, 0, 5);
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Padding = new Padding(9, 0, 3, 0);
            b.Click += (_, _) => ShowInspectorPage(key);
            playInspectorNavButtons[key] = b;
            buttons.Controls.Add(b);
            return b;
        }

        AddNav("Overview", "Overview");
        followLatestPlay.Text = "Follow latest";
        followLatestPlay.AutoSize = false;
        followLatestPlay.Width = 90;
        followLatestPlay.Height = 28;
        followLatestPlay.Margin = new Padding(3, 0, 0, 8);
        buttons.Controls.Add(followLatestPlay);

        AddNav("Summary", "Summary");
        AddNav("Training", "Training");
        AddNav("Compare", "Compare");
        AddNav("Errors", "Errors");
        AddNav("Diagnosis", "Diagnosis");

        var divider = new Label
        {
            Text = "TOOLS", AutoSize = false, Width = 90, Height = 24, ForeColor = Theme.Muted,
            Font = new Font("Segoe UI Semibold", 7.8f), TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(7, 0, 0, 3), Margin = new Padding(0, 5, 0, 4)
        };
        buttons.Controls.Add(divider);

        playInspectorErrors.AutoSize = false; playInspectorErrors.Width = 90; playInspectorErrors.Height = 34;
        playInspectorErrors.Margin = new Padding(0, 0, 0, 5); playInspectorErrors.TextAlign = ContentAlignment.MiddleLeft;
        playInspectorErrors.Padding = new Padding(9, 0, 3, 0);
        playInspectorNavButtons["Top errors"] = playInspectorErrors;
        buttons.Controls.Add(playInspectorErrors);

        playInspectorAdvanced.AutoSize = false; playInspectorAdvanced.Width = 90; playInspectorAdvanced.Height = 34;
        playInspectorAdvanced.Margin = new Padding(0, 0, 0, 5); playInspectorAdvanced.TextAlign = ContentAlignment.MiddleLeft;
        playInspectorAdvanced.Padding = new Padding(9, 0, 3, 0);
        playInspectorNavButtons["Advanced"] = playInspectorAdvanced;
        buttons.Controls.Add(playInspectorAdvanced);

        nav.Controls.Add(buttons);
        return nav;
    }

    private Control BuildInspectorTopErrorsPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = Theme.Background, Margin = Padding.Empty, Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Theme.Panel,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(10, 8, 8, 5), Margin = new Padding(0, 0, 0, 7)
        };
        tools.Controls.Add(new Label
        {
            Text = "Worst transitions", AutoSize = true, ForeColor = Theme.Muted,
            Font = new Font("Segoe UI Semibold", 8.5f), Padding = new Padding(0, 7, 4, 0)
        });
        playInspectorErrorCount.Width = 60;
        tools.Controls.Add(playInspectorErrorCount);
        var regenerate = ToolkitUi.Button("Generate / refresh");
        regenerate.Click += async (_, _) => await GenerateInspectorErrorsAsync();
        tools.Controls.Add(regenerate);

        root.Controls.Add(tools, 0, 0);
        root.Controls.Add(playInspectorTopErrorsHost, 0, 1);
        return root;
    }

    private Control BuildInspectorOverviewPage()
    {
        // v28: Runs are useful enough to deserve permanent context. Put the exact-difficulty
        // comparison back underneath the visual diagnostics, but keep the new vertical rail for
        // alternate pages/tools. The previously empty lower area is used for the comparison table.
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background, Margin = Padding.Empty };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 892, ColumnCount = 1, RowCount = 4,
            BackColor = Theme.Background, Margin = Padding.Empty, Padding = Padding.Empty
        };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 205));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 203));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));

        var timelineShell = ToolkitUi.Wrap(playInspectorTimeline, "Song timeline · proficiency / aim performance / map difficulty");
        timelineShell.Margin = Padding.Empty;
        stack.Controls.Add(timelineShell, 0, 0);

        var causeShell = ToolkitUi.Wrap(playInspectorCauseProfile, "Why control broke · inferred movement cause");
        causeShell.Margin = new Padding(0, 7, 0, 0);
        stack.Controls.Add(causeShell, 0, 1);

        var errorShell = ToolkitUi.Wrap(playInspectorErrorProfile, "Where the cursor ended · centering / overaim / underaim / lateral");
        errorShell.Margin = new Padding(0, 7, 0, 0);
        stack.Controls.Add(errorShell, 0, 2);

        var runsShell = ToolkitUi.Wrap(playInspectorHistory, "Runs on this exact difficulty · current + nearby attempts");
        runsShell.Margin = new Padding(0, 7, 0, 0);
        stack.Controls.Add(runsShell, 0, 3);

        scroll.Controls.Add(stack);
        return scroll;
    }

    private void ShowInspectorPage(string key)
    {
        if (!playInspectorPages.TryGetValue(key, out var target)) return;
        if (playInspectorPage.Equals(key, StringComparison.OrdinalIgnoreCase) && target.Visible) return;
        playInspectorPage = key;
        bool overview = key.Equals("Overview", StringComparison.OrdinalIgnoreCase);
        if (playInspectorHero.Parent is TableLayoutPanel inspector)
        {
            inspector.RowStyles[0].Height = overview ? 154 : 0;
            playInspectorHero.Visible = overview;
        }
        playInspectorPageHost.SuspendLayout();
        try
        {
            foreach (var kv in playInspectorPages)
                kv.Value.Visible = ReferenceEquals(kv.Value, target);
            target.BringToFront();
        }
        finally { playInspectorPageHost.ResumeLayout(true); }

        foreach (var kv in playInspectorNavButtons)
        {
            bool active = kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
            kv.Value.BackColor = active ? Theme.Popup : Theme.Panel2;
            kv.Value.ForeColor = active ? Theme.Text : Theme.Muted;
            kv.Value.FlatAppearance.BorderColor = active ? Theme.Accent : Theme.Border;
        }
        if (key.Equals("Diagnosis", StringComparison.OrdinalIgnoreCase))
            EnsureInspectorDiagnosis();
    }

    private void EnsureInspectorDiagnosis()
    {
        if (!inspectorPlayId.HasValue || inspectorDiagnosisRenderedPlayId == inspectorPlayId.Value) return;
        var play = allPlays.FirstOrDefault(p => p.Id == inspectorPlayId.Value) ?? database.LoadPlay(inspectorPlayId.Value);
        if (play == null) return;
        try
        {
            playInspectorDiagnosis.Text = "Analyzing this run against your similar-map history…";
            var transitions = database.LoadTransitions(play.Id);
            var history = allPlays.Any(p => p.Id == play.Id) ? allPlays : database.LoadPlays();
            playInspectorDiagnosis.Text = AimTrainingDiagnosisEngine.BuildRunDiagnosis(play, transitions, database, history);
            inspectorDiagnosisRenderedPlayId = play.Id;
        }
        catch (Exception ex)
        {
            playInspectorDiagnosis.Text = "Could not build objective run diagnosis: " + ex.Message;
        }
    }

    private void ClearEmbeddedInspectorTools()
    {
        try { embeddedDiagnosticsForm?.Close(); embeddedDiagnosticsForm?.Dispose(); } catch { }
        try { embeddedErrorVisualizer?.Close(); embeddedErrorVisualizer?.Dispose(); } catch { }
        embeddedDiagnosticsForm = null;
        embeddedErrorVisualizer = null;
        embeddedErrorCount = -1;
        playInspectorAdvancedHost.Controls.Clear();
        playInspectorTopErrorsHost.Controls.Clear();
    }

    private void ShowInspectorAdvancedInline()
    {
        if (!inspectorPlayId.HasValue) return;
        var play = allPlays.FirstOrDefault(p => p.Id == inspectorPlayId.Value) ?? database.LoadPlay(inspectorPlayId.Value);
        if (play == null) return;

        ShowInspectorPage("Advanced");
        if (embeddedDiagnosticsForm != null) return;
        playInspectorAdvancedHost.Controls.Clear();
        embeddedDiagnosticsForm = new AdvancedDiagnosticsForm(play, database, resolver, settings)
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            Dock = DockStyle.Fill,
            MinimumSize = Size.Empty
        };
        playInspectorAdvancedHost.Controls.Add(embeddedDiagnosticsForm);
        embeddedDiagnosticsForm.Show();
    }

    private static SplitContainer CreateDashboardSplitter(Orientation orientation)
    {
        return new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = orientation,
            SplitterWidth = 7,
            BackColor = Theme.Border,
            BorderStyle = BorderStyle.None,
            IsSplitterFixed = false,
            FixedPanel = FixedPanel.None,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Panel1 = { BackColor = Theme.Background, Padding = Padding.Empty },
            Panel2 = { BackColor = Theme.Background, Padding = Padding.Empty }
        };
    }

    private static void SetInitialSplit(SplitContainer split, double ratio)
    {
        bool initialized = false;
        split.SizeChanged += (_, _) =>
        {
            if (initialized) return;
            int total = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
            if (total <= 0) return;
            int desired = (int)Math.Round(total * ratio);
            if (!ToolkitUi.SetSplitterDistanceSafe(split, desired)) return;
            initialized = true;
        };
    }

    private void ConfigurePlayInspectorHistoryGrid()
    {
        ToolkitUi.StyleDataGrid(playInspectorHistory);
        // This grid lives in a compact inspector, so the global 72 px wrapped header
        // wastes nearly the whole pane and can make the first data row look missing.
        playInspectorHistory.ColumnHeadersHeight = 42;
        playInspectorHistory.ColumnHeadersDefaultCellStyle.Padding = new Padding(3, 4, 3, 4);
        playInspectorHistory.RowTemplate.Height = 27;
        playInspectorHistory.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        playInspectorHistory.RowHeadersVisible = false;
        playInspectorHistory.AllowUserToResizeRows = false;
        playInspectorHistory.MultiSelect = false;
        playInspectorHistory.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        playInspectorHistory.ScrollBars = ScrollBars.Both;
        playInspectorHistory.Columns.Clear();
        playInspectorHistory.Columns.Add("run", "Run");
        playInspectorHistory.Columns.Add("mods", "Mods");
        playInspectorHistory.Columns.Add("acc", "Acc");
        playInspectorHistory.Columns.Add("miss", "Miss");
        playInspectorHistory.Columns.Add("prof", "Prof. / tier");
        playInspectorHistory.Columns.Add("dprof", "Δ Prof.");
        playInspectorHistory.Columns.Add("aim", "Aim perf.");
        playInspectorHistory.Columns.Add("daim", "Δ Aim");
        playInspectorHistory.Columns.Add("tension", "Tension");
        int[] widths = { 82, 42, 54, 36, 92, 52, 54, 50, 62 };
        for (int i = 0; i < widths.Length; i++) playInspectorHistory.Columns[i].Width = widths[i];
        playInspectorHistory.Columns["dprof"].HeaderCell.ToolTipText = "Change in proficiency versus the immediately previous attempt on this exact difficulty.";
        playInspectorHistory.Columns["daim"].HeaderCell.ToolTipText = "Change in play aim performance versus the immediately previous attempt.";
    }

    private Control BuildDiagnosticsTab()
    {
        ConfigureDiagnosticGrid();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Background, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 9, 8, 6), WrapContents = true, AutoScroll = true };
        diagnosticPreset.Width = 205;
        controls.Controls.Add(LabelFor("Skill preset")); controls.Controls.Add(diagnosticPreset);
        controls.Controls.Add(LabelFor("History")); controls.Controls.Add(diagnosticRange);
        controls.Controls.Add(LabelFor("Min samples")); controls.Controls.Add(diagnosticMinSamples);
        var refresh = ToolkitUi.Button("Recalculate diagnostics"); refresh.Click += (_, _) => RefreshDiagnosticsTab(); controls.Controls.Add(refresh);
        diagnosticPresetHelp.Text = DiagnosticsEngine.PresetDescription(diagnosticPreset.SelectedItem?.ToString() ?? "All aim");
        diagnosticPresetHelp.MaximumSize = new Size(1180, 0);
        controls.Controls.Add(diagnosticPresetHelp);
        root.Controls.Add(controls, 0, 0);
        root.Controls.Add(ToolkitUi.Wrap(diagnosticGrid, "Map-type diagnostics · skill cohorts, stars, AR, tension and limiting mechanics"), 0, 1);
        root.Controls.Add(ToolkitUi.Wrap(BuildDiagnosticDetailTabs(), "Selected cohort"), 0, 2);
        return root;
    }

    private Control BuildDiagnosticDetailTabs()
    {
        var tabs = ToolkitUi.Tabs();
        tabs.ItemSize = new Size(138, 30);
        tabs.Padding = new Point(12, 4);
        foreach (var item in new[] { ("Overview", diagnosticOverview), ("Mechanics", diagnosticMechanics), ("Demand / context", diagnosticDemand), ("What to do", diagnosticAction) })
        {
            var page = new TabPage(item.Item1) { BackColor = Theme.Panel, Padding = new Padding(8) };
            page.Controls.Add(item.Item2);
            tabs.TabPages.Add(page);
        }
        return tabs;
    }

    private Control BuildTrainingTab()
    {
        ConfigureTrainingGrid();
        ConfigureTrainingMapGrid();
        ConfigureTrainingMapListGrid();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Background, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 67));

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 9, 8, 6), WrapContents = true, AutoScroll = true };
        trainingPreset.Width = 205;
        controls.Controls.Add(LabelFor("Skill preset")); controls.Controls.Add(trainingPreset);
        controls.Controls.Add(LabelFor("Train by")); controls.Controls.Add(trainingDimension);
        controls.Controls.Add(LabelFor("History")); controls.Controls.Add(trainingRange);
        controls.Controls.Add(LabelFor("Recommendation leniency")); controls.Controls.Add(trainingLeniency);
        var refresh = ToolkitUi.Button("Generate recommendations"); refresh.Click += (_, _) => RefreshTrainingTab(); controls.Controls.Add(refresh);
        controls.Controls.Add(new Label { Text = "1 = very strict matches · 10 = broad useful matches", AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(8, 7, 0, 0) });
        trainingPresetHelp.Text = DiagnosticsEngine.PresetGuideline(trainingPreset.SelectedItem?.ToString() ?? "All aim");
        trainingPresetHelp.MaximumSize = new Size(1250, 0);
        controls.Controls.Add(trainingPresetHelp);
        root.Controls.Add(controls, 0, 0);

        root.Controls.Add(ToolkitUi.Wrap(trainingMapGrid, "Top map recommendations from your own history · best fit first"), 0, 1);

        var lowerTabs = ToolkitUi.Tabs();
        lowerTabs.ItemSize = new Size(180, 32);
        var targets = new TabPage("Training targets") { BackColor = Theme.Background, Padding = new Padding(6) };
        var targetLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Theme.Background };
        targetLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        targetLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        targetLayout.Controls.Add(ToolkitUi.Wrap(trainingGrid, "Recommended parameter bands"), 0, 0);
        targetLayout.Controls.Add(ToolkitUi.Wrap(trainingDetail, "Simple guideline + why this target"), 0, 1);
        targets.Controls.Add(targetLayout);
        var mapList = new TabPage("Map recommendations") { BackColor = Theme.Background, Padding = new Padding(6) };
        mapList.Controls.Add(ToolkitUi.Wrap(trainingMapListGrid, "Long recommendation list · selected skill preset + leniency"));
        var quota = new TabPage("Session quota") { BackColor = Theme.Background, Padding = new Padding(6) };
        quota.Controls.Add(ToolkitUi.Wrap(trainingQuota, "Suggested session mix vs what you have done this session"));
        lowerTabs.TabPages.Add(targets); lowerTabs.TabPages.Add(mapList); lowerTabs.TabPages.Add(quota);
        root.Controls.Add(lowerTabs, 0, 2);
        return root;
    }

    private Control BuildFilters()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 10, 8, 8), WrapContents = false, AutoScroll = true };
        panel.Controls.Add(LabelFor("Range")); panel.Controls.Add(timeRange);
        panel.Controls.Add(LabelFor("X")); panel.Controls.Add(xAxis);
        panel.Controls.Add(LabelFor("Y")); panel.Controls.Add(yAxis);
        panel.Controls.Add(LabelFor("Group")); panel.Controls.Add(grouping);
        panel.Controls.Add(LabelFor("Grade")); panel.Controls.Add(gradeFilter);
        panel.Controls.Add(LabelFor("Table")); panel.Controls.Add(tableSort);
        panel.Controls.Add(Separator());

        var advancedButton = ToolkitUi.Button("Advanced filters ▾");
        advancedButton.AutoSize = true;
        var advancedDropDown = BuildAdvancedFilterDropDown();
        advancedButton.Click += (_, _) => advancedDropDown.Show(advancedButton, new Point(0, advancedButton.Height + 2));
        panel.Controls.Add(advancedButton);

        panel.Controls.Add(Separator());
        panel.Controls.Add(LabelFor("Trend overlays")); panel.Controls.Add(trendProficiency); panel.Controls.Add(trendAim); panel.Controls.Add(trendPp); panel.Controls.Add(trendTension);
        return panel;
    }

    private ToolStripDropDown BuildAdvancedFilterDropDown()
    {
        var content = new FlowLayoutPanel
        {
            BackColor = Theme.Panel, Padding = new Padding(12), WrapContents = true, AutoScroll = false,
            Width = 900, Height = 86, Margin = Padding.Empty
        };
        content.Controls.Add(LabelFor("Misses")); content.Controls.Add(missMin); content.Controls.Add(LabelFor("–")); content.Controls.Add(missMax);
        content.Controls.Add(LabelFor("BPM")); content.Controls.Add(bpmMin); content.Controls.Add(LabelFor("–")); content.Controls.Add(bpmMax);
        content.Controls.Add(LabelFor("Spacing px")); content.Controls.Add(spacingMin); content.Controls.Add(LabelFor("–")); content.Controls.Add(spacingMax);
        content.Controls.Add(LabelFor("★")); content.Controls.Add(starMin); content.Controls.Add(LabelFor("–")); content.Controls.Add(starMax);
        content.Controls.Add(LabelFor("Density")); content.Controls.Add(densityMin); content.Controls.Add(LabelFor("–")); content.Controls.Add(densityMax);

        var reset = ToolkitUi.Button("Reset ranges");
        reset.Click += (_, _) =>
        {
            missMin.Value = missMin.Minimum; missMax.Value = missMax.Maximum;
            bpmMin.Value = bpmMin.Minimum; bpmMax.Value = bpmMax.Maximum;
            spacingMin.Value = spacingMin.Minimum; spacingMax.Value = spacingMax.Maximum;
            starMin.Value = starMin.Minimum; starMax.Value = starMax.Maximum;
            densityMin.Value = densityMin.Minimum; densityMax.Value = densityMax.Maximum;
        };
        content.Controls.Add(reset);

        var host = new ToolStripControlHost(content) { AutoSize = false, Size = content.Size, Margin = Padding.Empty, Padding = Padding.Empty };
        var drop = new ToolStripDropDown { AutoClose = true, BackColor = Theme.Panel, Padding = new Padding(2), Margin = Padding.Empty };
        drop.Items.Add(host);
        return drop;
    }

    private Control BuildCards()
    {
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, BackColor = Theme.Background, Padding = new Padding(0, 8, 0, 8) };
        for (int i = 0; i < 7; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14.285f));
        cards.Controls.Add(Card("PLAYS", cardPlays), 0, 0);
        cards.Controls.Add(Card("AIM TRANSITIONS", cardTransitions), 1, 0);
        cards.Controls.Add(Card("AVG PROFICIENCY", cardProficiency), 2, 0);
        cards.Controls.Add(Card("AIM RATING · TOP 100 AVG", cardAimRating), 3, 0);
        cards.Controls.Add(Card("AVG PP EST.", cardPp), 4, 0);
        cards.Controls.Add(Card("WEAKEST MECHANIC", cardWeakest), 5, 0);
        cards.Controls.Add(Card("TRAINING ZONE", cardZone), 6, 0);
        return cards;
    }

    private Control BuildAimAnalysisTab()
    {
        var tabs = ToolkitUi.Tabs();
        aimAnalysisTabs = tabs;
        tabs.ItemSize = new Size(178, 34);
        tabs.Padding = new Point(14, 5);

        var profile = new TabPage("Profile") { BackColor = Theme.Background, Padding = Padding.Empty };
        profile.Controls.Add(BuildAimProfileTab());
        tabs.TabPages.Add(profile);

        var context = new TabPage("Error conditions") { BackColor = Theme.Background, Padding = Padding.Empty };
        context.Controls.Add(BuildAimErrorContextTab());
        tabs.TabPages.Add(context);

        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab?.Text == "Error conditions")
            {
                // Lifetime data is cached by history window. Entering this subtab should not
                // rebuild every graph/transition set; only materialize the selected condition.
                RefreshAimAnalysisTab();
                RefreshAimErrorContextSelection();
            }
            else if (tabs.SelectedTab?.Text == "Profile" && aimAdvancedVisible && currentAimAnalysisData != null)
            {
                // If history changed while Error conditions was open, refresh the hidden raw
                // graphs only when the user actually comes back to the expanded breakdown.
                RefreshAimAdvancedBreakdown(currentAimAnalysisData, aimAnalysisGrouping.SelectedItem?.ToString() ?? "Play by play");
            }
        };
        return tabs;
    }

    private Control BuildAimErrorContextTab()
    {
        ConfigureAimContextGrids();
        ConfigureAimDiagnosisGrid();
        ConfigureAimContextList(aimContextCauseList);
        ConfigureAimContextList(aimContextCategoryList);

        aimContextCauseList.SelectedIndexChanged += (_, _) =>
        {
            if (aimAnalysisTabs?.SelectedTab?.Text == "Error conditions")
                RefreshAimErrorContextSelection();
        };
        aimContextCategoryList.SelectedIndexChanged += (_, _) =>
        {
            if (aimAnalysisTabs?.SelectedTab?.Text == "Error conditions")
                RefreshAimCategoryDiagnosisSelection();
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Background,
            Padding = new Padding(14), Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Panel2,
            Padding = new Padding(16, 9, 12, 8), Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Panel2, Margin = Padding.Empty };
        titles.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        titles.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        titles.Controls.Add(new Label
        {
            Text = "⬡   error conditions",
            AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 14f)
        }, 0, 0);
        titles.Controls.Add(new Label
        {
            Text = "Find where an error happens, then turn the correlations into evidence-backed training guidance from your own controlled history.",
            AutoSize = true, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9f)
        }, 0, 1);
        header.Controls.Add(titles, 0, 0);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, BackColor = Theme.Panel2,
            Padding = new Padding(8, 4, 0, 0), Margin = Padding.Empty
        };
        filters.Controls.Add(LabelFor("History"));
        aimContextRange.Width = 116;
        filters.Controls.Add(aimContextRange);
        var refresh = ToolkitUi.Button("Refresh");
        refresh.Click += (_, _) => RefreshAimAnalysisTab(true);
        filters.Controls.Add(refresh);
        header.Controls.Add(filters, 1, 0);
        root.Controls.Add(header, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Theme.Background, Padding = new Padding(0, 8, 0, 8) };
        for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        cards.Controls.Add(Card("SELECTED ERROR", aimContextCauseCard), 0, 0);
        cards.Controls.Add(Card("SHARE OF ALL JUMPS", aimContextRateCard), 1, 0);
        cards.Controls.Add(Card("MAPS AFFECTED", aimContextMapsCard), 2, 0);
        root.Controls.Add(cards, 0, 1);

        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = Theme.Background, Margin = Padding.Empty, Padding = Padding.Empty
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var selectorShell = ToolkitUi.Wrap(aimContextCauseList, "Error type");
        selectorShell.Margin = new Padding(0, 0, 7, 0);
        workspace.Controls.Add(selectorShell, 0, 0);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Background,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.Controls.Add(ToolkitUi.Wrap(aimContextSummary, "What the common denominator looks like"), 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background,
            Padding = new Padding(0, 7, 0, 0), Margin = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));

        var evidence = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Background,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        evidence.RowStyles.Add(new RowStyle(SizeType.Absolute, 296));
        evidence.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var metricShell = ToolkitUi.Wrap(aimContextMetricGrid, "Demand associations · selected error vs your normal jump distribution");
        metricShell.Margin = new Padding(0, 0, 7, 7);
        evidence.Controls.Add(metricShell, 0, 0);
        var mapsShell = ToolkitUi.Wrap(aimContextMapGrid, "Maps where this error appears most often · count first, rate second");
        mapsShell.Margin = new Padding(0, 0, 7, 0);
        evidence.Controls.Add(mapsShell, 0, 1);
        body.Controls.Add(evidence, 0, 0);

        var diagnosis = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Background,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        diagnosis.RowStyles.Add(new RowStyle(SizeType.Absolute, 350));
        diagnosis.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var diagnosisTop = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        diagnosisTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
        diagnosisTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var categoryShell = ToolkitUi.Wrap(aimContextCategoryList, "Aim type · prioritized by your volume");
        categoryShell.Margin = new Padding(0, 0, 7, 7);
        diagnosisTop.Controls.Add(categoryShell, 0, 0);
        var diagnosisTextShell = ToolkitUi.Wrap(aimContextDiagnosis, "Objective diagnosis · evidence → training decision");
        diagnosisTextShell.Margin = new Padding(0, 0, 0, 7);
        diagnosisTop.Controls.Add(diagnosisTextShell, 1, 0);
        diagnosis.Controls.Add(diagnosisTop, 0, 0);
        diagnosis.Controls.Add(ToolkitUi.Wrap(aimContextDiagnosisGrid, "Probable contributors · evidence confidence, not claimed causality"), 0, 1);
        body.Controls.Add(diagnosis, 1, 0);

        right.Controls.Add(body, 0, 1);
        workspace.Controls.Add(right, 1, 0);
        root.Controls.Add(workspace, 0, 2);
        return root;
    }

    private static void ConfigureAimContextList(ListBox list)
    {
        list.BackColor = Theme.Panel;
        list.ForeColor = Theme.Text;
        list.BorderStyle = BorderStyle.None;
        list.Font = new Font("Segoe UI Semibold", 9.1f);
        list.IntegralHeight = false;
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.ItemHeight = 31;
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var itemBrush = new SolidBrush(selected ? Theme.Popup : Theme.Panel);
            e.Graphics.FillRectangle(itemBrush, e.Bounds);
            string text = list.Items[e.Index]?.ToString() ?? "";
            TextRenderer.DrawText(e.Graphics, text, list.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 12), e.Bounds.Height),
                selected ? Theme.Text : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    private void ConfigureAimContextGrids()
    {
        ToolkitUi.StyleDataGrid(aimContextMetricGrid);
        aimContextMetricGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        aimContextMetricGrid.AllowUserToResizeRows = false;
        aimContextMetricGrid.Columns.Clear();
        (string Key, string Title, int Width)[] metricCols =
        {
            ("metric", "Map demand", 132), ("hot", "Most associated range", 150), ("rate", "Error rate there", 105),
            ("base", "Overall rate", 92), ("lift", "Relative tendency", 112), ("typical", "Typical on cause", 118),
            ("baseline", "Your baseline", 108), ("samples", "Samples", 92)
        };
        foreach (var c in metricCols)
        {
            int i = aimContextMetricGrid.Columns.Add(c.Key, c.Title);
            aimContextMetricGrid.Columns[i].Width = c.Width;
        }
        aimContextMetricGrid.ColumnHeadersHeight = 54;
        aimContextMetricGrid.RowTemplate.Height = 34;

        ToolkitUi.StyleDataGrid(aimContextMapGrid);
        aimContextMapGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        aimContextMapGrid.AllowUserToResizeRows = false;
        aimContextMapGrid.Columns.Clear();
        (string Key, string Title, int Width)[] mapCols =
        {
            ("map", "Map / difficulty", 360), ("mods", "Mods", 58), ("plays", "Runs", 56),
            ("count", "Matching jumps", 92), ("rate", "Match rate", 82), ("star", "★", 58),
            ("ar", "AR", 62), ("bpm", "BPM", 68), ("spacing", "Spacing", 88)
        };
        foreach (var c in mapCols)
        {
            int i = aimContextMapGrid.Columns.Add(c.Key, c.Title);
            aimContextMapGrid.Columns[i].Width = c.Width;
        }
        aimContextMapGrid.ColumnHeadersHeight = 54;
        aimContextMapGrid.RowTemplate.Height = 32;
    }

    private void ConfigureAimDiagnosisGrid()
    {
        ToolkitUi.StyleDataGrid(aimContextDiagnosisGrid);
        aimContextDiagnosisGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        aimContextDiagnosisGrid.AllowUserToResizeRows = false;
        aimContextDiagnosisGrid.Columns.Clear();
        var metric = aimContextDiagnosisGrid.Columns.Add("metric", "Possible contributor");
        var confidence = aimContextDiagnosisGrid.Columns.Add("confidence", "Evidence confidence");
        var observed = aimContextDiagnosisGrid.Columns.Add("observed", "Error jumps");
        var comfort = aimContextDiagnosisGrid.Columns.Add("comfort", "Controlled range");
        var evidence = aimContextDiagnosisGrid.Columns.Add("evidence", "Measured relationship");
        aimContextDiagnosisGrid.Columns[metric].FillWeight = 17;
        aimContextDiagnosisGrid.Columns[confidence].FillWeight = 15;
        aimContextDiagnosisGrid.Columns[observed].FillWeight = 14;
        aimContextDiagnosisGrid.Columns[comfort].FillWeight = 17;
        aimContextDiagnosisGrid.Columns[evidence].FillWeight = 37;
        aimContextDiagnosisGrid.ColumnHeadersHeight = 50;
        aimContextDiagnosisGrid.RowTemplate.Height = 44;
    }

    private Control BuildAimProfileTab()
    {
        ConfigureAimMechanicGrid();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Background,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Panel2,
            Padding = new Padding(16, 9, 12, 8),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Panel2, Margin = Padding.Empty };
        titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        titleStack.Controls.Add(new Label
        {
            Text = "⬡   aim profile",
            AutoSize = true,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI Semibold", 14f),
            Padding = new Padding(0, 1, 0, 0)
        }, 0, 0);
        titleStack.Controls.Add(new Label
        {
            Text = "A readable picture of how you aim. The raw timelines are still available under Advanced breakdown.",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 9f)
        }, 0, 1);
        header.Controls.Add(titleStack, 0, 0);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            BackColor = Theme.Panel2,
            Padding = new Padding(8, 4, 0, 0),
            Margin = Padding.Empty
        };
        filters.Controls.Add(LabelFor("History"));
        aimAnalysisRange.Width = 116;
        filters.Controls.Add(aimAnalysisRange);
        var refresh = ToolkitUi.Button("Refresh");
        refresh.Click += (_, _) => RefreshAimAnalysisTab(true);
        filters.Controls.Add(refresh);
        header.Controls.Add(filters, 1, 0);
        root.Controls.Add(header, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, BackColor = Theme.Background, Padding = new Padding(0, 8, 0, 8) };
        for (int i = 0; i < 5; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
        cards.Controls.Add(Card("PLAYS", aimPlayCountCard), 0, 0);
        cards.Controls.Add(Card("AVG PROFICIENCY", aimProfCard), 1, 0);
        cards.Controls.Add(Card("RAW AIM SKILL · TOP MAP AVG", aimPerformanceCard), 2, 0);
        cards.Controls.Add(Card("AVG CENTER ERROR", aimErrorCard), 3, 0);
        cards.Controls.Add(Card("DOMINANT CAUSE", aimDominantErrorCard), 4, 0);
        root.Controls.Add(cards, 0, 1);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background, Margin = Padding.Empty };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Theme.Background,
            Padding = new Padding(0, 4, 0, 12),
            Margin = Padding.Empty
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 640));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 410));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var snapshot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Theme.Background,
            Margin = Padding.Empty
        };
        snapshot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        snapshot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        snapshot.RowStyles.Add(new RowStyle(SizeType.Absolute, 395));
        snapshot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        snapshot.Controls.Add(ToolkitUi.Wrap(aimLifetimeRadar, "Aim fingerprint · execution shape + raw aim skill"), 0, 0);
        snapshot.Controls.Add(ToolkitUi.Wrap(aimLifetimeErrorProfile, "Where the cursor ends · direction / centering"), 1, 0);
        var lifetimeCauseShell = ToolkitUi.Wrap(aimLifetimeCauseProfile, "Why control breaks · inferred movement causes + repeated-pattern detection");
        lifetimeCauseShell.Margin = new Padding(0, 8, 0, 0);
        snapshot.Controls.Add(lifetimeCauseShell, 0, 1);
        snapshot.SetColumnSpan(lifetimeCauseShell, 2);
        stack.Controls.Add(snapshot, 0, 0);

        stack.Controls.Add(ToolkitUi.Wrap(BuildAimMetricCards(), "Mechanics at a glance · the drawing explains what each number means · pink marker = recent form"), 0, 1);
        stack.Controls.Add(ToolkitUi.Wrap(aimAnalysisSummary, "What this profile says in plain English"), 0, 2);

        var advancedButton = ToolkitUi.Button("Advanced breakdown  ▾");
        advancedButton.Margin = new Padding(0, 8, 0, 8);
        var advancedButtonHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 0),
            Margin = Padding.Empty
        };
        advancedButtonHost.Controls.Add(advancedButton);
        advancedButtonHost.Controls.Add(new Label
        {
            Text = "Raw historical graphs and the exact mechanic table",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Padding = new Padding(8, 9, 0, 0)
        });
        stack.Controls.Add(advancedButtonHost, 0, 3);

        var advanced = BuildAimAdvancedBreakdown();
        advanced.Visible = false;
        stack.Controls.Add(advanced, 0, 4);
        advancedButton.Click += (_, _) =>
        {
            advanced.Visible = !advanced.Visible;
            aimAdvancedVisible = advanced.Visible;
            advancedButton.Text = advanced.Visible ? "Advanced breakdown  ▴" : "Advanced breakdown  ▾";
            if (advanced.Visible && currentAimAnalysisData != null)
                RefreshAimAdvancedBreakdown(currentAimAnalysisData, aimAnalysisGrouping.SelectedItem?.ToString() ?? "Play by play");
        };

        scroll.Controls.Add(stack);
        root.Controls.Add(scroll, 0, 2);
        return root;
    }

    private Control BuildAimMetricCards()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Theme.Panel,
            Padding = new Padding(0),
            Margin = Padding.Empty
        };
        for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        Control[] cards =
        {
            aimCenteringMetric, aimArrivalMetric, aimStraightnessMetric, aimStabilityMetric,
            aimBrakingMetric, aimPathMetric, aimRawSkillMetric
        };
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i].Margin = new Padding(i % 4 == 0 ? 0 : 4, i < 4 ? 0 : 4, i % 4 == 3 ? 0 : 4, 4);
            grid.Controls.Add(cards[i], i % 4, i / 4);
        }

        var readMe = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(4, 4, 0, 4), Padding = new Padding(15) };
        readMe.Resize += (_, _) => ToolkitUi.RoundControl(readMe, 10);
        readMe.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "HOW TO READ THIS\r\n\r\nBlue bar = the selected history window.\r\nPink tick = your most recent 20 plays.\r\n\r\nMechanic scores describe execution quality. Raw aim skill is difficulty-adjusted and can exceed 1000.",
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 8.6f),
            TextAlign = ContentAlignment.MiddleLeft
        });
        grid.Controls.Add(readMe, 3, 1);
        return grid;
    }

    private Control BuildAimAdvancedBreakdown()
    {
        var advanced = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 1980,
            ColumnCount = 1,
            RowCount = 8,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Panel2,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(12, 9, 0, 0),
            Margin = new Padding(0, 0, 0, 8)
        };
        tools.Controls.Add(LabelFor("Timeline grouping"));
        aimAnalysisGrouping.Width = 128;
        tools.Controls.Add(aimAnalysisGrouping);
        tools.Controls.Add(new Label
        {
            Text = "These are the exact telemetry views retained for deeper analysis.",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Padding = new Padding(12, 7, 0, 0)
        });
        advanced.Controls.Add(tools, 0, 0);
        advanced.Controls.Add(ToolkitUi.Wrap(aimPerformanceHistory, "Aim performance over time · demonstrated map-level capability"), 0, 1);
        advanced.Controls.Add(ToolkitUi.Wrap(aimProficiencyHistory, "Aim proficiency over time · execution quality"), 0, 2);
        advanced.Controls.Add(ToolkitUi.Wrap(aimErrorHistory, "Centering error over time · lower is better · R = target-circle radius"), 0, 3);
        advanced.Controls.Add(ToolkitUi.Wrap(aimErrorMixHistory, "Where errors land over time · overaim / underaim / lateral / correction / plain error / clean"), 0, 4);
        advanced.Controls.Add(ToolkitUi.Wrap(aimCauseHistory, "Why control breaks over time · inferred movement-cause share"), 0, 5);
        advanced.Controls.Add(ToolkitUi.Wrap(aimMechanicsHistory, "Proficiency mechanics over time · centering / timing / straightness / shake / braking / path"), 0, 6);
        advanced.Controls.Add(ToolkitUi.Wrap(aimMechanicGrid, "Exact mechanic rundown · lifetime average vs recent form"), 0, 7);
        return advanced;
    }

    private void ConfigureAimMechanicGrid()
    {
        ToolkitUi.StyleDataGrid(aimMechanicGrid);
        aimMechanicGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        aimMechanicGrid.AllowUserToResizeRows = false;
        aimMechanicGrid.RowHeadersVisible = false;
        aimMechanicGrid.Columns.Clear();
        (string key, string title, int width)[] cols =
        {
            ("metric", "Metric", 180),
            ("weight", "Base weight", 125),
            ("lifetime", "Lifetime avg", 105),
            ("recent", "Recent 20", 105),
            ("delta", "Change", 90),
            ("meaning", "What it measures", 640)
        };
        foreach (var c in cols)
        {
            int i = aimMechanicGrid.Columns.Add(c.key, c.title);
            aimMechanicGrid.Columns[i].Width = c.width;
        }
    }


    private Control BuildInsightsTab()
    {
        ConfigureAimContextList(insightsList);
        insightsList.SelectedIndexChanged += (_, _) => RefreshInsightSelection();
        insightsRange.SelectedIndexChanged += (_, _) => { insightsRevision = -1; if (mainTabs?.SelectedTab?.Text == "Insights") RefreshInsightsTab(true); };
        insightsCategory.SelectedIndexChanged += (_, _) => RefreshInsightsList();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Background, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Panel2, Padding = new Padding(16, 10, 12, 8) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Text = "insights", Dock = DockStyle.Fill, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 15f), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, BackColor = Theme.Panel2, Padding = new Padding(4) };
        filters.Controls.Add(LabelFor("History")); insightsRange.Width = 110; filters.Controls.Add(insightsRange);
        filters.Controls.Add(LabelFor("Category")); insightsCategory.Width = 150; filters.Controls.Add(insightsCategory);
        var refresh = ToolkitUi.Button("Refresh"); refresh.Click += (_, _) => RefreshInsightsTab(true); filters.Controls.Add(refresh);
        header.Controls.Add(filters, 1, 0); root.Controls.Add(header, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Background, Padding = new Padding(0, 8, 0, 8) };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.Controls.Add(Card("MEANINGFUL FINDINGS", insightsCountCard),0,0); cards.Controls.Add(Card("HIGHEST-PRIORITY SIGNAL", insightsTopCard),1,0); root.Controls.Add(cards,0,1);

        // Keep construction-time minimums deliberately small. WinForms validates panel
        // minimums against the current (often tiny) pre-layout width, so large minimums
        // here can throw before the deferred splitter initialization even runs. The
        // actual 31/69 layout still gives the list and evidence pane the intended space.
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 7,
            BackColor = Theme.Background, Panel1MinSize = 40, Panel2MinSize = 40
        };
        SetInitialSplit(split, 0.31);
        split.Panel1.Controls.Add(ToolkitUi.Wrap(insightsList, "Prioritized findings · biggest useful discrepancies first"));
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Theme.Background };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 44)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        right.Controls.Add(ToolkitUi.Wrap(insightsDetail, "What this means · evidence + caveats"),0,0);
        right.Controls.Add(ToolkitUi.Wrap(insightsGraph, "Visual evidence · exact values behind the selected insight"),0,1);
        split.Panel2.Controls.Add(right); root.Controls.Add(split,0,2);
        return root;
    }

    private void RefreshInsightsTab(bool force = false)
    {
        if (!IsHandleCreated) return;
        string range = insightsRange.SelectedItem?.ToString() ?? "All time";
        string signature = range;
        if (!force && insightsRevision == dataRevision && insightsSignature == signature) return;
        insightsRevision = dataRevision; insightsSignature = signature;
        var source = allPlays.Count > 0 ? allPlays : database.LoadPlays();
        List<PlayRow> plays = range switch
        {
            "7 days" => source.Where(p=>p.TimestampUtc>=DateTime.UtcNow.AddDays(-7)).OrderBy(p=>p.TimestampUtc).ToList(),
            "30 days" => source.Where(p=>p.TimestampUtc>=DateTime.UtcNow.AddDays(-30)).OrderBy(p=>p.TimestampUtc).ToList(),
            "90 days" => source.Where(p=>p.TimestampUtc>=DateTime.UtcNow.AddDays(-90)).OrderBy(p=>p.TimestampUtc).ToList(),
            _ => source.OrderBy(p=>p.TimestampUtc).ToList()
        };
        var data = LifetimeAimAnalysisBuilder.Build(database, plays);
        currentInsights = PlayerInsightsEngine.Build(data);
        insightsCountCard.Text = currentInsights.Count.ToString("N0");
        insightsTopCard.Text = currentInsights.Count == 0 ? "—" : currentInsights[0].Category + "\n" + currentInsights[0].Confidence.ToString("0") + "% evidence";
        RefreshInsightsList();
    }

    private void RefreshInsightsList()
    {
        string filter = insightsCategory.SelectedItem?.ToString() ?? "All";
        var rows = filter == "All" ? currentInsights : currentInsights.Where(x=>x.Category.Equals(filter,StringComparison.OrdinalIgnoreCase)).ToList();
        insightsList.BeginUpdate(); insightsList.Items.Clear();
        foreach (var i in rows) insightsList.Items.Add($"{i.Category.ToUpperInvariant()}  ·  {i.Title}\n{i.Summary}");
        insightsList.EndUpdate();
        insightsList.Tag = rows;
        if (rows.Count > 0) insightsList.SelectedIndex = 0; else { insightsDetail.Text = "No sufficiently strong findings in this filter yet."; insightsGraph.SetData(Array.Empty<GraphPoint>(),"Time","Value",true); }
    }

    private void RefreshInsightSelection()
    {
        if (insightsList.Tag is not List<PlayerInsight> rows || insightsList.SelectedIndex < 0 || insightsList.SelectedIndex >= rows.Count) return;
        var i = rows[insightsList.SelectedIndex];
        insightsDetail.Clear();
        insightsDetail.SelectionFont = new Font("Segoe UI Semibold", 13f); insightsDetail.SelectionColor = Theme.Text; insightsDetail.AppendText(i.Title + "\n");
        insightsDetail.SelectionFont = new Font("Segoe UI Semibold", 9.5f); insightsDetail.SelectionColor = i.Confidence >= 65 ? Theme.Good : Theme.Warn; insightsDetail.AppendText($"{i.Category} · {i.Confidence:0}% evidence confidence\n\n");
        insightsDetail.SelectionFont = new Font("Segoe UI", 10f); insightsDetail.SelectionColor = Theme.Text; insightsDetail.AppendText(i.Detail + "\n\n");
        insightsDetail.SelectionColor = Theme.Muted; insightsDetail.AppendText("The analyzer reports associations and training-response sequences from your own history. A discrepancy can be useful without proving a single causal mechanism.");
        if (i.Graph.Count > 0) insightsGraph.SetData(i.Graph,"Time",i.GraphLabel,true); else insightsGraph.SetData(Array.Empty<GraphPoint>(),"Time",i.GraphLabel,true);
    }

    private Control BuildCollectionsTab()
    {
        ConfigureCollectionRulesGrid();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Theme.Background, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Panel, Padding = new Padding(14, 9, 14, 8) };
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        top.Controls.Add(autoCollectionCheck, 0, 0);
        top.Controls.Add(collectionExclusiveCheck, 0, 1);
        top.Controls.Add(collectionDbInfo, 0, 2);
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(ToolkitUi.Wrap(collectionRulesGrid, "Live proficiency → osu! collection rules"), 0, 1);

        var backfill = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 10, 8, 7), WrapContents = false, AutoScroll = true };
        backfill.Controls.Add(new Label { Text = "Sort previous plays", AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 9f), Padding = new Padding(0, 7, 4, 0) });
        collectionBackfillRange.Width = 120;
        backfill.Controls.Add(collectionBackfillRange);
        var sortPast = ToolkitUi.Button("Categorize history"); sortPast.Click += (_, _) => BackfillCollectionsFromSelectedRange();
        backfill.Controls.Add(sortPast);
        backfill.Controls.Add(collectionBackfillPreview);
        backfill.Controls.Add(new Label { Text = "Uses the latest run on each difficulty in the selected window · additive, so older managed maps are left alone.", AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(12, 7, 0, 0) });
        root.Controls.Add(backfill, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(10, 8, 8, 6), WrapContents = false, AutoScroll = true };
        var add = ToolkitUi.Button("Add range"); add.Click += (_, _) => AddCollectionRuleRow();
        var remove = ToolkitUi.Button("Delete selected"); remove.Click += (_, _) => DeleteSelectedCollectionRule();
        var defaults = ToolkitUi.Button("Default ranges"); defaults.Click += (_, _) => LoadDefaultCollectionRules();
        var save = ToolkitUi.Button("Save + activate"); save.Click += (_, _) => SaveCollectionRulesAndActivate();
        var rebuild = ToolkitUi.Button("Rebuild ALL history"); rebuild.Click += (_, _) => RebuildCollectionsFromHistory();
        var backup = ToolkitUi.Button("Backup collection.db"); backup.Click += (_, _) => BackupCollectionsNow();
        var restore = ToolkitUi.Button("Restore latest backup"); restore.Click += (_, _) => RestoreLatestCollectionBackup();
        var open = ToolkitUi.Button("Open backups"); open.Click += (_, _) => OpenCollectionBackups();
        buttons.Controls.Add(add); buttons.Controls.Add(remove); buttons.Controls.Add(defaults); buttons.Controls.Add(save); buttons.Controls.Add(rebuild); buttons.Controls.Add(backup); buttons.Controls.Add(restore); buttons.Controls.Add(open);
        root.Controls.Add(buttons, 0, 3);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Background };
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var statusWrap = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 3, 12, 3) };
        statusWrap.Controls.Add(collectionStatus);
        bottom.Controls.Add(statusWrap, 0, 0);
        bottom.Controls.Add(ToolkitUi.Wrap(collectionActivity, "Collection activity / safety log"), 0, 1);
        root.Controls.Add(bottom, 0, 4);
        return root;
    }

    private void ConfigureCollectionRulesGrid()
    {
        ToolkitUi.StyleDataGrid(collectionRulesGrid);
        collectionRulesGrid.ReadOnly = false;
        collectionRulesGrid.AllowUserToAddRows = false;
        collectionRulesGrid.AllowUserToDeleteRows = false;
        collectionRulesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        collectionRulesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        collectionRulesGrid.Columns.Clear();
        collectionRulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "enabled", HeaderText = "On", Width = 54 });
        collectionRulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Collection name", Width = 300 });
        collectionRulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "min", HeaderText = "Min proficiency", Width = 125 });
        collectionRulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "max", HeaderText = "Max proficiency", Width = 125 });
        collectionRulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "count", HeaderText = "Maps currently managed", Width = 150, ReadOnly = true });
        collectionRulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "example", HeaderText = "Meaning", Width = 350, ReadOnly = true });
    }

    private void LoadCollectionRulesIntoGrid()
    {
        collectionRulesGrid.Rows.Clear();
        foreach (var r in settings.CollectionRules)
        {
            int row = collectionRulesGrid.Rows.Add(r.Enabled, r.CollectionName, r.MinProficiency.ToString("0", CultureInfo.InvariantCulture), r.MaxProficiency.ToString("0", CultureInfo.InvariantCulture), collectionService.CountFor(r.CollectionName), CollectionRangeMeaning(r.MinProficiency, r.MaxProficiency));
            collectionRulesGrid.Rows[row].Tag = r;
        }
    }

    private void AddCollectionRuleRow()
    {
        collectionRulesGrid.Rows.Add(true, "Aim · custom", "700", "799", "0", CollectionRangeMeaning(700, 799));
    }

    private void DeleteSelectedCollectionRule()
    {
        foreach (DataGridViewRow row in collectionRulesGrid.SelectedRows.Cast<DataGridViewRow>().ToList())
            if (!row.IsNewRow) collectionRulesGrid.Rows.Remove(row);
    }

    private void LoadDefaultCollectionRules()
    {
        settings.CollectionRules = DefaultCollectionRules();
        LoadCollectionRulesIntoGrid();
        AppendCollectionActivity("Loaded default production proficiency bands. Click Save + activate to commit them.");
    }

    private static List<CollectionRule> DefaultCollectionRules() => AppSettings.ProductionDefaultCollectionRules();

    private bool ReadCollectionRulesFromGrid(bool showErrors)
    {
        var rules = new List<CollectionRule>();
        foreach (DataGridViewRow row in collectionRulesGrid.Rows)
        {
            string name = Convert.ToString(row.Cells["name"].Value)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!double.TryParse(Convert.ToString(row.Cells["min"].Value), NumberStyles.Float, CultureInfo.InvariantCulture, out double min) ||
                !double.TryParse(Convert.ToString(row.Cells["max"].Value), NumberStyles.Float, CultureInfo.InvariantCulture, out double max) || max < min)
            {
                if (showErrors) MessageBox.Show(this, $"Invalid proficiency range for '{name}'.", "Collection rule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            rules.Add(new CollectionRule { Enabled = Convert.ToBoolean(row.Cells["enabled"].Value ?? true), CollectionName = name, MinProficiency = min, MaxProficiency = max });
        }
        settings.CollectionRules = rules;
        settings.AutoCollectionEnabled = autoCollectionCheck.Checked;
        settings.CollectionRulesExclusive = collectionExclusiveCheck.Checked;
        return true;
    }

    private void SaveCollectionRulesAndActivate()
    {
        try
        {
            if (!ReadCollectionRulesFromGrid(true)) return;
            settings.Save(paths.SettingsPath);
            collectionService.RefreshConfiguration();
            collectionService.EnsureCollectionsExist();
            RefreshCollectionTab();
            AppendCollectionActivity("Rules saved. New analyzed plays will route automatically while auto-routing is enabled.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save collection rules", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private List<PlayRow> GetCollectionBackfillPlays(string range)
    {
        var plays = database.LoadPlays();
        DateTime now = DateTime.UtcNow;
        return range switch
        {
            "Today" => plays.Where(p => p.TimestampUtc.ToLocalTime().Date == DateTime.Today).ToList(),
            "7 days" => plays.Where(p => p.TimestampUtc >= now.AddDays(-7)).ToList(),
            "30 days" => plays.Where(p => p.TimestampUtc >= now.AddDays(-30)).ToList(),
            "90 days" => plays.Where(p => p.TimestampUtc >= now.AddDays(-90)).ToList(),
            _ => plays
        };
    }

    private void UpdateCollectionBackfillPreview()
    {
        try
        {
            string range = collectionBackfillRange.SelectedItem?.ToString() ?? "30 days";
            var plays = GetCollectionBackfillPlays(range);
            int uniqueMaps = plays.Where(p => !string.IsNullOrWhiteSpace(p.BeatmapHash))
                .Select(p => p.BeatmapHash).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            collectionBackfillPreview.Text = $"{plays.Count:N0} analyzed plays · {uniqueMaps:N0} unique map difficulties";
        }
        catch { collectionBackfillPreview.Text = "Preview unavailable"; }
    }

    private void BackfillCollectionsFromSelectedRange()
    {
        try
        {
            if (!ReadCollectionRulesFromGrid(true)) return;
            string range = collectionBackfillRange.SelectedItem?.ToString() ?? "30 days";
            settings.CollectionBackfillRange = range;
            settings.Save(paths.SettingsPath);
            var plays = GetCollectionBackfillPlays(range);
            if (plays.Count == 0)
            {
                MessageBox.Show(this, $"No analyzed plays were found in {range}.", "Categorize history", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = collectionService.BackfillFromPlays(plays);
            AppendCollectionActivity($"History sort ({range}): {result.CategorizedMaps:N0}/{result.UniqueMaps:N0} unique maps categorized from {result.PlaysConsidered:N0} plays; {result.UnmatchedMaps:N0} outside enabled proficiency ranges.");
            RefreshCollectionTab();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "History categorization failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RebuildCollectionsFromHistory()
    {
        try
        {
            if (!ReadCollectionRulesFromGrid(true)) return;
            settings.Save(paths.SettingsPath);
            collectionService.RebuildFromLatestPlays(database.LoadPlays());
            RefreshCollectionTab();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Collection rebuild failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BackupCollectionsNow()
    {
        try
        {
            string file = collectionService.BackupNow();
            AppendCollectionActivity("Manual backup: " + file);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Backup failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RestoreLatestCollectionBackup()
    {
        if (collectionService.IsOsuRunning())
        {
            var result = MessageBox.Show(this, "osu! is currently running. Restoring collection.db while stable has collections loaded can be overwritten by the game. Close osu! first for the safest restore.\n\nRestore anyway?", "Restore collection backup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
        }
        try
        {
            string? file = collectionService.RestoreLatestBackup();
            if (file == null) MessageBox.Show(this, "No collection backups were found.", "Restore backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else AppendCollectionActivity("Restored: " + file);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OpenCollectionBackups()
    {
        Directory.CreateDirectory(collectionService.BackupDirectory);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = collectionService.BackupDirectory, UseShellExecute = true }); } catch { }
    }

    private void RefreshCollectionTab()
    {
        if (!IsHandleCreated) return;
        autoCollectionCheck.Checked = settings.AutoCollectionEnabled;
        collectionExclusiveCheck.Checked = settings.CollectionRulesExclusive;
        if (!collectionRulesGrid.IsCurrentCellInEditMode) LoadCollectionRulesIntoGrid();
        string db = collectionService.CollectionDbPath;
        bool running = collectionService.IsOsuRunning();
        collectionDbInfo.Text = $"collection.db: {(string.IsNullOrWhiteSpace(db) ? "not configured" : db)}   ·   osu! running: {(running ? "yes" : "no")}   ·   backups: {collectionService.BackupDirectory}";
        collectionStatus.Text = settings.AutoCollectionEnabled
            ? (running
                ? "LIVE DISK ROUTING ACTIVE · additions are written and protected during the session. osu!stable may not display external collection changes until its next start."
                : "LIVE ROUTING ACTIVE · collection.db will update immediately after each analyzed play.")
            : "Auto-routing is off. Configure ranges, then Save + activate.";
        collectionStatus.ForeColor = settings.AutoCollectionEnabled ? Theme.Good : Theme.Warn;
        UpdateCollectionBackfillPreview();
    }

    private static string CollectionRangeMeaning(double min, double max)
    {
        double mid = (min + Math.Min(max, 1000)) / 2.0;
        return $"{Humanize.ProductionProficiencyTier(mid)} · {min:0}–{max:0}";
    }

    private void AppendCollectionActivity(string text)
    {
        if (collectionActivity.IsDisposed) return;
        void Add()
        {
            collectionActivity.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
            collectionActivity.SelectionStart = collectionActivity.TextLength;
            collectionActivity.ScrollToCaret();
        }
        if (InvokeRequired) BeginInvoke((Action)Add); else Add();
    }

    private Control BuildSettings()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(28) };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, BackColor = Theme.Panel, Padding = new Padding(20) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        AddPathRow(table, 0, "osu!stable folder", osuPath, () => BrowseFolder(osuPath));
        AddPathRow(table, 1, "Songs folder", songsPath, () => BrowseFolder(songsPath));
        AddPathRow(table, 2, "Replay folder (Data/r)", replayPath, () => BrowseFolder(replayPath));

        table.Controls.Add(new Label { Text = "Session gap", ForeColor = Theme.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        var gapFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Theme.Panel };
        gapFlow.Controls.Add(sessionGap); gapFlow.Controls.Add(new Label { Text = "minutes", ForeColor = Theme.Muted, AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
        table.Controls.Add(gapFlow, 1, 3);
        table.SetColumnSpan(gapFlow, 2);

        table.Controls.Add(new Label { Text = "Replay history", ForeColor = Theme.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
        var historyFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Theme.Panel };
        replayBackfill.Width = 130;
        historyFlow.Controls.Add(replayBackfill);
        historyFlow.Controls.Add(new Label { Text = "Backfill older replays already stored in Data/r. Imported plays are skipped automatically.", ForeColor = Theme.Muted, AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
        table.Controls.Add(historyFlow, 1, 4);
        table.SetColumnSpan(historyFlow, 2);

        var checkFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, BackColor = Theme.Panel, Padding = new Padding(0, 8, 0, 8) };
        checkFlow.Controls.Add(watchCheck); checkFlow.Controls.Add(pureJumpCheck);
        table.Controls.Add(checkFlow, 1, 5); table.SetColumnSpan(checkFlow, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Theme.Panel, Padding = new Padding(0, 14, 0, 0) };
        var save = Button("Save + reindex"); save.Click += async (_, _) => await SaveAndReindexAsync();
        var history = Button("Analyze replay history"); history.Click += async (_, _) => await ScanHistoryAsync();
        var import = Button("Import .osr files…"); import.Click += async (_, _) => await ImportFilesAsync();
        var refresh = Button("Refresh dashboard"); refresh.Click += (_, _) => RefreshDashboard();
        buttons.Controls.Add(save); buttons.Controls.Add(history); buttons.Controls.Add(import); buttons.Controls.Add(refresh);
        table.Controls.Add(buttons, 1, 6); table.SetColumnSpan(buttons, 2);

        var help = new Label
        {
            Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(1000, 0), Padding = new Padding(4, 24, 4, 4), ForeColor = Theme.Muted,
            Text = "The app reads local osu!stable replays only. Automatic watching picks up new completed/passed plays saved in Data/r. For deliberate limit testing, NoFail is useful because stable otherwise may not keep a full internal replay for failed attempts. The proficiency model is trajectory-based; normal score accuracy is stored but does not drive the aim score."
        };

        root.Controls.Add(help); root.Controls.Add(table);
        return root;
    }

    private StatusStrip BuildStatusBar()
    {
        var s = new StatusStrip { BackColor = Theme.Panel2, ForeColor = Theme.Muted, SizingGrip = false };
        statusLabel.Spring = true; statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        s.Items.Add(statusLabel); s.Items.Add(indexLabel);
        return s;
    }

    private async Task InitializeAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            SetStatus("Indexing local beatmaps…");
            var progress = new Progress<string>(SetStatus);
            await resolver.RebuildAsync(progress);
            indexLabel.Text = $"Beatmaps: {resolver.CachedBeatmapCount:N0}";
            collectionService.StatusChanged -= OnCollectionStatus;
            collectionService.StatusChanged += OnCollectionStatus;
            collectionService.Start();
            if (settings.AutoCollectionEnabled)
            {
                try { collectionService.EnsureCollectionsExist(); }
                catch (Exception ex) { AppendCollectionActivity("Initial collection sync failed: " + ex.Message); }
            }
            processor = new ReplayProcessor(settings, resolver, database);
            processor.StatusChanged += s => BeginInvoke((Action)(() => SetStatus(s)));
            processor.StartWatcher();
            if (Directory.Exists(settings.ReplayDirectory))
            {
                int imported = await processor.ScanRecentAsync(TimeSpan.FromHours(6), progress);
                if (imported > 0) SetStatus($"Recovered {imported:N0} recent session plays.");
            }
            processor.PlayAnalyzed += id => BeginInvoke((Action)(() => HandlePlayAnalyzed(id)));
            RefreshDashboard();
            SetStatus(settings.WatchReplaysAutomatically ? "Ready · watching for new replays" : "Ready");
        }
        catch (Exception ex)
        {
            SetStatus("Initialization failed: " + ex.Message);
            MessageBox.Show(this, ex.ToString(), "Initialization error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { busy = false; }
    }

    private void HandlePlayAnalyzed(long playId)
    {
        // Update the post-play inspector first, then queue the heavier dashboard redraw.
        // This makes the newest-run feedback feel immediate even when the graph/table has
        // hundreds of points to rebuild.
        var play = database.LoadPlay(playId);
        if (play != null)
        {
            if (!allPlays.Any(p => p.Id == play.Id)) allPlays.Add(play);
            try { collectionService.RoutePlay(play); }
            catch (Exception ex) { AppendCollectionActivity("Auto-route failed: " + ex.Message); }
            if (followLatestPlay.Checked) ShowPlayInspector(play);
        }
        BeginInvoke(new Action(RefreshDashboard));
    }

    private async Task SaveAndReindexAsync()
    {
        if (busy) return;
        ApplyUiSettings(); settings.Save(paths.SettingsPath);
        processor?.Dispose(); processor = null;
        busy = true;
        try
        {
            var progress = new Progress<string>(SetStatus);
            await resolver.RebuildAsync(progress);
            indexLabel.Text = $"Beatmaps: {resolver.CachedBeatmapCount:N0}";
            processor = new ReplayProcessor(settings, resolver, database);
            processor.StatusChanged += s => BeginInvoke((Action)(() => SetStatus(s)));
            processor.PlayAnalyzed += id => BeginInvoke((Action)(() => HandlePlayAnalyzed(id)));
            processor.StartWatcher();
            collectionService.RefreshConfiguration();
            SetStatus("Settings saved and beatmap index rebuilt.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.ToString(), "Reindex failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { busy = false; }
    }

    private void OnCollectionStatus(string message)
    {
        AppendCollectionActivity(message);
        if (mainTabs?.SelectedTab?.Text == "Collections") BeginInvoke((Action)RefreshCollectionTab);
    }

    private async Task ScanHistoryAsync()
    {
        if (processor == null || busy) return;
        busy = true;
        try
        {
            string range = replayBackfill.SelectedItem?.ToString() ?? "30 days";
            settings.ReplayBackfillRange = range;
            settings.Save(paths.SettingsPath);
            TimeSpan? age = range switch
            {
                "12 hours" => TimeSpan.FromHours(12),
                "3 days" => TimeSpan.FromDays(3),
                "7 days" => TimeSpan.FromDays(7),
                "30 days" => TimeSpan.FromDays(30),
                "90 days" => TimeSpan.FromDays(90),
                _ => null
            };

            var p = new Progress<string>(SetStatus);
            int n = await processor.ScanHistoryAsync(age, p);
            SetStatus($"Replay history scan complete ({range}) · {n:N0} new plays analyzed.");
            RefreshDashboard();
        }
        catch (Exception ex) { SetStatus("Replay history scan failed: " + ex.Message); }
        finally { busy = false; }
    }

    private async Task ImportFilesAsync()
    {
        if (processor == null || busy) return;
        using var dlg = new OpenFileDialog { Filter = "osu! replay (*.osr)|*.osr", Multiselect = true, Title = "Import osu! replays" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        busy = true;
        try
        {
            var p = new Progress<string>(SetStatus);
            int n = await processor.ImportFilesAsync(dlg.FileNames, p);
            SetStatus($"Import complete · {n:N0} new plays analyzed.");
            RefreshDashboard();
        }
        catch (Exception ex) { SetStatus("Import failed: " + ex.Message); }
        finally { busy = false; }
    }

    private void RefreshDashboard()
    {
        allPlays = database.LoadPlays();
        long fingerprint = allPlays.Count == 0 ? 0 : HashCode.Combine(allPlays.Count, allPlays.Max(p => p.Id));
        if (fingerprint != dataFingerprint)
        {
            dataFingerprint = fingerprint;
            dataRevision++;
        }
        var filtered = ApplyFilters(allPlays).ToList();
        UpdateCards(filtered);
        UpdateGraph(filtered);
        UpdateGrid(filtered);
        string selectedTab = mainTabs?.SelectedTab?.Text ?? "";
        if (selectedTab == "Diagnostics") RefreshDiagnosticsTab();
        else if (selectedTab == "Training") RefreshTrainingTab();
        else if (selectedTab == "Aim analysis") RefreshAimAnalysisTab();
        else if (selectedTab == "Insights") RefreshInsightsTab();
        else if (selectedTab == "Collections") RefreshCollectionTab();
    }

    private IEnumerable<PlayRow> ApplyFilters(IEnumerable<PlayRow> source)
    {
        var list = source.OrderBy(p => p.TimestampUtc).ToList();
        string range = timeRange.SelectedItem?.ToString() ?? "Latest session";
        if (range == "Latest session" && list.Count > 0)
        {
            var selected = new List<PlayRow> { list[^1] };
            for (int i = list.Count - 2; i >= 0; i--)
            {
                if ((selected[^1].TimestampUtc - list[i].TimestampUtc).TotalMinutes > settings.SessionGapMinutes) break;
                selected.Add(list[i]);
            }
            list = selected.OrderBy(p => p.TimestampUtc).ToList();
        }
        else if (range == "Today") list = list.Where(p => p.TimestampUtc.ToLocalTime().Date == DateTime.Today).ToList();
        else if (range == "7 days") list = list.Where(p => p.TimestampUtc >= DateTime.UtcNow.AddDays(-7)).ToList();
        else if (range == "30 days") list = list.Where(p => p.TimestampUtc >= DateTime.UtcNow.AddDays(-30)).ToList();

        string selectedGrade = gradeFilter.SelectedItem?.ToString() ?? "Any grade";
        return list.Where(p => p.MeanBpm >= (double)bpmMin.Value && p.MeanBpm <= (double)bpmMax.Value
                            && p.SpacingP75 >= (double)spacingMin.Value && p.SpacingP75 <= (double)spacingMax.Value
                            && (p.StarRating <= 0 || (p.StarRating >= (double)starMin.Value && p.StarRating <= (double)starMax.Value))
                            && p.DensityP90 >= (double)densityMin.Value && p.DensityP90 <= (double)densityMax.Value
                            && p.MissCount >= (int)missMin.Value && p.MissCount <= (int)missMax.Value
                            && (selectedGrade == "Any grade" || p.Grade == selectedGrade));
    }

    private List<PlayRow> FilterByTimeRange(IEnumerable<PlayRow> source, string range)
    {
        var list = source.OrderBy(p => p.TimestampUtc).ToList();
        if (range == "Latest session" && list.Count > 0)
        {
            var selected = new List<PlayRow> { list[^1] };
            for (int i = list.Count - 2; i >= 0; i--)
            {
                if ((selected[^1].TimestampUtc - list[i].TimestampUtc).TotalMinutes > settings.SessionGapMinutes) break;
                selected.Add(list[i]);
            }
            list = selected.OrderBy(p => p.TimestampUtc).ToList();
        }
        else if (range == "Today") list = list.Where(p => p.TimestampUtc.ToLocalTime().Date == DateTime.Today).ToList();
        else if (range == "7 days") list = list.Where(p => p.TimestampUtc >= DateTime.UtcNow.AddDays(-7)).ToList();
        else if (range == "30 days") list = list.Where(p => p.TimestampUtc >= DateTime.UtcNow.AddDays(-30)).ToList();
        return list;
    }

    private void RefreshAimAnalysisTab(bool force = false)
    {
        if (!IsHandleCreated) return;
        string history = aimAnalysisRange.SelectedItem?.ToString() ?? "All time";
        string groupingMode = aimAnalysisGrouping.SelectedItem?.ToString() ?? "Play by play";
        string signature = history;
        if (!force && aimAnalysisRevision == dataRevision && aimAnalysisSignature == signature) return;
        aimAnalysisRevision = dataRevision;
        aimAnalysisSignature = signature;

        var plays = FilterByTimeRange(allPlays.Count > 0 ? allPlays : database.LoadPlays(), history)
            .OrderBy(p => p.TimestampUtc).ToList();
        var data = LifetimeAimAnalysisBuilder.Build(database, plays);
        currentAimAnalysisData = data;
        aimContextResultCache.Clear();
        aimContextDiagnosisCache.Clear();
        SyncAimContextRange(history);
        RefreshAimContextCauseList(data);

        var recentSamples = data.Samples.OrderByDescending(x => x.Play.TimestampUtc).Take(20).ToList();
        double Avg(Func<LifetimeAimSample, double> selector, IReadOnlyList<LifetimeAimSample> rows)
            => rows.Count == 0 ? 0 : rows.Average(selector);
        double WeightedError(IReadOnlyList<LifetimeAimSample> rows)
        {
            double n = rows.Sum(x => (double)x.ErrorSamples);
            return n <= 0 ? 0 : rows.Sum(x => x.MeanCenterError * x.ErrorSamples) / n;
        }

        double rawAimSkill = AimRatingUtils.Top100Average(plays);
        double recentRawAimSkill = AimRatingUtils.Top100Average(recentSamples.Select(x => x.Play));
        double centerError = WeightedError(data.Samples);
        double recentCenterError = WeightedError(recentSamples);

        aimPlayCountCard.Text = data.Samples.Count.ToString("N0");
        aimProfCard.Text = data.Samples.Count > 0 ? Humanize.ProductionScoreShort(data.AverageProficiency) : "—";
        aimPerformanceCard.Text = data.Samples.Count > 0 ? Humanize.CapabilityScore(rawAimSkill) : "—";
        aimErrorCard.Text = data.TransitionCount > 0 ? $"{centerError:0.00}R" : "—";
        aimDominantErrorCard.Text = data.DominantCause;

        aimLifetimeErrorProfile.SetData(data.Transitions);
        aimLifetimeCauseProfile.SetData(data.Transitions);

        double centering = Avg(x => x.Play.Landing, data.Samples);
        double arrival = Avg(x => x.Play.Arrival, data.Samples);
        double straightness = Avg(x => x.Play.Straightness, data.Samples);
        double stability = Avg(x => x.Play.Stability, data.Samples);
        double braking = Avg(x => x.Play.Deceleration, data.Samples);
        double idealPath = Avg(x => x.Play.IdealPathMatch, data.Samples);
        double recentCentering = Avg(x => x.Play.Landing, recentSamples);
        double recentArrival = Avg(x => x.Play.Arrival, recentSamples);
        double recentStraightness = Avg(x => x.Play.Straightness, recentSamples);
        double recentStability = Avg(x => x.Play.Stability, recentSamples);
        double recentBraking = Avg(x => x.Play.Deceleration, recentSamples);
        double recentIdealPath = Avg(x => x.Play.IdealPathMatch, recentSamples);

        aimCenteringMetric.SetMetric(
            "Centering",
            centering, recentCentering,
            data.TransitionCount > 0 ? $"{centerError:0.00}R avg" : "—",
            $"{centering:0}/1000 landing quality · recent error {recentCenterError:0.00}R",
            "How close the cursor actually lands to the center of the circle. 1.00R is one full hit-circle radius.",
            centerError);
        aimArrivalMetric.SetMetric(
            "Arrival timing",
            arrival, recentArrival,
            $"{arrival:0}/1000",
            "reaching the target at the right moment",
            "Higher means the cursor arrives cleanly in the intended hit window instead of needing a rushed or mistimed finish.");
        aimStraightnessMetric.SetMetric(
            "Straightness",
            straightness, recentStraightness,
            $"{straightness:0}/1000",
            "direct target-to-target movement",
            "Higher means less unnecessary bending, wandering and correction between objects.");
        aimStabilityMetric.SetMetric(
            "Stability / shake",
            stability, recentStability,
            $"{stability:0}/1000",
            "how settled the cursor is around the target",
            "Higher means less visible shake and fewer small corrective movements near the hit object.");
        aimBrakingMetric.SetMetric(
            "Braking",
            braking, recentBraking,
            $"{braking:0}/1000",
            "stopping the cursor without overshooting",
            "Higher means velocity is shed cleanly into the target instead of carrying through it and snapping back.");
        aimPathMetric.SetMetric(
            "Path efficiency",
            idealPath, recentIdealPath,
            $"{idealPath:0}/1000",
            "similarity to a smooth minimum-jerk path",
            "Higher means the whole movement is smooth and efficient rather than only ending in the right place.");
        aimRawSkillMetric.SetMetric(
            "Raw aim skill",
            rawAimSkill, recentRawAimSkill,
            rawAimSkill > 0 ? rawAimSkill.ToString("0") : "—",
            $"{AimRatingUtils.ContributingMapCount(plays)}/100 best unique maps contributing",
            "Difficulty-adjusted demonstrated aim capability. This uses the top unique-map Aim Performance profile and can exceed 1000.",
            capabilityScale: true);

        aimLifetimeRadar.Aspects = data.Samples.Count == 0 ? new List<AimAspectScore>() : new List<AimAspectScore>
        {
            new() { Name = "Centering", Score = centering, Interpretation = Humanize.LegacyProficiencyTier(centering), Note = $"Average center error {centerError:0.00}R" },
            new() { Name = "Timing", Score = arrival, Interpretation = Humanize.LegacyProficiencyTier(arrival), Note = "Arrival timing quality" },
            new() { Name = "Straightness", Score = straightness, Interpretation = Humanize.LegacyProficiencyTier(straightness), Note = "Directness of cursor travel" },
            new() { Name = "Stability", Score = stability, Interpretation = Humanize.LegacyProficiencyTier(stability), Note = "Shake / settling control" },
            new() { Name = "Braking", Score = braking, Interpretation = Humanize.LegacyProficiencyTier(braking), Note = "Deceleration into the target" },
            new() { Name = "Path", Score = idealPath, Interpretation = Humanize.LegacyProficiencyTier(idealPath), Note = "Minimum-jerk path match" },
            new() { Name = "Raw aim", Score = rawAimSkill, Interpretation = Humanize.CapabilityTier(rawAimSkill), Note = "Difficulty-adjusted top-map aim capability" }
        };

        if (aimAdvancedVisible && aimAnalysisTabs?.SelectedTab?.Text == "Profile")
            RefreshAimAdvancedBreakdown(data, groupingMode);
        aimAnalysisSummary.Text = BuildAimLifetimeSummary(data, history, groupingMode);
        aimAnalysisSummary.SelectionStart = 0;
        aimAnalysisSummary.ScrollToCaret();
    }

    private void RefreshAimAdvancedBreakdown(LifetimeAimAnalysisData data, string groupingMode)
    {
        bool daily = groupingMode == "Daily average";
        List<GraphPoint> Points(Func<LifetimeAimSample, double> selector, bool requireError = false)
        {
            var source = requireError ? data.Samples.Where(x => x.ErrorSamples > 0).ToList() : data.Samples;
            if (daily)
            {
                return source.GroupBy(x => x.Play.TimestampUtc.ToLocalTime().Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new GraphPoint(
                        g.Key.ToOADate(),
                        g.Average(selector),
                        $"{g.Key:yyyy-MM-dd} · {g.Count()} plays",
                        g.Key))
                    .ToList();
            }

            return source.OrderBy(x => x.Play.TimestampUtc)
                .Select(x => new GraphPoint(
                    x.Play.TimestampUtc.ToLocalTime().ToOADate(),
                    selector(x),
                    $"{x.Play.TimestampUtc.ToLocalTime():g} · {x.Play.Map} [{x.Play.ModsText}]",
                    x.Play.TimestampUtc.ToLocalTime()))
                .ToList();
        }

        aimPerformanceHistory.SetData(Points(x => x.Play.RawAimRating), "Time", "Aim performance", true);
        aimProficiencyHistory.SetData(Points(x => x.Play.Proficiency), "Time", "Proficiency", true);
        aimErrorHistory.SetSeries(new[]
        {
            new GraphSeries("Mean center error", Points(x => x.MeanCenterError, true), Theme.Accent, 1f, true),
            new GraphSeries("Median", Points(x => x.MedianCenterError, true), Theme.Accent2, .88f, false),
            new GraphSeries("P90", Points(x => x.P90CenterError, true), Theme.Warn, .88f, false)
        }, "Time", "Center error (R)", true);
        aimErrorMixHistory.SetSeries(new[]
        {
            new GraphSeries("Overaim", Points(x => x.OveraimPercent, true), Theme.Bad, .95f, false),
            new GraphSeries("Underaim", Points(x => x.UnderaimPercent, true), Theme.Accent2, .95f, false),
            new GraphSeries("Lateral", Points(x => x.LateralPercent, true), Theme.Warn, .92f, false),
            new GraphSeries("Correction", Points(x => x.CorrectionPercent, true), Theme.Accent, .82f, false),
            new GraphSeries("Plain error", Points(x => x.PlainErrorPercent, true), Theme.Muted, .78f, false),
            new GraphSeries("Clean", Points(x => x.CleanPercent, true), Theme.Good, .72f, false)
        }, "Time", "Error share (%)", true);

        var topCauses = AimErrorDiagnostics.Analyze(data.Transitions).CauseCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .Take(5)
            .ToList();
        aimCauseHistory.SetSeries(topCauses.Select((cause, i) =>
            new GraphSeries(cause, Points(x => x.CausePercents.GetValueOrDefault(cause)),
                AimCauseProfileControl.CauseColor(cause), i == 0 ? .98f : .82f, false)),
            "Time", "Cause share (% of jumps)", true);

        aimMechanicsHistory.SetSeries(new[]
        {
            new GraphSeries("Straightness", Points(x => x.Play.Straightness), Theme.Accent, .96f, false),
            new GraphSeries("Landing / centering", Points(x => x.Play.Landing), Theme.Accent2, .96f, false),
            new GraphSeries("Arrival timing", Points(x => x.Play.Arrival), Theme.Good, .90f, false),
            new GraphSeries("Stability / shake", Points(x => x.Play.Stability), Theme.Warn, .90f, false),
            new GraphSeries("Braking", Points(x => x.Play.Deceleration), Theme.Bad, .88f, false),
            new GraphSeries("Ideal path", Points(x => x.Play.IdealPathMatch), Color.FromArgb(184, 138, 255), .88f, false)
        }, "Time", "Mechanic score /1000", true);
        FillAimMechanicGrid(data);
    }

    private void SyncAimContextRange(string history)
    {
        if (syncingAimContextRange) return;
        syncingAimContextRange = true;
        try
        {
            if (!string.Equals(aimContextRange.SelectedItem?.ToString(), history, StringComparison.OrdinalIgnoreCase))
                aimContextRange.SelectedItem = history;
        }
        finally { syncingAimContextRange = false; }
    }

    private void RefreshAimContextCauseList(LifetimeAimAnalysisData data)
    {
        string? previous = aimContextCauseList.SelectedIndex >= 0 && aimContextCauseList.SelectedIndex < aimContextCauseKeys.Count
            ? aimContextCauseKeys[aimContextCauseList.SelectedIndex]
            : null;

        var summary = AimErrorDiagnostics.Analyze(data.Transitions);
        var rankedCauses = AimErrorDiagnostics.OrderedCauses
            .Select(c => (Label: c, Count: summary.CauseCounts.GetValueOrDefault(c)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label)
            .ToList();

        string[] directionOrder = { "Overaim", "Underaim", "Lateral", "Correction", "Plain error", "Clean" };
        var directionCounts = data.Transitions
            .GroupBy(t => string.IsNullOrWhiteSpace(t.ErrorClass) ? "Plain error" : t.ErrorClass, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        aimContextCauseList.BeginUpdate();
        try
        {
            aimContextCauseList.Items.Clear();
            aimContextCauseKeys.Clear();
            foreach (var row in rankedCauses)
            {
                double pct = data.TransitionCount <= 0 ? 0 : 100.0 * row.Count / data.TransitionCount;
                aimContextCauseKeys.Add("cause:" + row.Label);
                aimContextCauseList.Items.Add($"CAUSE · {row.Label} · {pct:0.0}%");
            }
            foreach (string direction in directionOrder)
            {
                int count = directionCounts.GetValueOrDefault(direction);
                if (count <= 0) continue;
                double pct = data.TransitionCount <= 0 ? 0 : 100.0 * count / data.TransitionCount;
                aimContextCauseKeys.Add("direction:" + direction);
                aimContextCauseList.Items.Add($"LANDING · {direction} · {pct:0.0}%");
            }
        }
        finally { aimContextCauseList.EndUpdate(); }

        if (aimContextCauseKeys.Count == 0)
        {
            aimContextCauseCard.Text = "Clean";
            aimContextRateCard.Text = "0%";
            aimContextMapsCard.Text = "0";
            aimContextMetricGrid.Rows.Clear();
            aimContextMapGrid.Rows.Clear();
            aimContextCategoryList.Items.Clear();
            aimContextCategoryKeys.Clear();
            aimContextDiagnosisGrid.Rows.Clear();
            aimContextDiagnosis.SetEmpty("No trajectory-derived error samples were found in this history window.");
            aimContextSummary.Text = "No trajectory-derived error samples were found in this history window.";
            return;
        }

        int selected = previous == null ? 0 : aimContextCauseKeys.FindIndex(x => x.Equals(previous, StringComparison.OrdinalIgnoreCase));
        aimContextCauseList.SelectedIndex = selected >= 0 ? selected : 0;
    }

    private void RefreshAimErrorContextSelection()
    {
        if (currentAimAnalysisData == null || aimContextCauseList.SelectedIndex < 0 || aimContextCauseList.SelectedIndex >= aimContextCauseKeys.Count)
            return;

        string selectionKey = aimContextCauseKeys[aimContextCauseList.SelectedIndex];
        if (!aimContextResultCache.TryGetValue(selectionKey, out var result))
        {
            result = AimErrorContextAnalyzer.Build(currentAimAnalysisData, selectionKey);
            aimContextResultCache[selectionKey] = result;
        }

        aimContextCauseCard.Text = result.Label;
        aimContextRateCard.Text = $"{result.OverallCauseRate:0.0}%";
        aimContextMapsCard.Text = result.AffectedMaps.ToString("N0");
        string explanation = result.Kind == "direction"
            ? DescribeLandingDirection(result.Label)
            : AimErrorDiagnostics.CauseDescription(result.Label);
        aimContextSummary.Text = $"{explanation}\r\n\r\n{result.Summary}";
        aimContextSummary.SelectionStart = 0;
        aimContextSummary.ScrollToCaret();

        aimContextMetricGrid.Rows.Clear();
        foreach (var m in result.Metrics)
        {
            string tendency = m.Lift <= 0 ? "—" : $"{m.Lift:0.00}×";
            int rowIndex = aimContextMetricGrid.Rows.Add(
                m.Metric, m.HotRange, $"{m.CauseRateInRange:0.0}%", $"{m.OverallCauseRate:0.0}%", tendency,
                m.CauseTypical, m.BaselineTypical, $"{m.CauseSamplesInRange}/{m.TotalSamplesInRange}");
            var cell = aimContextMetricGrid.Rows[rowIndex].Cells[4];
            if (m.Lift >= 1.35) cell.Style.ForeColor = Theme.Bad;
            else if (m.Lift >= 1.12) cell.Style.ForeColor = Theme.Warn;
            else if (m.Lift > 0 && m.Lift <= .78) cell.Style.ForeColor = Theme.Good;
            else cell.Style.ForeColor = Theme.Muted;
            cell.ToolTipText = "Relative tendency compares this cause's rate inside the highlighted range with its rate across all of your analyzed jumps. 1.50× means 50% more frequent than your own baseline.";
        }

        aimContextMapGrid.Rows.Clear();
        foreach (var map in result.Maps)
        {
            int rowIndex = aimContextMapGrid.Rows.Add(
                map.Map, map.Mods, map.PlayCount, map.CauseCount, $"{map.CauseRate:0.0}%",
                map.Star > 0 ? map.Star.ToString("0.00") : "—",
                map.Ar > 0 ? map.Ar.ToString("0.0") : "—",
                map.Bpm > 0 ? map.Bpm.ToString("0") : "—",
                map.Spacing > 0 ? map.Spacing.ToString("0") + "px" : "—");
            if (map.CauseRate >= result.OverallCauseRate * 1.5 && map.CauseCount >= 3)
                aimContextMapGrid.Rows[rowIndex].Cells[4].Style.ForeColor = Theme.Warn;
        }

        RefreshAimContextCategoryList(selectionKey);
    }

    private void RefreshAimContextCategoryList(string selectionKey)
    {
        if (currentAimAnalysisData == null) return;
        string? previous = aimContextCategoryList.SelectedIndex >= 0 && aimContextCategoryList.SelectedIndex < aimContextCategoryKeys.Count
            ? aimContextCategoryKeys[aimContextCategoryList.SelectedIndex] : null;
        var categories = AimTrainingDiagnosisEngine.RankCategories(currentAimAnalysisData, selectionKey);

        aimContextCategoryList.BeginUpdate();
        try
        {
            aimContextCategoryList.Items.Clear();
            aimContextCategoryKeys.Clear();
            foreach (var c in categories)
            {
                aimContextCategoryKeys.Add(c.Category);
                aimContextCategoryList.Items.Add($"{c.Category} · {c.PlayCount} runs · {c.ErrorRate:0}% error");
            }
        }
        finally { aimContextCategoryList.EndUpdate(); }

        if (aimContextCategoryKeys.Count == 0)
        {
            aimContextDiagnosis.SetEmpty("Not enough categorized aim history exists in this window yet.");
            aimContextDiagnosisGrid.Rows.Clear();
            return;
        }
        int index = previous == null ? 0 : aimContextCategoryKeys.FindIndex(x => x.Equals(previous, StringComparison.OrdinalIgnoreCase));
        aimContextCategoryList.SelectedIndex = index >= 0 ? index : 0;
    }

    private void RefreshAimCategoryDiagnosisSelection()
    {
        if (currentAimAnalysisData == null || aimContextCauseList.SelectedIndex < 0 || aimContextCauseList.SelectedIndex >= aimContextCauseKeys.Count ||
            aimContextCategoryList.SelectedIndex < 0 || aimContextCategoryList.SelectedIndex >= aimContextCategoryKeys.Count)
            return;

        string selectionKey = aimContextCauseKeys[aimContextCauseList.SelectedIndex];
        string category = aimContextCategoryKeys[aimContextCategoryList.SelectedIndex];
        string cacheKey = selectionKey + "|" + category;
        if (!aimContextDiagnosisCache.TryGetValue(cacheKey, out var diagnosis))
        {
            diagnosis = AimTrainingDiagnosisEngine.BuildCategory(currentAimAnalysisData, selectionKey, category);
            aimContextDiagnosisCache[cacheKey] = diagnosis;
        }

        aimContextDiagnosis.SetDiagnosis(diagnosis);

        aimContextDiagnosisGrid.Rows.Clear();
        foreach (var f in diagnosis.Factors)
        {
            int row = aimContextDiagnosisGrid.Rows.Add(
                f.Metric,
                $"{f.Confidence:0}%",
                f.Observed,
                f.ComfortBand,
                $"{f.Difference}; {f.Lift:0.00}× tendency · n={f.ErrorSamples}/{f.ComparisonSamples}");
            var confCell = aimContextDiagnosisGrid.Rows[row].Cells[1];
            confCell.Style.ForeColor = f.Confidence >= 65 ? Theme.Bad : f.Confidence >= 40 ? Theme.Warn : Theme.Muted;
            aimContextDiagnosisGrid.Rows[row].Cells[4].ToolTipText = f.Explanation + (string.IsNullOrWhiteSpace(f.Adjustment) ? "" : "\n" + f.Adjustment);
        }
    }

    private static string DescribeLandingDirection(string direction) => direction switch
    {
        "Overaim" => "The cursor finishes beyond target center along the incoming jump axis. This says where the landing ended, not why the movement failed.",
        "Underaim" => "The cursor finishes short of target center along the incoming jump axis. This can come from insufficient movement amplitude, early braking, late acquisition, or other causes.",
        "Lateral" => "The cursor finishes primarily sideways from the intended jump axis. This often exposes directional control or path-stability problems.",
        "Correction" => "The cursor crosses/corrects around the target axis near the hit. The cause layer can distinguish whether that correction is tied to shake, braking, path shape, or timing.",
        "Plain error" => "The cursor finishes meaningfully away from center without one dominant directional signature.",
        "Clean" => "The landing is classified as clean/centered. Looking at where clean landings cluster can show which map conditions are currently comfortable.",
        _ => "Landing-direction classification from the normalized cursor position relative to the incoming jump axis."
    };

    private void FillAimMechanicGrid(LifetimeAimAnalysisData data)
    {
        aimMechanicGrid.Rows.Clear();
        if (data.Samples.Count == 0) return;

        var all = data.Samples;
        var recent = all.OrderByDescending(x => x.Play.TimestampUtc).Take(20).ToList();
        var profile = ProductionScoring.Profile;
        double weightSum = profile.LandingWeight + profile.ArrivalWeight + profile.StraightnessWeight
                         + profile.DecelerationWeight + profile.StabilityWeight + profile.IdealPathWeight;
        string W(double w) => weightSum > 0 ? $"{100.0 * w / weightSum:0}%" : "—";
        double Avg(Func<LifetimeAimSample, double> f, IReadOnlyList<LifetimeAimSample> rows) => rows.Count == 0 ? 0 : rows.Average(f);
        double WeightedError(IReadOnlyList<LifetimeAimSample> rows)
        {
            double n = rows.Sum(x => (double)x.ErrorSamples);
            return n <= 0 ? 0 : rows.Sum(x => x.MeanCenterError * x.ErrorSamples) / n;
        }

        void AddScore(string metric, string weight, Func<LifetimeAimSample, double> f, string meaning, bool lowerIsBetter = false, string suffix = "")
        {
            double lifetime = Avg(f, all);
            double now = Avg(f, recent);
            double delta = now - lifetime;
            string fmt = suffix == "R" ? "0.00" : "0";
            int row = aimMechanicGrid.Rows.Add(metric, weight, lifetime.ToString(fmt) + suffix, now.ToString(fmt) + suffix,
                delta.ToString(suffix == "R" ? "+0.00;-0.00;0.00" : "+0;-0;0") + suffix, meaning);
            bool improved = lowerIsBetter ? delta < 0 : delta > 0;
            bool worse = lowerIsBetter ? delta > 0 : delta < 0;
            if (improved) aimMechanicGrid.Rows[row].Cells[4].Style.ForeColor = Theme.Good;
            else if (worse) aimMechanicGrid.Rows[row].Cells[4].Style.ForeColor = Theme.Bad;
        }

        double allErr = WeightedError(all);
        double recentErr = WeightedError(recent);
        int erow = aimMechanicGrid.Rows.Add("Center error", "feeds landing", $"{allErr:0.00}R", $"{recentErr:0.00}R",
            (recentErr - allErr).ToString("+0.00;-0.00;0.00") + "R",
            "Actual average cursor distance from the target center at landing. 1.00R equals one hit-circle radius; lower is better.");
        aimMechanicGrid.Rows[erow].Cells[4].Style.ForeColor = recentErr <= allErr ? Theme.Good : Theme.Bad;

        AddScore("Landing / centering", W(profile.LandingWeight), x => x.Play.Landing,
            "How accurately the movement terminates on the target, including the tuned underaim recovery model.");
        AddScore("Arrival timing", W(profile.ArrivalWeight), x => x.Play.Arrival,
            "Whether the cursor reaches the target at the right moment instead of arriving substantially early or late.");
        AddScore("Straightness", W(profile.StraightnessWeight), x => x.Play.Straightness,
            "How directly the cursor travels from object to object instead of wandering, curving or making unnecessary corrections.");
        AddScore("Braking / deceleration", W(profile.DecelerationWeight), x => x.Play.Deceleration,
            "How cleanly velocity is shed into the target. Weak braking often appears as overshoot or a late corrective snap.");
        AddScore("Stability / shake", W(profile.StabilityWeight), x => x.Play.Stability,
            "How still and controlled the cursor remains around the target after arrival. This is the closest proficiency component to visible shake/jitter.");
        AddScore("Ideal path match", W(profile.IdealPathWeight), x => x.Play.IdealPathMatch,
            "Similarity to the analyzer's minimum-jerk reference movement: a smooth, efficient human-like trajectory between objects.");
        AddScore("Aim tension", "derived only", x => x.Play.AimTension,
            "Replay-visible stress/instability inferred from stability, braking, straightness, landing errors and map demand. Lower is better; it is not directly weighted into proficiency.", true);
    }

    private string BuildAimLifetimeSummary(LifetimeAimAnalysisData data, string history, string groupingMode)
    {
        if (data.Samples.Count == 0) return "No analyzed plays exist in this history window yet.";

        var all = data.Samples.OrderBy(x => x.Play.TimestampUtc).ToList();
        var recent = all.TakeLast(Math.Min(20, all.Count)).ToList();
        double Avg(Func<LifetimeAimSample, double> f, IReadOnlyList<LifetimeAimSample> rows) => rows.Count == 0 ? 0 : rows.Average(f);
        double WeightedError(IReadOnlyList<LifetimeAimSample> rows)
        {
            double n = rows.Sum(x => (double)x.ErrorSamples);
            return n <= 0 ? 0 : rows.Sum(x => x.MeanCenterError * x.ErrorSamples) / n;
        }

        var mechanics = new (string Name, Func<LifetimeAimSample, double> Get)[]
        {
            ("landing / centering", x => x.Play.Landing),
            ("arrival timing", x => x.Play.Arrival),
            ("straightness", x => x.Play.Straightness),
            ("braking", x => x.Play.Deceleration),
            ("stability / shake control", x => x.Play.Stability),
            ("ideal path match", x => x.Play.IdealPathMatch)
        };
        var recentMechanics = mechanics.Select(m => (m.Name, Value: Avg(m.Get, recent))).OrderByDescending(x => x.Value).ToList();
        string strongest = recentMechanics.First().Name;
        string weakest = recentMechanics.Last().Name;
        double profDelta = Avg(x => x.Play.Proficiency, recent) - Avg(x => x.Play.Proficiency, all);
        double perfDelta = Avg(x => x.Play.RawAimRating, recent) - Avg(x => x.Play.RawAimRating, all);
        double errDelta = WeightedError(recent) - WeightedError(all);

        string span = all.Count > 1
            ? $"{all[0].Play.TimestampUtc.ToLocalTime():MMM d, yyyy} → {all[^1].Play.TimestampUtc.ToLocalTime():MMM d, yyyy}"
            : all[0].Play.TimestampUtc.ToLocalTime().ToString("MMM d, yyyy");

        double rawSkill = AimRatingUtils.Top100Average(all.Select(x => x.Play));
        var causeSummary = AimErrorDiagnostics.Analyze(data.Transitions);
        string causeProfile = causeSummary.PrimaryCause == AimErrorDiagnostics.Clean
            ? "No recurring movement cause dominates."
            : $"Most common likely cause: {causeSummary.PrimaryCause.ToLowerInvariant()} ({causeSummary.PrimaryShare:0}% of diagnosed problem jumps). {AimErrorDiagnostics.CauseDescription(causeSummary.PrimaryCause)}";
        string causeStreak = causeSummary.LongestStreak is { Count: >= 3 } streak
            ? $" Longest repeated pattern: {streak.Cause.ToLowerInvariant()} ×{streak.Count}. {AimErrorDiagnostics.RepeatedMeaning(streak.Cause)}"
            : "";
        string recentDirection = profDelta > 5 || errDelta < -.02
            ? "Your recent form is trending cleaner than the longer window."
            : profDelta < -5 || errDelta > .02
                ? "Your recent form is a little rougher than the longer window."
                : "Your recent form is close to your longer-term baseline.";

        return
            $"{history.ToUpperInvariant()} · {all.Count:N0} plays · {data.TransitionCount:N0} aim transitions · {span}\r\n\r\n" +
            $"PROFILE · raw aim skill {rawSkill:0} · average center error {WeightedError(all):0.00}R · most common directional error: {data.DominantError.Replace(Environment.NewLine, " ")}. " +
            $"{causeProfile}{causeStreak} Your strongest recent mechanic is {strongest}; the mechanic with the most room is {weakest}.\r\n" +
            $"RECENT FORM · proficiency {profDelta:+0;-0;0}, per-play aim performance {perfDelta:+0;-0;0}, center error {errDelta:+0.00;-0.00;0.00}R versus this history window. {recentDirection} " +
            "The fingerprint shows the overall shape of your aim, the landing map shows where the cursor is actually ending up, and the mechanic cards translate each production score into a movement concept. Open Advanced breakdown only when you want the raw timelines.";
    }

    private void UpdateCards(List<PlayRow> plays)
    {
        cardPlays.Text = plays.Count.ToString("N0");
        cardTransitions.Text = plays.Sum(p => p.TransitionCount).ToString("N0");
        if (plays.Count == 0)
        {
            cardProficiency.Text = cardAimRating.Text = cardPp.Text = cardWeakest.Text = cardZone.Text = "—";
            return;
        }
        cardProficiency.Text = Humanize.ProductionScoreShort(plays.Average(p => p.Proficiency));
        cardAimRating.Text = $"{AimRatingUtils.Top100Average(plays):0}  ({AimRatingUtils.ContributingMapCount(plays)}/100)";
        var ppKnown = plays.Where(p => p.PpEstimate > 0).ToList();
        cardPp.Text = ppKnown.Count > 0 ? $"{ppKnown.Average(p => p.PpEstimate):0} pp" : "—";
        var components = new Dictionary<string, double>
        {
            ["Straightness"] = plays.Average(p => p.Straightness), ["Landing"] = plays.Average(p => p.Landing),
            ["Arrival"] = plays.Average(p => p.Arrival), ["Stability"] = plays.Average(p => p.Stability), ["Deceleration"] = plays.Average(p => p.Deceleration)
        };
        var weak = components.MinBy(kv => kv.Value);
        cardWeakest.Text = $"{weak.Key}\n{weak.Value:0}";
        double pavg = plays.Average(p => p.Proficiency);
        string aggregateZone = ProductionScoring.ClassifyZone(pavg, plays.Average(p => p.Accuracy), plays.Sum(p => p.MissCount));
        cardZone.Text = aggregateZone;
        cardZone.ForeColor = aggregateZone switch { "Mastered" => Theme.Good, "Controlled" => Theme.Accent2, "Challenging" => Theme.Warn, _ => Theme.Bad };
    }

    private void UpdateGraph(List<PlayRow> plays)
    {
        string x = xAxis.SelectedItem?.ToString() ?? "Time";
        string y = yAxis.SelectedItem?.ToString() ?? "Proficiency";
        bool daily = grouping.SelectedItem?.ToString() == "Daily average";

        if (y == "Aim rating")
        {
            if (x != "Time") { xAxis.SelectedItem = "Time"; x = "Time"; }
            var ordered = plays.OrderBy(p => p.TimestampUtc).ToList();
            var cumulative = new List<PlayRow>();
            var pts = new List<GraphPoint>();
            if (daily)
            {
                foreach (var g in ordered.GroupBy(p => p.TimestampUtc.ToLocalTime().Date).OrderBy(g => g.Key))
                {
                    cumulative.AddRange(g);
                    pts.Add(new GraphPoint(g.Key.ToOADate(), AimRatingUtils.Top100Average(cumulative), $"{g.Key:yyyy-MM-dd} · {AimRatingUtils.ContributingMapCount(cumulative)} contributing maps", g.Key));
                }
            }
            else
            {
                foreach (var p in ordered)
                {
                    cumulative.Add(p);
                    pts.Add(new GraphPoint(p.TimestampUtc.ToLocalTime().ToOADate(), AimRatingUtils.Top100Average(cumulative), $"{p.TimestampUtc.ToLocalTime():g} · {p.Map}", p.TimestampUtc.ToLocalTime()));
                }
            }
            graph.SetData(pts, "Time", "Aim rating", true);
            return;
        }

        if (daily && x != "Time") { xAxis.SelectedItem = "Time"; x = "Time"; }

        IReadOnlyList<GraphPoint> BuildPoints(string metric, bool tensionTimesTen = false)
        {
            Func<PlayRow,double> value = ValueY(metric);
            if (daily)
                return plays.GroupBy(p => p.TimestampUtc.ToLocalTime().Date).OrderBy(g => g.Key)
                    .Select(g => new GraphPoint(g.Key.ToOADate(), g.Average(p => value(p)) * (tensionTimesTen ? 10 : 1), $"{g.Key:yyyy-MM-dd} · {g.Count()} plays", g.Key)).ToList();
            return plays.Select(p => new GraphPoint(ValueX(p, x), value(p) * (tensionTimesTen ? 10 : 1), $"{p.TimestampUtc.ToLocalTime():g} · {p.Map} · {p.ModsText} · {p.Grade} · {p.MissCount} miss", p.TimestampUtc.ToLocalTime())).ToList();
        }

        var series = new List<GraphSeries>
        {
            new GraphSeries(y, BuildPoints(y), Theme.Accent, 1f, true)
        };
        void AddOverlay(CheckBox box, string metric, Color color, float opacity, bool tension10 = false)
        {
            if (!box.Checked || string.Equals(metric, y, StringComparison.OrdinalIgnoreCase)) return;
            series.Add(new GraphSeries(tension10 ? "Aim tension ×10" : metric, BuildPoints(metric, tension10), color, opacity, false));
        }
        AddOverlay(trendProficiency, "Proficiency", Theme.Accent2, .78f);
        AddOverlay(trendAim, "Play aim performance", Theme.Good, .74f);
        AddOverlay(trendPp, "PP estimate", Theme.Warn, .74f);
        AddOverlay(trendTension, "Aim tension", Theme.Bad, .62f, true);

        string yTitle = series.Count == 1 ? y : $"{y} + overlays (tension shown ×10)";
        graph.SetSeries(series, x, yTitle, x == "Time");
    }

    private void UpdateGrid(List<PlayRow> plays)
    {
        playsGrid.Rows.Clear();
        IEnumerable<PlayRow> ordered = tableSort.SelectedItem?.ToString() switch
        {
            "Aim performance highest" => plays.OrderByDescending(p => p.RawAimRating).ThenByDescending(p => p.TimestampUtc),
            "PP estimate highest" => plays.OrderByDescending(p => p.PpEstimate).ThenByDescending(p => p.TimestampUtc),
            "Proficiency highest" => plays.OrderByDescending(p => p.Proficiency).ThenByDescending(p => p.TimestampUtc),
            "Aim tension highest" => plays.OrderByDescending(p => p.AimTension).ThenByDescending(p => p.TimestampUtc),
            "Misses lowest" => plays.OrderBy(p => p.MissCount).ThenByDescending(p => p.RawAimRating),
            "Grade highest" => plays.OrderByDescending(p => GradeOrder(p.Grade)).ThenByDescending(p => p.RawAimRating),
            _ => plays.OrderByDescending(p => p.TimestampUtc)
        };

        foreach (var p in ordered.Take(500))
        {
            int i = playsGrid.Rows.Add(p.TimestampUtc.ToLocalTime().ToString("g"), p.Map, p.ModsText, p.Grade, p.MissCount,
                p.StarRating > 0 ? p.StarRating.ToString("0.00") : "—", p.EffectiveAr.ToString("0.0"), p.MeanBpm.ToString("0"), Humanize.Spacing(p.SpacingP75, true),
                p.DensityP90.ToString("0.0"), p.Accuracy.ToString("0.00") + "%", Humanize.ProductionScoreShort(p.Proficiency), p.RawAimRating.ToString("0"),
                p.PpEstimate > 0 ? p.PpEstimate.ToString("0") : "—", Humanize.TensionShort(p.AimTension), p.Zone);
            playsGrid.Rows[i].Tag = p.Id;
            playsGrid.Rows[i].Cells[11].Style.ForeColor = ProficiencyColor(p.Proficiency);
            playsGrid.Rows[i].Cells[15].Style.ForeColor = p.Zone switch { "Mastered" => Theme.Good, "Controlled" => Theme.Accent2, "Challenging" => Theme.Warn, _ => Theme.Bad };
        }

        RefreshPlayInspectorForView(plays);
    }

    private void RefreshPlayInspectorForView(List<PlayRow> plays)
    {
        if (followLatestPlay.Checked)
        {
            // The inspector follows the actual newest analyzed play, independent of graph filters.
            // This makes it useful as immediate post-map feedback even while the dashboard is
            // filtered to a specific skill/rank cohort.
            var latest = allPlays.OrderByDescending(p => p.TimestampUtc).FirstOrDefault();
            ShowPlayInspector(latest);
            return;
        }

        if (inspectorPlayId.HasValue)
        {
            var pinned = allPlays.FirstOrDefault(p => p.Id == inspectorPlayId.Value) ?? database.LoadPlay(inspectorPlayId.Value);
            if (pinned != null) ShowPlayInspector(pinned);
        }
    }

    private void ShowPlayInspector(PlayRow? play, bool force = false)
    {
        if (play != null && !force && inspectorRenderedPlayId == play.Id) return;
        if (play == null)
        {
            ClearEmbeddedInspectorTools();
            ShowInspectorPage("Overview");
            inspectorPlayId = null;
            inspectorRenderedPlayId = null;
            inspectorDiagnosisRenderedPlayId = null;
            inspectorBackgroundKey = "";
            playInspectorHero.SetPlay(null);
            playInspectorTimeline.Clear();
            playInspectorCauseProfile.Clear();
            playInspectorErrorProfile.Clear();
            playInspectorOverview.Text = "No plays match the current dashboard filters.";
            playInspectorTraining.Clear();
            playInspectorComparison.Clear();
            playInspectorErrorsText.Clear();
            playInspectorDiagnosis.Clear();
            playInspectorHistory.Rows.Clear();
            playInspectorErrors.Enabled = playInspectorAdvanced.Enabled = false;
            SetInspectorBackground(null);
            return;
        }

        ClearEmbeddedInspectorTools();
        ShowInspectorPage("Overview");
        inspectorPlayId = play.Id;
        inspectorRenderedPlayId = play.Id;
        inspectorDiagnosisRenderedPlayId = null;
        playInspectorDiagnosis.Text = "Open Diagnosis for an evidence-backed comparison of this run with your similar-map history.";
        playInspectorHero.SetPlay(play);
        var inspectorTransitions = new List<TransitionMetric>();
        try
        {
            inspectorTransitions = database.LoadTransitions(play.Id);
            var timeline = SongTimelineBuilder.Build(play, inspectorTransitions);
            playInspectorTimeline.SetData(timeline, play.Proficiency, play.RawAimRating);
            playInspectorCauseProfile.SetData(inspectorTransitions);
            playInspectorErrorProfile.SetData(inspectorTransitions);
        }
        catch
        {
            playInspectorTimeline.Clear();
            playInspectorCauseProfile.Clear();
            playInspectorErrorProfile.Clear();
        }
        var historySource = allPlays.Any(p => p.Id == play.Id) ? allPlays : database.LoadPlays();
        RefreshInspectorHistory(play, historySource);
        try
        {
            var sections = PlayInsightBuilder.BuildSections(play, database, historySource);
            playInspectorOverview.Text = sections.Overview;
            playInspectorTraining.Text = sections.Training;
            playInspectorComparison.Text = sections.Comparison;
            playInspectorErrorsText.Text = sections.Errors;
        }
        catch (Exception ex)
        {
            playInspectorOverview.Text = "Could not build play insights: " + ex.Message;
            playInspectorTraining.Clear();
            playInspectorComparison.Clear();
            playInspectorErrorsText.Clear();
        }
        playInspectorErrors.Enabled = playInspectorAdvanced.Enabled = true;
        LoadInspectorBackground(play);
    }

    private void RefreshInspectorHistory(PlayRow play, IReadOnlyList<PlayRow> all)
    {
        playInspectorHistory.Rows.Clear();
        bool SameDifficulty(PlayRow p) => !string.IsNullOrWhiteSpace(play.BeatmapHash)
            ? string.Equals(p.BeatmapHash, play.BeatmapHash, StringComparison.OrdinalIgnoreCase)
            : string.Equals(p.Map, play.Map, StringComparison.OrdinalIgnoreCase);

        // Include the selected play and nearby attempts on the exact same difficulty.
        // Deltas are chronological: each row is compared with the attempt immediately before it.
        var history = all.Where(SameDifficulty).OrderBy(p => p.TimestampUtc).ToList();
        int selectedIndex = history.FindIndex(p => p.Id == play.Id);
        if (selectedIndex < 0) return;

        // The Runs page now owns the full inspector body, so show enough attempts to make
        // progression/comparison genuinely useful instead of clipping the history to ~8 rows.
        int first = Math.Max(0, selectedIndex - 20);
        int last = Math.Min(history.Count - 1, selectedIndex + 3);
        var visible = history.Skip(first).Take(last - first + 1).OrderByDescending(p => p.TimestampUtc).ToList();

        foreach (var run in visible)
        {
            int idx = history.FindIndex(p => p.Id == run.Id);
            PlayRow? previous = idx > 0 ? history[idx - 1] : null;
            double? dProf = previous == null ? null : run.Proficiency - previous.Proficiency;
            double? dAim = previous == null ? null : run.RawAimRating - previous.RawAimRating;
            string runLabel = run.Id == play.Id ? "CURRENT" : run.TimestampUtc.ToLocalTime().ToString("M/d h:mm tt");
            int rowIndex = playInspectorHistory.Rows.Add(
                runLabel, run.ModsText, run.Accuracy.ToString("0.00") + "%", run.MissCount,
                Humanize.ProductionScoreShort(run.Proficiency), dProf.HasValue ? "(" + dProf.Value.ToString("+0;-0;0") + ")" : "—",
                run.RawAimRating.ToString("0"), dAim.HasValue ? "(" + dAim.Value.ToString("+0;-0;0") + ")" : "—",
                Humanize.TensionShort(run.AimTension));

            var row = playInspectorHistory.Rows[rowIndex];
            row.Tag = run.Id;
            if (run.Id == play.Id)
            {
                row.DefaultCellStyle.BackColor = Theme.Panel2;
                row.DefaultCellStyle.Font = new Font(playInspectorHistory.Font, FontStyle.Bold);
            }
            ApplyDeltaColor(row.Cells[5], dProf);
            ApplyDeltaColor(row.Cells[7], dAim);
        }
    }

    private static void ApplyDeltaColor(DataGridViewCell cell, double? delta)
    {
        if (!delta.HasValue || Math.Abs(delta.Value) < 0.5)
        {
            cell.Style.ForeColor = Theme.Muted;
            return;
        }
        cell.Style.ForeColor = delta.Value > 0 ? Theme.Good : Theme.Bad;
        cell.Style.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
    }

    private void LoadInspectorBackground(PlayRow play)
    {
        string key = !string.IsNullOrWhiteSpace(play.BeatmapHash) ? play.BeatmapHash : play.BeatmapPath;
        if (!string.IsNullOrWhiteSpace(key) && string.Equals(key, inspectorBackgroundKey, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            BeatmapData? map = null;
            if (!string.IsNullOrWhiteSpace(play.BeatmapPath) && File.Exists(play.BeatmapPath))
                map = BeatmapParser.Parse(play.BeatmapPath);
            map ??= resolver.Resolve(play.BeatmapHash);
            string? bg = map?.BackgroundPath;
            if (string.IsNullOrWhiteSpace(bg) || !File.Exists(bg)) { SetInspectorBackground(null); return; }
            using var stream = new FileStream(bg, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var source = Image.FromStream(stream);
            SetInspectorBackground(new Bitmap(source));
            inspectorBackgroundKey = key;
        }
        catch { SetInspectorBackground(null); inspectorBackgroundKey = ""; }
    }

    private void SetInspectorBackground(Image? image)
    {
        playInspectorHero.SetBackgroundImage(image);
        if (image == null) inspectorBackgroundKey = "";
    }

    private async Task GenerateInspectorErrorsAsync()
    {
        if (!inspectorPlayId.HasValue) return;
        long requestedPlayId = inspectorPlayId.Value;
        var play = allPlays.FirstOrDefault(p => p.Id == requestedPlayId) ?? database.LoadPlay(requestedPlayId);
        if (play == null) return;

        ShowInspectorPage("Top errors");
        int requestedCount = (int)playInspectorErrorCount.Value;
        if (embeddedErrorVisualizer != null && embeddedErrorCount == requestedCount) return;
        playInspectorErrors.Enabled = false;
        playInspectorTopErrorsHost.Controls.Clear();
        playInspectorTopErrorsHost.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Reconstructing cursor paths for the worst aim errors…",
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 10f),
            TextAlign = ContentAlignment.MiddleCenter
        });

        try
        {
            SetStatus($"Reconstructing top aim errors · {play.Map}");
            var samples = await ErrorVisualBuilder.GenerateAsync(play, database, resolver, settings, requestedCount);
            if (inspectorPlayId != requestedPlayId) return; // selection changed while replay data was being reconstructed

            playInspectorTopErrorsHost.Controls.Clear();
            if (samples.Count == 0)
            {
                playInspectorTopErrorsHost.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    Text = "No reconstructable aim transitions were found for this play.\r\nThe original replay or exact beatmap version may no longer exist locally.",
                    ForeColor = Theme.Muted,
                    Font = new Font("Segoe UI", 9.5f),
                    TextAlign = ContentAlignment.MiddleCenter
                });
                return;
            }

            try { embeddedErrorVisualizer?.Close(); embeddedErrorVisualizer?.Dispose(); } catch { }
            embeddedErrorVisualizer = new ErrorVisualizerForm(play, samples)
            {
                TopLevel = false,
                FormBorderStyle = FormBorderStyle.None,
                Dock = DockStyle.Fill,
                MinimumSize = Size.Empty
            };
            playInspectorTopErrorsHost.Controls.Add(embeddedErrorVisualizer);
            embeddedErrorVisualizer.Show();
            embeddedErrorCount = requestedCount;
        }
        catch (Exception ex)
        {
            playInspectorTopErrorsHost.Controls.Clear();
            playInspectorTopErrorsHost.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "Could not generate the cursor-path view.\r\n" + ex.Message,
                ForeColor = Theme.Bad,
                Font = new Font("Segoe UI", 9.5f),
                TextAlign = ContentAlignment.MiddleCenter
            });
        }
        finally
        {
            playInspectorErrors.Enabled = inspectorPlayId.HasValue;
            SetStatus("Ready");
        }
    }

    private void UpdateDiagnostics(List<PlayRow> plays)
    {
        if (plays.Count == 0)
        {
            diagnosticOverview.Text = "No plays match the current filters.\r\n\r\nWiden the grade, miss-count, BPM, spacing, star-rating, or density filters to generate session insights.";
            return;
        }

        var errors = database.LoadErrorClasses(plays.Select(p => p.Id));
        int totalErrors = errors.Values.Sum();
        string ErrorPct(string k) => totalErrors == 0 ? "0%" : $"{100.0 * errors.GetValueOrDefault(k) / totalErrors:0.0}%";

        var components = new[]
        {
            (Name: "straightness", Value: plays.Average(p => p.Straightness)),
            (Name: "landing", Value: plays.Average(p => p.Landing)),
            (Name: "arrival timing", Value: plays.Average(p => p.Arrival)),
            (Name: "landing stability", Value: plays.Average(p => p.Stability)),
            (Name: "deceleration", Value: plays.Average(p => p.Deceleration))
        };
        var weakest = components.MinBy(x => x.Value);
        var strongest = components.MaxBy(x => x.Value);

        var peak = plays.MaxBy(p => p.RawAimRating)!;
        var cleanest = plays.MaxBy(p => p.Proficiency)!;
        int mastered = plays.Count(p => p.Zone == "Mastered");
        int controlled = plays.Count(p => p.Zone == "Controlled");
        int challenging = plays.Count(p => p.Zone == "Challenging");
        int breakdown = plays.Count(p => p.Zone == "Breakdown");

        var buckets = plays
            .GroupBy(p => (Bpm: ((int)p.MeanBpm / 20) * 20, Spacing: ((int)p.SpacingP75 / 25) * 25))
            .Select(g => new
            {
                g.Key.Bpm,
                g.Key.Spacing,
                Count = g.Count(),
                Prof = g.Average(p => p.Proficiency),
                Rating = g.Max(p => p.RawAimRating),
                Challenge = g.Average(p => p.AimChallenge),
                Accuracy = g.Average(p => p.Accuracy)
            })
            .Where(x => x.Count >= 2)
            .ToList();

        var trainable = buckets
            .Where(x => x.Prof >= ProductionScoring.ChallengingThreshold && x.Prof < ProductionScoring.MasteredThreshold)
            .OrderByDescending(x => x.Challenge)
            .ThenBy(x => x.Prof)
            .FirstOrDefault();

        var strongestRepeated = buckets
            .Where(x => x.Prof >= ProductionScoring.ControlledThreshold)
            .OrderByDescending(x => x.Challenge)
            .FirstOrDefault();

        var breakdownFrontier = plays
            .Where(p => p.Zone == "Breakdown")
            .OrderBy(p => p.AimChallenge)
            .ThenByDescending(p => p.Proficiency)
            .FirstOrDefault();

        string dominantError = totalErrors == 0
            ? "not enough transition errors yet"
            : errors.OrderByDescending(kv => kv.Value).First().Key;

        string mechanicAdvice = weakest.Name switch
        {
            "straightness" => "Your largest loss is path efficiency. Look for unnecessary curvature/corrections between objects, especially on direction changes.",
            "landing" => "Your largest loss is target centering. Check whether the dominant error is overaim, underaim, or lateral drift and train that spacing/BPM band.",
            "arrival timing" => "Your largest loss is cursor arrival timing. You are reaching targets inconsistently relative to hit time even when score accuracy can remain high.",
            "landing stability" => "Your largest loss is post-arrival stability. The cursor is continuing to move/shake after reaching objects instead of settling cleanly.",
            "deceleration" => "Your largest loss is braking quality. You are carrying too much terminal velocity or making late corrective braking near the target.",
            _ => ""
        };

        string gradeScope = gradeFilter.SelectedItem?.ToString() ?? "Any grade";
        string missScope = $"{missMin.Value:0}–{missMax.Value:0} misses";
        string starCoverage = plays.Any(p => p.StarRating > 0)
            ? $"{plays.Where(p => p.StarRating > 0).Min(p => p.StarRating):0.00}–{plays.Where(p => p.StarRating > 0).Max(p => p.StarRating):0.00}★"
            : "no cached star ratings in this selection";

        var sb = new StringBuilder();
        sb.AppendLine($"FILTERED DATA · {plays.Count:N0} plays · {plays.Sum(p => p.TransitionCount):N0} aim transitions · {gradeScope} · {missScope}");
        sb.AppendLine($"Coverage: {plays.Min(p => p.MeanBpm):0}–{plays.Max(p => p.MeanBpm):0} BPM · {plays.Min(p => p.SpacingP75):0}–{plays.Max(p => p.SpacingP75):0}px P75 spacing · {starCoverage}");
        sb.AppendLine();

        sb.AppendLine($"PEAK AIM PERFORMANCE · {peak.RawAimRating:0} rating · {peak.Proficiency:0} proficiency · {peak.Accuracy:0.00}% · {peak.Grade} · {peak.MissCount} miss");
        sb.AppendLine($"{peak.Map} [{peak.ModsText}] · {peak.MeanBpm:0} BPM · {peak.SpacingP75:0}px · {peak.StarRating:0.00}★");
        if (cleanest.Id != peak.Id)
            sb.AppendLine($"Cleanest execution: {cleanest.Proficiency:0} proficiency on {cleanest.Map} ({cleanest.Accuracy:0.00}%, {cleanest.MissCount} miss)");
        sb.AppendLine();

        sb.AppendLine($"MECHANICS · strongest {strongest.Name} {strongest.Value:0}/1000 · weakest {weakest.Name} {weakest.Value:0}/1000");
        sb.AppendLine($"Error profile: underaim {ErrorPct("Underaim")} · overaim {ErrorPct("Overaim")} · lateral {ErrorPct("Lateral")} · correction {ErrorPct("Correction")} · plain error {ErrorPct("Plain error")} · dominant: {dominantError}");
        sb.AppendLine(mechanicAdvice);
        sb.AppendLine();

        sb.AppendLine($"CONTROL ZONES · {mastered} mastered · {controlled} controlled · {challenging} challenging · {breakdown} breakdown");
        if (strongestRepeated != null)
            sb.AppendLine($"Highest repeated controlled slice: {strongestRepeated.Bpm}–{strongestRepeated.Bpm + 19} BPM / {strongestRepeated.Spacing}–{strongestRepeated.Spacing + 24}px · {strongestRepeated.Prof:0} avg proficiency across {strongestRepeated.Count} plays.");
        else
            sb.AppendLine("Highest repeated controlled slice: not enough repeated controlled BPM/spacing data yet.");

        if (trainable != null)
            sb.AppendLine($"Training frontier: {trainable.Bpm}–{trainable.Bpm + 19} BPM / {trainable.Spacing}–{trainable.Spacing + 24}px · {trainable.Prof:0} avg proficiency · {trainable.Accuracy:0.00}% avg accuracy across {trainable.Count} plays.");
        else
            sb.AppendLine("Training frontier: not enough repeated challenging BPM/spacing slices yet.");

        if (breakdownFrontier != null)
            sb.AppendLine($"First observed breakdown frontier: aim challenge {breakdownFrontier.AimChallenge:0.0} · {breakdownFrontier.MeanBpm:0} BPM / {breakdownFrontier.SpacingP75:0}px · {breakdownFrontier.Proficiency:0} proficiency on {breakdownFrontier.Map}.");
        else
            sb.AppendLine("Breakdown frontier: none in the current filtered data.");

        diagnosticOverview.Text = sb.ToString();
        diagnosticOverview.SelectionStart = 0;
        diagnosticOverview.SelectionLength = 0;
        diagnosticOverview.ScrollToCaret();
    }

    private IEnumerable<PlayRow> ScopeByHistory(IEnumerable<PlayRow> source, string range)
    {
        return range switch
        {
            "Today" => source.Where(p => p.TimestampUtc.ToLocalTime().Date == DateTime.Today),
            "7 days" => source.Where(p => p.TimestampUtc >= DateTime.UtcNow.AddDays(-7)),
            "30 days" => source.Where(p => p.TimestampUtc >= DateTime.UtcNow.AddDays(-30)),
            _ => source
        };
    }

    private void RefreshDiagnosticsTab()
    {
        if (!IsHandleCreated) return;
        string signature = $"{diagnosticRange.SelectedItem}|{diagnosticPreset.SelectedItem}|{diagnosticMinSamples.Value}";
        if (diagnosticsRevision == dataRevision && diagnosticsSignature == signature) return;
        diagnosticsRevision = dataRevision; diagnosticsSignature = signature;
        allPlays = database.LoadPlays();
        var scope = ScopeByHistory(allPlays, diagnosticRange.SelectedItem?.ToString() ?? "All time").ToList();
        var engine = new DiagnosticsEngine(database);
        string preset = diagnosticPreset.SelectedItem?.ToString() ?? "All aim";
        diagnosticPresetHelp.Text = DiagnosticsEngine.PresetDescription(preset);
        currentCohorts = engine.BuildCohorts(scope, (int)diagnosticMinSamples.Value, preset);
        diagnosticGrid.Rows.Clear();
        foreach (var c in currentCohorts)
        {
            int i = diagnosticGrid.Rows.Add(
                c.Label, c.Count.ToString("N0"), Humanize.ScoreShort(c.Proficiency), Humanize.ScoreShort(c.Straightness),
                Humanize.ScoreShort(c.Landing), Humanize.ScoreShort(c.Arrival), Humanize.ScoreShort(c.Stability), Humanize.ScoreShort(c.Deceleration),
                Humanize.TensionShort(c.AimTension), c.StarMedian > 0 ? c.StarMedian.ToString("0.00") + "★" : "—",
                c.ArMedian.ToString("0.0"), c.BpmMedian.ToString("0"), Humanize.Spacing(c.SpacingMedian, true), c.DensityMedian.ToString("0.0"), c.Weakest);
            diagnosticGrid.Rows[i].Tag = c;
        }
        if (currentCohorts.Count == 0)
        {
            string text = $"Not enough transitions match the selected preset yet. This history window has {scope.Count:N0} plays. Try another preset, lower Min samples, or import more replay history.\r\n\r\nPreset: {DiagnosticsEngine.PresetDescription(preset)}";
            diagnosticOverview.Text = text; diagnosticMechanics.Text = text; diagnosticDemand.Text = text; diagnosticAction.Text = text;
        }
        else
        {
            diagnosticGrid.ClearSelection(); diagnosticGrid.Rows[0].Selected = true; ShowSelectedCohort();
        }
    }

    private void ShowSelectedCohort()
    {
        if (diagnosticGrid.SelectedRows.Count == 0 || diagnosticGrid.SelectedRows[0].Tag is not DiagnosticCohort c) return;
        string severity = c.Proficiency switch { >= 920 => "Mastered", >= 850 => "Controlled", >= 725 => "Challenging", _ => "Breakdown" };
        string weakDelta = Humanize.Difference(c.WeakestScore, c.WeakestBaseline);
        string spacingRange = Humanize.SpacingRange(c.SpacingMin, c.SpacingMax);
        string starTypical = c.StarMedian > 0 ? $"{c.StarMedian:0.00}★" : "unknown star rating";
        string starRange = c.StarMedian > 0 ? $"{c.StarMin:0.00}–{c.StarMax:0.00}★" : "star rating unavailable";

        diagnosticOverview.Text =
            $"{c.Label.ToUpperInvariant()} · {severity}\r\n\r\n" +
            $"{c.Insight}\r\n\r\n" +
            $"Overall control: {Humanize.Score(c.Proficiency)}\r\n" +
            $"Main limiter: {c.Weakest}\r\n" +
            $"Inferred aim tension: {Humanize.Tension(c.AimTension)} ({c.TensionDelta:+0;-0;0} vs baseline)\r\n" +
            $"Source-play accuracy: {c.Accuracy:0.00}%\r\n" +
            $"Samples: {c.Count:N0} individual aim transitions.";

        diagnosticMechanics.Text =
            $"PATH / CONTROL\r\n" +
            $"Path straightness: {Humanize.Score(c.Straightness)}\r\n" +
            $"Landing / centering: {Humanize.Score(c.Landing)}\r\n" +
            $"Arrival timing: {Humanize.Score(c.Arrival)}\r\n" +
            $"Post-landing stability: {Humanize.Score(c.Stability)}\r\n" +
            $"Braking / deceleration: {Humanize.Score(c.Deceleration)}\r\n\r\n" +
            $"BIGGEST CONDITIONAL WEAKNESS\r\n{c.Weakest}: {Humanize.Score(c.WeakestScore)}; your selected-history baseline for the same mechanic is {c.WeakestBaseline:0}/1000 ({weakDelta}).\r\n\r\n" +
            $"TENSION\r\n{Humanize.Tension(c.AimTension)}. High-BPM and AR10+ movement is intentionally judged more strictly because visible shake/correction there means less spare control margin.";

        diagnosticDemand.Text =
            $"WHAT THIS MAP TYPE FEELS LIKE\r\n" +
            $"Typical difficulty: {starTypical}\r\n" +
            $"Typical AR: {c.ArMedian:0.0}\r\n" +
            $"Typical BPM: {c.BpmMedian:0}\r\n" +
            $"Typical jump: {Humanize.Spacing(c.SpacingMedian)}\r\n" +
            $"Typical interval: {Humanize.Interval(c.IntervalMedian)}\r\n" +
            $"Visible objects: {Humanize.Density(c.DensityMedian)}\r\n\r\n" +
            $"OBSERVED RANGE\r\n{starRange} · AR {c.ArMin:0.0}–{c.ArMax:0.0} · {c.BpmMin:0}–{c.BpmMax:0} BPM · {spacingRange} · {c.DensityMin:0.0}–{c.DensityMax:0.0} visible objects.";

        string action = severity switch
        {
            "Mastered" => "This is already a strength. Move to a slightly harder version of the same skill instead of adding lots of easy volume.",
            "Controlled" => $"This is under control. Increase one demand at a time—stars, BPM, spacing, density or AR—and watch {c.Weakest} first.",
            "Challenging" => $"This is a useful main-volume training range. Keep meaningful volume here and improve {c.Weakest} before making the maps much harder.",
            _ => $"This is beyond your clean-control range. Back off one variable until {c.Weakest} and tension recover."
        };
        diagnosticAction.Text =
            $"WHAT TO DO\r\n{action}\r\n\r\n" +
            $"{DiagnosticsEngine.PresetGuideline(diagnosticPreset.SelectedItem?.ToString() ?? "All aim")}\r\n\r\n" +
            $"Useful anchor: {starTypical}, AR {c.ArMedian:0.0}, {c.BpmMedian:0} BPM, {Humanize.Spacing(c.SpacingMedian, true)}.\r\n" +
            $"Peak play aim performance represented in this cohort: {c.PeakAimRating:0}.";

        foreach (var box in new[] { diagnosticOverview, diagnosticMechanics, diagnosticDemand, diagnosticAction }) { box.SelectionStart = 0; box.ScrollToCaret(); }
    }

    private void RefreshTrainingTab()
    {
        if (!IsHandleCreated) return;
        string signature = $"{trainingRange.SelectedItem}|{trainingPreset.SelectedItem}|{trainingDimension.SelectedItem}|{trainingLeniency.Value}";
        if (trainingRevision == dataRevision && trainingSignature == signature) return;
        trainingRevision = dataRevision; trainingSignature = signature;
        allPlays = database.LoadPlays();
        var scope = ScopeByHistory(allPlays, trainingRange.SelectedItem?.ToString() ?? "All time").ToList();
        string dim = trainingDimension.SelectedItem?.ToString() ?? "Spacing";
        string preset = trainingPreset.SelectedItem?.ToString() ?? "All aim";
        int leniency = (int)trainingLeniency.Value;
        var engine = new TrainingEngine(database);
        currentTraining = engine.Build(scope, dim, preset, 30, leniency);

        trainingMapGrid.Rows.Clear();
        trainingMapListGrid.Rows.Clear();
        var maps = engine.BuildMapRecommendations(scope, preset, leniency, 40);
        foreach (var m in maps)
        {
            object[] cells = { m.Map, m.Mods, m.Star > 0 ? m.Star.ToString("0.00") + "★" : "—", $"AR {m.Ar:0.0}", $"{m.Bpm:0} BPM", Humanize.Spacing(m.Spacing, true), Humanize.Density(m.Density), Humanize.ScoreShort(m.Proficiency), Humanize.TensionShort(m.AimTension), m.Accuracy.ToString("0.00") + "%" };
            int full = trainingMapListGrid.Rows.Add(cells);
            trainingMapListGrid.Rows[full].Tag = m;
            if (trainingMapGrid.Rows.Count < 8)
            {
                int i = trainingMapGrid.Rows.Add(cells);
                trainingMapGrid.Rows[i].Tag = m;
            }
        }

        trainingGrid.Rows.Clear();
        foreach (var r in currentTraining)
        {
            string reference = r.ExampleMaps.FirstOrDefault()?.Map ?? "—";
            int i = trainingGrid.Rows.Add(r.Range, r.Status, r.Samples, Humanize.ScoreShort(r.CurrentProficiency), Humanize.TensionShort(r.CurrentTension), r.TargetStar, r.TargetAr, r.TargetBpm, r.TargetSpacing, r.TargetDensity, r.Focus, reference);
            if (r.ExampleMaps.Count > 0) trainingGrid.Rows[i].Cells[11].ToolTipText = string.Join("\n", r.ExampleMaps.Select(x => x.Map));
            trainingGrid.Rows[i].Tag = r;
        }
        if (currentTraining.Count == 0) trainingDetail.Text = "Not enough replay data in this history window / skill preset to infer training targets.";
        else
        {
            trainingGrid.ClearSelection(); trainingGrid.Rows[0].Selected = true; ShowSelectedTraining();
        }
        UpdateTrainingQuota(preset);
    }

    private void ShowSelectedTraining()
    {
        if (trainingGrid.SelectedRows.Count == 0 || trainingGrid.SelectedRows[0].Tag is not TrainingRecommendation r) return;
        string preset = trainingPreset.SelectedItem?.ToString() ?? "All aim";
        string examples = r.ExampleMaps.Count == 0
            ? "No close reference map was found in your analyzed history yet."
            : string.Join("\r\n", r.ExampleMaps.Select((m, i) =>
                $"{i + 1}. {m.Map} [{m.Mods}] · {(m.Star > 0 ? $"{m.Star:0.00}★ · " : "")}AR {m.Ar:0.0} · {m.Bpm:0} BPM · {Humanize.Spacing(m.Spacing, true)} · control {m.Proficiency:0}/1000 · {m.Accuracy:0.00}%"));

        trainingDetail.Text =
            $"{preset.ToUpperInvariant()} · {r.Range.ToUpperInvariant()}\r\n\r\n" +
            $"SIMPLE MAP-PICKING RULE\r\n{DiagnosticsEngine.PresetGuideline(preset)}\r\n\r\n" +
            $"CURRENT LEVEL\r\nControl is {Humanize.Score(r.CurrentProficiency)} across {r.Samples:N0} aim transitions. Inferred aim tension: {Humanize.Tension(r.CurrentTension)}.\r\n\r\n" +
            $"RECOMMENDED PRACTICE TARGET\r\n{r.TargetSummary}\r\n\r\n" +
            $"Easy-to-read band: {r.TargetStar} · {r.TargetAr} · {r.TargetBpm} · {r.TargetSpacing} · {r.TargetDensity}.\r\n" +
            $"Main thing to train: {r.Focus}.\r\n\r\n" +
            $"REFERENCE MAPS YOU HAVE ACTUALLY PLAYED\r\n{examples}\r\n\r\n" +
            $"WHY\r\n{r.Rationale}\r\n\r\n" +
            "Spacing is normalized to CS4: ~73 px = 1 circle diameter; 146 px = 2; 219 px = 3; 292 px = 4.";
        trainingDetail.SelectionStart = 0; trainingDetail.ScrollToCaret();
    }

    private void UpdateTrainingQuota(string preset)
    {
        var ordered = allPlays.OrderBy(p => p.TimestampUtc).ToList();
        if (ordered.Count == 0) { trainingQuota.Text = "No session data yet."; return; }
        var latest = new List<PlayRow> { ordered[^1] };
        for (int i = ordered.Count - 2; i >= 0; i--)
        {
            if ((latest[^1].TimestampUtc - ordered[i].TimestampUtc).TotalMinutes > settings.SessionGapMinutes) break;
            latest.Add(ordered[i]);
        }
        latest = latest.Where(p => DiagnosticsEngine.PlayMatchesPreset(p, preset)).OrderBy(p => p.TimestampUtc).ToList();
        int n = latest.Count;
        if (n == 0) { trainingQuota.Text = $"No plays from the latest session match {preset}."; return; }
        int m = latest.Count(p => p.Zone == "Mastered"), c = latest.Count(p => p.Zone == "Controlled"), ch = latest.Count(p => p.Zone == "Challenging"), b = latest.Count(p => p.Zone == "Breakdown");
        int targetMastered = Math.Max(1, (int)Math.Round(n * .15));
        int targetControlled = Math.Max(1, (int)Math.Round(n * .45));
        int targetChallenging = Math.Max(1, (int)Math.Round(n * .35));
        int targetBreakdown = Math.Max(0, n - targetMastered - targetControlled - targetChallenging);
        trainingQuota.Text =
            $"SESSION QUOTA · {preset} · {n} matching maps so far\r\n\r\n" +
            $"Warm-up / mastery: {m}/{n} maps · target about {targetMastered}/{n}\r\n" +
            $"Main volume (Controlled): {c}/{n} maps · target about {targetControlled}/{n}\r\n" +
            $"Adaptation / overload (Challenging): {ch}/{n} maps · target about {targetChallenging}/{n}\r\n" +
            $"Breakdown / limit tests: {b}/{n} maps · target no more than about {targetBreakdown}/{n}\r\n\r\n" +
            "The goal is not to hit these counts exactly. Use them as a session-balance check: most maps should be clean enough to accumulate quality repetitions, a meaningful minority should challenge you, and very little volume should be complete mechanical breakdown.";
        trainingQuota.SelectionStart = 0; trainingQuota.ScrollToCaret();
    }

    private void ConfigureDiagnosticGrid()
    {
        ToolkitUi.StyleDataGrid(diagnosticGrid);
        diagnosticGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        (string key, string title, int width)[] cols =
        {
            ("cohort","Map type / condition",230), ("samples","Aim transitions",90), ("prof","Overall control /1000",125),
            ("straight","Path straightness",125), ("landing","Landing / centering",130), ("arrival","Arrival timing",120),
            ("stability","Landing stability",125), ("decel","Braking / decel",125), ("tension","Aim tension /100",120),
            ("star","Typical ★",82), ("ar","Typical AR",82), ("bpm","Typical BPM",88), ("spacing","Typical spacing",145),
            ("density","Visible objects",95), ("weakest","Main limiter",180)
        };
        foreach (var c in cols)
        {
            int idx = diagnosticGrid.Columns.Add(c.key, c.title);
            diagnosticGrid.Columns[idx].Width = c.width;
        }
        diagnosticGrid.Columns["prof"].HeaderCell.ToolTipText = "Average transition proficiency for this map type. Higher = cleaner movement.";
        diagnosticGrid.Columns["tension"].HeaderCell.ToolTipText = "Inferred replay-visible aim tension. High BPM and AR10+ are judged more strictly.";
        diagnosticGrid.Columns["star"].HeaderCell.ToolTipText = "Median modded star rating of source plays represented in this cohort.";
        diagnosticGrid.Columns["ar"].HeaderCell.ToolTipText = "Median effective AR after mods.";
        diagnosticGrid.Columns["spacing"].HeaderCell.ToolTipText = "CS4-normalized center-to-center jump distance. About 73 px = one CS4 circle diameter.";
        diagnosticGrid.SelectionChanged += (_, _) => ShowSelectedCohort();
    }

    private void ConfigureTrainingGrid()
    {
        ToolkitUi.StyleDataGrid(trainingGrid);
        trainingGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        (string key, string title, int width)[] cols =
        {
            ("range","Skill range",165), ("status","What to do",190), ("samples","Aim transitions",90), ("prof","Current control /1000",125),
            ("tension","Aim tension /100",115), ("star","Target ★",115), ("ar","Target AR",105), ("bpm","Target BPM",105),
            ("spacing","Target spacing",170), ("density","Target visible objects",130), ("focus","Main focus",180), ("reference","Closest reference map",250)
        };
        foreach (var c in cols)
        {
            int idx = trainingGrid.Columns.Add(c.key, c.title);
            trainingGrid.Columns[idx].Width = c.width;
        }
        trainingGrid.SelectionChanged += (_, _) => ShowSelectedTraining();
    }

    private void ConfigureTrainingMapGrid()
    {
        ToolkitUi.StyleDataGrid(trainingMapGrid);
        trainingMapGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        (string key, string title, int width)[] cols =
        {
            ("map","Recommended map from your history",390), ("mods","Mods",60), ("star","★",72), ("ar","AR",75), ("bpm","BPM",85),
            ("spacing","Typical spacing",155), ("density","Visible objects",115), ("prof","Control",120), ("tension","Aim tension",115), ("acc","Accuracy",90)
        };
        foreach (var c in cols) { int idx = trainingMapGrid.Columns.Add(c.key, c.title); trainingMapGrid.Columns[idx].Width = c.width; }
        trainingMapGrid.Columns["spacing"].HeaderCell.ToolTipText = "CS4-normalized spacing with circle-diameter reference.";
    }

    private void ConfigureTrainingMapListGrid()
    {
        ToolkitUi.StyleDataGrid(trainingMapListGrid);
        trainingMapListGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        (string key, string title, int width)[] cols =
        {
            ("map","Recommended map from your history",430), ("mods","Mods",60), ("star","★",72), ("ar","AR",75), ("bpm","BPM",85),
            ("spacing","Typical spacing",165), ("density","Visible objects",120), ("prof","Control",120), ("tension","Aim tension",115), ("acc","Accuracy",90)
        };
        foreach (var c in cols) { int idx = trainingMapListGrid.Columns.Add(c.key, c.title); trainingMapListGrid.Columns[idx].Width = c.width; }
        trainingMapListGrid.Columns["spacing"].HeaderCell.ToolTipText = "CS4-normalized spacing with circle-diameter reference.";
    }

    private static void ConfigureDarkGrid(DataGridView g) => ToolkitUi.StyleDataGrid(g);

    private void OpenSelectedPlayDiagnostics()
    {
        if (playsGrid.SelectedRows.Count == 0 || playsGrid.SelectedRows[0].Tag is not long id) return;
        OpenPlayDiagnostics(id);
    }

    private void OpenPlayDiagnostics(long id)
    {
        var play = allPlays.FirstOrDefault(p => p.Id == id) ?? database.LoadPlay(id);
        if (play == null) return;
        using var form = new AdvancedDiagnosticsForm(play, database, resolver, settings);
        form.ShowDialog(this);
    }

    private void ConfigureGrid()
    {
        ToolkitUi.StyleDataGrid(playsGrid);
        playsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        playsGrid.Columns.Add("time", "Time"); playsGrid.Columns.Add("map", "Map"); playsGrid.Columns.Add("mods", "Mods"); playsGrid.Columns.Add("grade", "Grade"); playsGrid.Columns.Add("miss", "Miss");
        playsGrid.Columns.Add("star", "★"); playsGrid.Columns.Add("ar", "AR"); playsGrid.Columns.Add("bpm", "BPM"); playsGrid.Columns.Add("spacing", "P75 spacing"); playsGrid.Columns.Add("density", "P90 visible");
        playsGrid.Columns.Add("acc", "Acc"); playsGrid.Columns.Add("prof", "Proficiency"); playsGrid.Columns.Add("rating", "Aim perf."); playsGrid.Columns.Add("pp", "PP est."); playsGrid.Columns.Add("tension", "Aim tension"); playsGrid.Columns.Add("zone", "Zone");
        int[] widths = {125,340,55,52,48,50,50,58,125,78,70,118,78,68,110,88};
        for (int i = 0; i < widths.Length; i++) playsGrid.Columns[i].Width = widths[i];
        playsGrid.Columns["pp"].HeaderCell.ToolTipText = "Local PP estimate for trend/reference use. Not an exact live osu! pp calculation.";
        playsGrid.Columns["tension"].HeaderCell.ToolTipText = "Inferred 0–100 replay-visible aim tension. High BPM and AR10+ are judged more strictly.";
        playsGrid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || playsGrid.Rows[e.RowIndex].Tag is not long id) return;
            followLatestPlay.Checked = false;
            var play = allPlays.FirstOrDefault(p => p.Id == id) ?? database.LoadPlay(id);
            ShowPlayInspector(play);
        };
        playsGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelectedPlayDiagnostics(); };
    }

    private void WireEvents()
    {
        followLatestPlay.CheckedChanged += (_, _) =>
        {
            if (!IsHandleCreated || !followLatestPlay.Checked) return;
            ShowPlayInspector(allPlays.OrderByDescending(p => p.TimestampUtc).FirstOrDefault());
        };
        playInspectorAdvanced.Click += (_, _) => ShowInspectorAdvancedInline();
        playInspectorErrors.Click += async (_, _) => await GenerateInspectorErrorsAsync();
        foreach (var c in new Control[] { timeRange, xAxis, yAxis, grouping, gradeFilter, tableSort, missMin, missMax, bpmMin, bpmMax, spacingMin, spacingMax, starMin, starMax, densityMin, densityMax, trendProficiency, trendAim, trendPp, trendTension })
        {
            if (c is ComboBox cb) cb.SelectedIndexChanged += (_, _) => { if (IsHandleCreated) RefreshDashboard(); };
            if (c is NumericUpDown nu) nu.ValueChanged += (_, _) => { if (IsHandleCreated) RefreshDashboard(); };
            if (c is CheckBox ck) ck.CheckedChanged += (_, _) => { if (IsHandleCreated) RefreshDashboard(); };
        }
        diagnosticRange.SelectedIndexChanged += (_, _) => { if (IsHandleCreated && mainTabs?.SelectedTab?.Text == "Diagnostics") RefreshDiagnosticsTab(); };
        diagnosticPreset.SelectedIndexChanged += (_, _) => { diagnosticPresetHelp.Text = DiagnosticsEngine.PresetDescription(diagnosticPreset.SelectedItem?.ToString() ?? "All aim"); if (IsHandleCreated && mainTabs?.SelectedTab?.Text == "Diagnostics") RefreshDiagnosticsTab(); };
        diagnosticMinSamples.ValueChanged += (_, _) => { if (IsHandleCreated && mainTabs?.SelectedTab?.Text == "Diagnostics") RefreshDiagnosticsTab(); };
        trainingDimension.SelectedIndexChanged += (_, _) => { if (IsHandleCreated && mainTabs?.SelectedTab?.Text == "Training") RefreshTrainingTab(); };
        trainingRange.SelectedIndexChanged += (_, _) => { if (IsHandleCreated && mainTabs?.SelectedTab?.Text == "Training") RefreshTrainingTab(); };
        trainingPreset.SelectedIndexChanged += (_, _) => { trainingPresetHelp.Text = DiagnosticsEngine.PresetGuideline(trainingPreset.SelectedItem?.ToString() ?? "All aim"); if (IsHandleCreated && mainTabs?.SelectedTab?.Text == "Training") RefreshTrainingTab(); };
        trainingLeniency.ValueChanged += (_, _) => { if (IsHandleCreated && mainTabs?.SelectedTab?.Text == "Training") RefreshTrainingTab(); };
        aimAnalysisRange.SelectedIndexChanged += (_, _) =>
        {
            if (syncingAimContextRange) return;
            SyncAimContextRange(aimAnalysisRange.SelectedItem?.ToString() ?? "All time");
            if (IsHandleCreated && mainTabs?.SelectedTab?.Text == "Aim analysis") RefreshAimAnalysisTab();
        };
        aimContextRange.SelectedIndexChanged += (_, _) =>
        {
            if (syncingAimContextRange) return;
            syncingAimContextRange = true;
            try { aimAnalysisRange.SelectedItem = aimContextRange.SelectedItem?.ToString() ?? "All time"; }
            finally { syncingAimContextRange = false; }
            if (IsHandleCreated && mainTabs?.SelectedTab?.Text == "Aim analysis") RefreshAimAnalysisTab();
        };
        aimAnalysisGrouping.SelectedIndexChanged += (_, _) =>
        {
            if (IsHandleCreated && aimAdvancedVisible && currentAimAnalysisData != null)
                RefreshAimAdvancedBreakdown(currentAimAnalysisData, aimAnalysisGrouping.SelectedItem?.ToString() ?? "Play by play");
        };
        collectionBackfillRange.SelectedIndexChanged += (_, _) => { if (IsHandleCreated) UpdateCollectionBackfillPreview(); };
    }

    private void LoadSettingsIntoUi()
    {
        osuPath.Text = settings.OsuDirectory; songsPath.Text = settings.SongsDirectory; replayPath.Text = settings.ReplayDirectory;
        watchCheck.Checked = settings.WatchReplaysAutomatically; pureJumpCheck.Checked = settings.AnalyzeOnlyPureJumps;
        sessionGap.Value = Math.Clamp(settings.SessionGapMinutes, (int)sessionGap.Minimum, (int)sessionGap.Maximum);
        int backfillIndex = replayBackfill.Items.IndexOf(settings.ReplayBackfillRange);
        replayBackfill.SelectedIndex = backfillIndex >= 0 ? backfillIndex : replayBackfill.Items.IndexOf("30 days");
        autoCollectionCheck.Checked = settings.AutoCollectionEnabled;
        collectionExclusiveCheck.Checked = settings.CollectionRulesExclusive;
        int collectionBackfillIndex = collectionBackfillRange.Items.IndexOf(settings.CollectionBackfillRange);
        collectionBackfillRange.SelectedIndex = collectionBackfillIndex >= 0 ? collectionBackfillIndex : collectionBackfillRange.Items.IndexOf("30 days");
        LoadCollectionRulesIntoGrid();
        RefreshCollectionTab();
    }

    private void ApplyUiSettings()
    {
        settings.OsuDirectory = osuPath.Text.Trim(); settings.SongsDirectory = songsPath.Text.Trim(); settings.ReplayDirectory = replayPath.Text.Trim();
        settings.WatchReplaysAutomatically = watchCheck.Checked; settings.AnalyzeOnlyPureJumps = pureJumpCheck.Checked; settings.SessionGapMinutes = (int)sessionGap.Value;
        settings.ReplayBackfillRange = replayBackfill.SelectedItem?.ToString() ?? "30 days";
        settings.CollectionBackfillRange = collectionBackfillRange.SelectedItem?.ToString() ?? "30 days";
        ReadCollectionRulesFromGrid(false);
    }

    private void BrowseFolder(TextBox box)
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = Directory.Exists(box.Text) ? box.Text : "", ShowNewFolderButton = false };
        if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.SelectedPath;
    }

    private void SetStatus(string s) => statusLabel.Text = s;

    private static Func<PlayRow, double> ValueY(string y) => y switch
    {
        "Play aim performance" => p => p.RawAimRating, "PP estimate" => p => p.PpEstimate, "Straightness" => p => p.Straightness, "Landing" => p => p.Landing,
        "Arrival" => p => p.Arrival, "Stability" => p => p.Stability, "Deceleration" => p => p.Deceleration, "Ideal path match" => p => p.IdealPathMatch, "Aim tension" => p => p.AimTension, _ => p => p.Proficiency
    };

    private static double ValueX(PlayRow p, string x) => x switch
    {
        "Star rating" => p.StarRating, "AR" => p.EffectiveAr, "BPM" => p.MeanBpm, "Spacing" => p.SpacingP75, "Density" => p.DensityP90,
        "Aim challenge" => p.AimChallenge, "Accuracy" => p.Accuracy, "Miss count" => p.MissCount, _ => p.TimestampUtc.ToLocalTime().ToOADate()
    };

    private static Color ProficiencyColor(double score)
    {
        if (score >= TuningScorer.RemapProficiency(925, ProductionScoring.Profile)) return Theme.Good;
        if (score >= TuningScorer.RemapProficiency(850, ProductionScoring.Profile)) return Theme.Accent2;
        if (score >= TuningScorer.RemapProficiency(775, ProductionScoring.Profile)) return Theme.Warn;
        return Theme.Bad;
    }

    private static int GradeOrder(string grade) => grade switch { "SS" => 6, "S" => 5, "A" => 4, "B" => 3, "C" => 2, "D" => 1, _ => 0 };

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(x => x).ToArray(); if (a.Length == 0) return 0;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }

    private static Control Card(string title, Label value)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(0, 0, 8, 0), Padding = new Padding(12, 10, 12, 8) };
        p.Resize += (_, _) => ToolkitUi.RoundControl(p, 10);
        p.Controls.Add(value);
        p.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 20, ForeColor = Theme.Muted, Font = new Font("Segoe UI Semibold", 8f) });
        return p;
    }

    private static Label CardValue(float size = 20) => new() { Dock = DockStyle.Fill, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", size), TextAlign = ContentAlignment.MiddleLeft, Text = "—" };

    private static Control WrapPanel(Control child, string title) => ToolkitUi.Wrap(child, title);

    private static ComboBox Combo(params string[] items)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 126, Height = 28, BackColor = Theme.Panel2, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(4, 0, 12, 0) };
        c.Items.AddRange(items); c.SelectedIndex = 0; return c;
    }

    private static NumericUpDown Num(decimal min, decimal max, decimal value, int decimals = 0)
        => new() { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Width = decimals > 0 ? 62 : 58, Height = 28, BackColor = Theme.Panel2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(2, 0, 2, 0) };

    private static Label LabelFor(string text) => new() { Text = text, AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(0, 6, 2, 0), Margin = new Padding(2, 0, 2, 0) };
    private static Control Separator() => new Panel { Width = 1, Height = 28, BackColor = Theme.Border, Margin = new Padding(8, 0, 10, 0) };
    private static TextBox PathBox() => new() { Dock = DockStyle.Fill, BackColor = Theme.Panel2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
    private static CheckBox DarkCheck(string text) => new() { Text = text, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 5, 0, 5) };
    private static Button Button(string text) => ToolkitUi.Button(text);

    private static void AddPathRow(TableLayoutPanel table, int row, string label, TextBox box, Action browse)
    {
        table.Controls.Add(new Label { Text = label, ForeColor = Theme.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 6, 8, 6) }, 0, row);
        table.Controls.Add(box, 1, row);
        var b = Button("Browse…"); b.Click += (_, _) => browse(); table.Controls.Add(b, 2, row);
    }
}
