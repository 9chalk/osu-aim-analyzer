namespace OsuAimAnalyzer;

public sealed class AdvancedDiagnosticsForm : Form
{
    private readonly PlayRow play;
    private readonly AnalyzerDatabase database;
    private readonly BeatmapResolver resolver;
    private readonly AppSettings settings;
    private readonly DataGridView grid = new();
    private readonly DataGridView priorGrid = new();
    private readonly PlayPerformanceCard hero = new() { Dock = DockStyle.Fill };
    private readonly SongTimelineControl timeline = new() { Dock = DockStyle.Fill };
    private readonly AimErrorProfileControl errorProfile = new() { Dock = DockStyle.Fill };
    private readonly AimCauseProfileControl causeProfile = new() { Dock = DockStyle.Fill };
    private readonly TextBox summary = new();
    private readonly TextBox runDiagnosis = new();
    private readonly NumericUpDown errorCount = new() { Minimum = 3, Maximum = 50, Value = 10, Width = 58, BackColor = Theme.Panel2, ForeColor = Theme.Text };
    private readonly Button generate = ToolkitUi.Button("Generate top errors");
    private List<TransitionMetric> loadedTransitions = new();
    private List<PlayRow> loadedHistory = new();
    private bool diagnosisBuilt;

    public AdvancedDiagnosticsForm(PlayRow play, AnalyzerDatabase database, BeatmapResolver resolver, AppSettings settings)
    {
        this.play = play; this.database = database; this.resolver = resolver; this.settings = settings;
        Text = $"Play diagnostics · {play.Map}";
        Width = 1380; Height = 900; MinimumSize = new Size(980, 700); StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9f);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(14), BackColor = Theme.Background, AutoScroll = true };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 215));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        summary.Dock = DockStyle.Fill; summary.Multiline = true; summary.ReadOnly = true; summary.ScrollBars = ScrollBars.Vertical; summary.WordWrap = true;
        summary.BackColor = Theme.Panel; summary.ForeColor = Theme.Text; summary.BorderStyle = BorderStyle.None; summary.Font = new Font("Segoe UI", 10f);
        runDiagnosis.Dock = DockStyle.Fill; runDiagnosis.Multiline = true; runDiagnosis.ReadOnly = true; runDiagnosis.ScrollBars = ScrollBars.Vertical; runDiagnosis.WordWrap = true;
        runDiagnosis.BackColor = Theme.Panel; runDiagnosis.ForeColor = Theme.Text; runDiagnosis.BorderStyle = BorderStyle.None; runDiagnosis.Font = new Font("Segoe UI", 10f);
        root.Controls.Add(hero, 0, 0);
        root.Controls.Add(ToolkitUi.Wrap(timeline, "Song timeline · aligned local proficiency / aim performance / map difficulty"), 0, 1);
        root.Controls.Add(ToolkitUi.Wrap(causeProfile, "Why control broke · likely movement causes + repeated-pattern detection"), 0, 2);
        root.Controls.Add(ToolkitUi.Wrap(errorProfile, "Where the cursor ended · normalized hit-circle direction / centering"), 0, 3);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 8, 8, 6), WrapContents = false };
        controls.Controls.Add(new Label { Text = "Worst transitions", AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(0, 6, 4, 0) });
        controls.Controls.Add(errorCount); controls.Controls.Add(generate);
        controls.Controls.Add(new Label { Text = "Generate only when you want the cursor-path/tap visualization.", AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(10, 7, 0, 0) });
        root.Controls.Add(controls, 0, 4);

        ConfigurePriorGrid();
        ConfigureGrid();
        root.Controls.Add(BuildDetailTabs(), 0, 5);
        Controls.Add(root);

        generate.Click += async (_, _) => await GenerateErrorsAsync();
        LoadData();
    }

    private Control BuildDetailTabs()
    {
        var tabs = ToolkitUi.Tabs();
        tabs.ItemSize = new Size(135, 32);
        tabs.Padding = new Point(8, 5);

        var overview = new TabPage("Overview") { BackColor = Theme.Panel, Padding = new Padding(7) };
        overview.Controls.Add(summary);
        tabs.TabPages.Add(overview);

        var diagnosis = new TabPage("Diagnosis") { BackColor = Theme.Panel, Padding = new Padding(7) };
        diagnosis.Controls.Add(runDiagnosis);
        tabs.TabPages.Add(diagnosis);

        var objects = new TabPage("Object diagnostics") { BackColor = Theme.Panel, Padding = new Padding(7) };
        objects.Controls.Add(grid);
        tabs.TabPages.Add(objects);

        var history = new TabPage("Previous runs") { BackColor = Theme.Panel, Padding = new Padding(7) };
        history.Controls.Add(priorGrid);
        tabs.TabPages.Add(history);
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab?.Text == "Diagnosis") EnsureRunDiagnosis();
        };
        return tabs;
    }

    private void LoadData()
    {
        hero.SetPlay(play);
        TryLoadHeroBackground();
        var transitions = database.LoadTransitions(play.Id);
        loadedTransitions = transitions;
        foreach (var t in transitions)
            t.AimTension = ScoringConfig.InferredAimTension(t.Stability, t.Deceleration, t.Straightness, t.Landing, t.ErrorClass, t.Bpm, play.EffectiveAr, t.NormalizedSpacing, t.Density);
        timeline.SetData(SongTimelineBuilder.Build(play, transitions), play.Proficiency, play.RawAimRating);
        causeProfile.SetData(transitions);
        errorProfile.SetData(transitions);
        var all = database.LoadPlays();
        loadedHistory = all;
        runDiagnosis.Text = "Open this tab to compare the run with your controlled history on similar aim maps.";
        bool SameDifficulty(PlayRow p) => !string.IsNullOrWhiteSpace(play.BeatmapHash)
            ? string.Equals(p.BeatmapHash, play.BeatmapHash, StringComparison.OrdinalIgnoreCase)
            : string.Equals(p.Map, play.Map, StringComparison.OrdinalIgnoreCase);

        var previous = all.Where(p => p.Id != play.Id && SameDifficulty(p) && p.TimestampUtc < play.TimestampUtc)
            .OrderByDescending(p => p.TimestampUtc).ToList();
        var comparablePrevious = previous.Where(p => p.Mods == play.Mods).ToList();
        var priorTransitions = comparablePrevious.Count == 0 ? new List<TransitionMetric>() : database.LoadTransitions(comparablePrevious.Select(p => p.Id));
        var priorArByPlay = comparablePrevious.ToDictionary(p => p.Id, p => p.EffectiveAr);
        foreach (var t in priorTransitions)
            t.AimTension = ScoringConfig.InferredAimTension(t.Stability, t.Deceleration, t.Straightness, t.Landing, t.ErrorClass, t.Bpm, priorArByPlay.GetValueOrDefault(t.PlayId), t.NormalizedSpacing, t.Density);
        var priorByObject = priorTransitions.GroupBy(t => t.ObjectIndex)
            .ToDictionary(g => g.Key, g => (Avg: g.Average(x => x.Proficiency), Tension: g.Average(x => x.AimTension), Count: g.Count()));

        var components = new Dictionary<string,double>
        {
            ["Straightness"] = play.Straightness, ["Landing"] = play.Landing, ["Arrival"] = play.Arrival,
            ["Stability"] = play.Stability, ["Deceleration"] = play.Deceleration
        };
        var weakest = components.MinBy(x => x.Value);
        var errors = transitions.GroupBy(x => x.ErrorClass).ToDictionary(g => g.Key, g => g.Count());
        var causeSummary = AimErrorDiagnostics.Analyze(transitions);
        int total = Math.Max(1, transitions.Count);
        string P(string k) => $"{100.0 * errors.GetValueOrDefault(k) / total:0.0}%";
        var landingErrors = transitions.Where(t => double.IsFinite(t.AxialError) && double.IsFinite(t.LateralError)).ToList();
        double avgCenterError = landingErrors.Count == 0 ? 0 : landingErrors.Average(t => Math.Sqrt(t.AxialError * t.AxialError + t.LateralError * t.LateralError));
        double meanAxialBias = landingErrors.Count == 0 ? 0 : landingErrors.Average(t => t.AxialError);
        double meanLateralBias = landingErrors.Count == 0 ? 0 : landingErrors.Average(t => t.LateralError);
        string biasSummary = Math.Abs(meanAxialBias) < .015 ? "no meaningful axial bias"
            : meanAxialBias > 0 ? $"{meanAxialBias:0.00}R overaim bias" : $"{Math.Abs(meanAxialBias):0.00}R underaim bias";
        if (Math.Abs(meanLateralBias) >= .015) biasSummary += $" · {Math.Abs(meanLateralBias):0.00}R lateral bias";
        var worst = transitions.OrderBy(x => x.Proficiency).Take(5).ToList();

        string historyText;
        if (previous.Count == 0)
        {
            historyText = "No earlier analyzed runs of this exact beatmap difficulty yet, so this play is your baseline.";
        }
        else
        {
            var last = previous[0];
            double priorAvgPerf = previous.Average(p => p.RawAimRating);
            double priorAvgProf = previous.Average(p => p.Proficiency);
            double priorAvgTension = previous.Average(p => p.AimTension);
            var priorBest = previous.MaxBy(p => p.RawAimRating)!;
            historyText =
                $"Compared with {previous.Count} earlier run{(previous.Count == 1 ? "" : "s")}: aim performance is {Humanize.PlayDifference(play.RawAimRating - priorAvgPerf)} than your prior average ({priorAvgPerf:0.0}), control is {Humanize.PlayDifference(play.Proficiency - priorAvgProf)} than your prior average ({priorAvgProf:0}), and aim tension is {Humanize.TensionDifference(play.AimTension, priorAvgTension)} than your prior average ({priorAvgTension:0}/100). " +
                $"Versus your immediately previous run, aim performance changed {play.RawAimRating - last.RawAimRating:+0.0;-0.0;0.0}, proficiency {play.Proficiency - last.Proficiency:+0;-0;0}, and tension is {Humanize.TensionDifference(play.AimTension, last.AimTension)}. " +
                $"Your previous best was {priorBest.RawAimRating:0.0} aim performance at {priorBest.Proficiency:0} proficiency. " +
                $"Same-object history uses the {comparablePrevious.Count} prior run{(comparablePrevious.Count == 1 ? "" : "s")} with the same mods ({play.ModsText}) so the comparison stays mechanically fair.";
        }

        string worstText = worst.Count > 0
            ? $"Worst current transition: object #{worst[0].ObjectIndex + 1} · {worst[0].Bpm:0} BPM · {Humanize.Spacing(worst[0].NormalizedSpacing)} · control {worst[0].Proficiency:0}/1000 · {worst[0].ErrorClass} · likely cause {AimErrorDiagnostics.Diagnose(worst[0]).Cause.ToLowerInvariant()}."
            : "";
        string causeText = causeSummary.PrimaryCause == AimErrorDiagnostics.Clean
            ? "No recurring movement cause dominates this run."
            : $"Primary likely cause: {causeSummary.PrimaryCause.ToLowerInvariant()} ({causeSummary.PrimaryShare:0}% of diagnosed problem jumps). {AimErrorDiagnostics.CauseDescription(causeSummary.PrimaryCause)}";
        string streakText = causeSummary.LongestStreak is { Count: >= 3 } streak
            ? $" Repeated {streak.Cause.ToLowerInvariant()} ×{streak.Count} across objects {streak.StartObject}–{streak.EndObject}: {AimErrorDiagnostics.RepeatedMeaning(streak.Cause)}"
            : "";

        summary.Text =
            $"{play.Map}\r\n{play.TimestampUtc.ToLocalTime():g} · {play.ModsText} · {play.Grade} · {play.Accuracy:0.00}% · {play.MissCount} miss\r\n" +
            $"Overall control: {Humanize.ProductionScore(play.Proficiency)} · play aim performance: {play.RawAimRating:0.0} · {play.Zone} ({Humanize.ZoneExplanation(play.Zone)}) · {play.StarRating:0.00}★ · {Humanize.Ar(play.EffectiveAr)}\r\n" +
            $"Typical map demand: {play.MeanBpm:0} BPM · P75 spacing {Humanize.Spacing(play.SpacingP75)} · P90 density {Humanize.Density(play.DensityP90)} · {play.TransitionCount:N0} analyzed jumps\r\n\r\n" +
            $"Main mechanical limiter: {DiagnosticsEngine.HumanMechanic(weakest.Key)} at {Humanize.Score(weakest.Value)}. " +
            $"Path {play.Straightness:0}, landing {play.Landing:0}, arrival {play.Arrival:0}, stability {play.Stability:0}, braking {play.Deceleration:0}, ideal-path match {play.IdealPathMatch:0}. Inferred aim tension: {Humanize.Tension(play.AimTension)}.\r\n" +
            $"Average center error: {avgCenterError:0.00}R · mean bias: {biasSummary}.\r\n" +
            $"WHY CONTROL BROKE: {causeText}{streakText}\r\n" +
            $"WHERE IT ENDED: overaim {P("Overaim")} · underaim {P("Underaim")} · lateral {P("Lateral")} · correction {P("Correction")} · plain error {P("Plain error")}.\r\n\r\n" +
            $"HISTORY\r\n{historyText}\r\n\r\n{worstText}";

        priorGrid.Rows.Clear();
        foreach (var p in previous.Take(50))
        {
            double dPerf = play.RawAimRating - p.RawAimRating;
            double dProf = play.Proficiency - p.Proficiency;
            priorGrid.Rows.Add(p.TimestampUtc.ToLocalTime().ToString("g"), p.ModsText, p.Grade, p.Accuracy.ToString("0.00") + "%", p.MissCount,
                Humanize.ProductionScoreShort(p.Proficiency), p.RawAimRating.ToString("0.0"), Humanize.TensionShort(p.AimTension),
                dPerf.ToString("+0.0;-0.0;0.0"), dProf.ToString("+0;-0;0"), Humanize.TensionDifference(play.AimTension, p.AimTension));
        }

        grid.Rows.Clear();
        foreach (var x in transitions.OrderBy(x => x.Proficiency))
        {
            string priorAvg = "—", delta = "—", priorTension = "—", tensionDelta = "—", n = "—";
            if (priorByObject.TryGetValue(x.ObjectIndex, out var hist))
            {
                priorAvg = hist.Avg.ToString("0");
                delta = (x.Proficiency - hist.Avg).ToString("+0;-0;0");
                priorTension = Humanize.TensionShort(hist.Tension);
                tensionDelta = Humanize.TensionDifference(x.AimTension, hist.Tension);
                n = hist.Count.ToString();
            }
            var diagnosis = AimErrorDiagnostics.Diagnose(x);
            int rowIndex = grid.Rows.Add(x.ObjectIndex + 1, x.TimeMs, x.Bpm.ToString("0"), Humanize.Spacing(x.NormalizedSpacing, true), x.Density.ToString("0.0"), x.Angle.ToString("0"),
                Humanize.ScoreShort(x.Proficiency), Humanize.ScoreShort(ScoringConfig.EffectiveIdealPathMatch(x)), Humanize.TensionShort(x.AimTension), priorAvg, delta, priorTension, tensionDelta, n, x.Straightness.ToString("0"), x.Landing.ToString("0"), x.Arrival.ToString("0"), x.Stability.ToString("0"), x.Deceleration.ToString("0"), diagnosis.Cause, $"{diagnosis.Confidence * 100:0}%", x.ErrorClass);
            grid.Rows[rowIndex].Cells[19].ToolTipText = diagnosis.Explanation;
            grid.Rows[rowIndex].Cells[20].ToolTipText = diagnosis.Explanation;
        }
    }

    private void EnsureRunDiagnosis()
    {
        if (diagnosisBuilt) return;
        diagnosisBuilt = true;
        try
        {
            runDiagnosis.Text = "Analyzing this run against your similar-map history…";
            runDiagnosis.Text = AimTrainingDiagnosisEngine.BuildRunDiagnosis(play, loadedTransitions, database, loadedHistory);
            runDiagnosis.SelectionStart = 0;
            runDiagnosis.ScrollToCaret();
        }
        catch (Exception ex)
        {
            runDiagnosis.Text = "Could not build objective run diagnosis: " + ex.Message;
        }
    }

    private void TryLoadHeroBackground()
    {
        try
        {
            BeatmapData? map = null;
            if (!string.IsNullOrWhiteSpace(play.BeatmapPath) && File.Exists(play.BeatmapPath))
                map = BeatmapParser.Parse(play.BeatmapPath);
            map ??= resolver.Resolve(play.BeatmapHash);
            string? bg = map?.BackgroundPath;
            if (string.IsNullOrWhiteSpace(bg) || !File.Exists(bg)) return;
            using var stream = new FileStream(bg, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var source = Image.FromStream(stream);
            hero.SetBackgroundImage(new Bitmap(source));
        }
        catch { }
    }

    private async Task GenerateErrorsAsync()
    {
        generate.Enabled = false; generate.Text = "Generating…";
        try
        {
            var samples = await ErrorVisualBuilder.GenerateAsync(play, database, resolver, settings, (int)errorCount.Value);
            if (samples.Count == 0)
            {
                MessageBox.Show(this, "No visual samples could be reconstructed. The original replay or exact beatmap version may no longer exist locally.", "Top errors", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var form = new ErrorVisualizerForm(play, samples);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not generate top errors", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { generate.Enabled = true; generate.Text = "Generate top errors"; }
    }

    private void ConfigurePriorGrid()
    {
        StyleGrid(priorGrid);
        priorGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        (string name, int width)[] cols =
        {
            ("When",125),("Mods",60),("Grade",55),("Accuracy",75),("Misses",60),("Control / tier",125),("Aim performance",95),("Aim tension",85),
            ("Current vs this · aim",115),("Current vs this · control",125),("Current vs this · tension",145)
        };
        foreach (var c in cols) { int i = priorGrid.Columns.Add(c.name, c.name); priorGrid.Columns[i].Width = c.width; }
    }

    private void ConfigureGrid()
    {
        StyleGrid(grid);
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        (string name, int width)[] cols =
        {
            ("Object",65),("Time ms",75),("BPM",60),("Spacing · circles",130),("Visible objs",80),("Angle",60),("Control",115),("Ideal path",110),("Aim tension",85),
            ("Prior object avg",95),("Δ vs prior",75),("Prior tension",85),("Δ tension",120),("Prior samples",80),("Path",65),("Landing",70),("Arrival",70),("Stability",75),("Braking",70),("Likely cause",125),("Cause conf.",80),("Direction",90)
        };
        foreach (var c in cols) { int i = grid.Columns.Add(c.name, c.name); grid.Columns[i].Width = c.width; }
    }

    private static void StyleGrid(DataGridView g) => ToolkitUi.StyleDataGrid(g);

}

public static class ErrorVisualBuilder
{
    public static async Task<List<ErrorVisualSample>> GenerateAsync(PlayRow play, AnalyzerDatabase db, BeatmapResolver resolver, AppSettings settings, int count)
    {
        string replayPath = FindReplayPath(play, settings);
        if (string.IsNullOrWhiteSpace(replayPath) || !File.Exists(replayPath))
            throw new FileNotFoundException("The original .osr could not be found. Old plays imported before replay-path tracking may need to be re-imported manually.");

        ReplayData replay = ReplayReader.Read(replayPath);
        BeatmapData? map = null;
        if (!string.IsNullOrWhiteSpace(play.BeatmapPath) && File.Exists(play.BeatmapPath))
            map = BeatmapParser.Parse(play.BeatmapPath);
        map ??= resolver.Resolve(play.BeatmapHash);
        map ??= await resolver.ResolveFromSongsAsync(play.BeatmapHash);
        if (map == null) throw new FileNotFoundException("The exact .osu version referenced by this replay could not be found.");

        var worst = db.LoadTransitions(play.Id).OrderBy(t => t.Proficiency).Take(count).ToList();
        var sampler = new CursorSampler(replay.Frames);
        var output = new List<ErrorVisualSample>();
        foreach (var metric in worst)
        {
            int idx = metric.ObjectIndex;
            if (idx <= 0 || idx >= map.HitObjects.Count) continue;
            var from = Transform(map.HitObjects[idx - 1], replay.Mods);
            var to = Transform(map.HitObjects[idx], replay.Mods);
            double rate = ModUtils.ClockRate(replay.Mods);
            var path = sampler.Sample(from.TimeMs - 12 * rate, to.TimeMs + 45 * rate, Math.Max(1, 2 * rate));
            var tap = FindTap(replay.Frames, to.TimeMs, rate);
            output.Add(new ErrorVisualSample
            {
                Metric = metric, From = from, To = to, Path = path,
                TapPoint = tap?.Point ?? sampler.At(to.TimeMs), TapOffsetMs = tap?.OffsetMs ?? 0, HasTap = tap != null,
                CircleRadius = AimAnalyzer.CircleRadius(ModUtils.ApplyDifficultyMods(map.CS, replay.Mods))
            });
        }
        return output;
    }

    private static string FindReplayPath(PlayRow play, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(play.ReplayPath) && File.Exists(play.ReplayPath)) return play.ReplayPath;
        if (!Directory.Exists(settings.ReplayDirectory)) return "";
        foreach (var f in Directory.EnumerateFiles(settings.ReplayDirectory, "*", SearchOption.AllDirectories))
        {
            try
            {
                var h = ReplayReader.ReadHeader(f);
                if ((!string.IsNullOrWhiteSpace(play.ReplayHash) && string.Equals(h.ReplayHash, play.ReplayHash, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(h.BeatmapHash, play.BeatmapHash, StringComparison.OrdinalIgnoreCase) && Math.Abs((h.TimestampUtc - play.TimestampUtc).TotalSeconds) < 2))
                    return f;
            }
            catch { }
        }
        return "";
    }

    private static HitObjectData Transform(HitObjectData o, int mods) => new()
    {
        Index = o.Index, X = o.X, Y = (mods & ModUtils.HardRock) != 0 ? 384 - o.Y : o.Y, TimeMs = o.TimeMs, Kind = o.Kind
    };

    private sealed record TapInfo(CursorPoint Point, double OffsetMs);
    private static TapInfo? FindTap(List<ReplayFrame> frames, long targetTime, double rate)
    {
        ReplayFrame? best = null;
        double bestAbs = double.MaxValue;
        bool prevDown = false;
        foreach (var f in frames)
        {
            bool down = (f.Keys & 15) != 0;
            if (down && !prevDown)
            {
                double delta = (f.TimeMs - targetTime) / rate;
                double ad = Math.Abs(delta);
                if (ad <= 120 && ad < bestAbs) { best = f; bestAbs = ad; }
            }
            prevDown = down;
        }
        return best is { } b ? new TapInfo(new CursorPoint(b.TimeMs, b.X, b.Y), (b.TimeMs - targetTime) / rate) : null;
    }
}

public sealed class ErrorVisualizerForm : Form
{
    private readonly PlayRow play;
    private readonly List<ErrorVisualSample> samples;
    private readonly ErrorCanvas canvas = new() { Dock = DockStyle.Fill };
    private readonly ListBox list = new() { Dock = DockStyle.Fill };
    private readonly Label details = new() { Dock = DockStyle.Fill, ForeColor = Theme.Text, Font = new Font("Segoe UI", 10f), Padding = new Padding(12), AutoEllipsis = true };

    public ErrorVisualizerForm(PlayRow play, List<ErrorVisualSample> samples)
    {
        this.play = play; this.samples = samples;
        Text = $"Top aim errors · {play.Map}"; Width = 1180; Height = 760; MinimumSize = new Size(900, 600); StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 9f);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(14), BackColor = Theme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        list.BackColor = Theme.Panel; list.ForeColor = Theme.Text; list.BorderStyle = BorderStyle.None;
        for (int i = 0; i < samples.Count; i++)
        {
            var d = AimErrorDiagnostics.Diagnose(samples[i].Metric);
            list.Items.Add($"#{i + 1} · obj {samples[i].Metric.ObjectIndex + 1} · {d.Cause} · {samples[i].Metric.ErrorClass} · {samples[i].Metric.Proficiency:0}");
        }
        list.SelectedIndexChanged += (_, _) => SelectSample();
        root.Controls.Add(ToolkitUi.Wrap(list, "Worst transitions"), 0, 0); root.SetRowSpan(root.GetControlFromPosition(0,0)!, 2);
        root.Controls.Add(ToolkitUi.Wrap(canvas, "Cursor path / target geometry"), 1, 0);
        root.Controls.Add(details, 1, 1);
        Controls.Add(root);
        if (samples.Count > 0) list.SelectedIndex = 0;
    }

    private void SelectSample()
    {
        int i = list.SelectedIndex; if (i < 0 || i >= samples.Count) return;
        var s = samples[i]; canvas.Sample = s; canvas.Invalidate();
        var diagnosis = AimErrorDiagnostics.Diagnose(s.Metric);
        details.Text = $"Object #{s.Metric.ObjectIndex + 1} · {s.Metric.Bpm:0} BPM · {Humanize.Spacing(s.Metric.NormalizedSpacing)} · {Humanize.Density(s.Metric.Density)} · angle {s.Metric.Angle:0}°\n" +
                       $"Likely cause: {diagnosis.Cause} ({diagnosis.Confidence * 100:0}% confidence) · {diagnosis.Explanation}\n" +
                       $"Proficiency {Humanize.ScoreShort(s.Metric.Proficiency)} · ideal path {Humanize.ScoreShort(ScoringConfig.EffectiveIdealPathMatch(s.Metric))} · straight {s.Metric.Straightness:0} · landing {s.Metric.Landing:0} · arrival {s.Metric.Arrival:0} · stability {s.Metric.Stability:0} · decel {s.Metric.Deceleration:0} · tension {Humanize.TensionShort(s.Metric.AimTension)} · direction {s.Metric.ErrorClass}" +
                       (s.HasTap ? $" · tap {s.TapOffsetMs:+0.0;-0.0;0.0} ms" : " · tap point unavailable");
    }
}

public sealed class ErrorCanvas : Control
{
    public ErrorVisualSample? Sample { get; set; }
    public ErrorCanvas() { DoubleBuffered = true; BackColor = Color.FromArgb(15, 17, 22); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var s = Sample; if (s == null) return;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var area = ClientRectangle; float margin = 24;
        float scale = Math.Min((area.Width - 2 * margin) / 512f, (area.Height - 2 * margin) / 384f);
        float ox = (area.Width - 512 * scale) / 2f, oy = (area.Height - 384 * scale) / 2f;
        PointF P(double x, double y) => new(ox + (float)x * scale, oy + (float)y * scale);
        e.Graphics.DrawRectangle(new Pen(Theme.Border, 1), ox, oy, 512 * scale, 384 * scale);
        float r = (float)s.CircleRadius * scale;
        var a = P(s.From.X, s.From.Y); var b = P(s.To.X, s.To.Y);
        using var fromPen = new Pen(Theme.Muted, 2); using var targetPen = new Pen(Theme.Accent, 3);
        e.Graphics.DrawEllipse(fromPen, a.X-r, a.Y-r, 2*r, 2*r); e.Graphics.DrawEllipse(targetPen, b.X-r, b.Y-r, 2*r, 2*r);
        if (s.Path.Count > 1)
        {
            using var pathPen = new Pen(Theme.Accent2, 2.2f);
            var pts = s.Path.Select(x => P(x.X, x.Y)).ToArray(); if (pts.Length > 1) e.Graphics.DrawLines(pathPen, pts);
        }
        if (s.HasTap)
        {
            var t = P(s.TapPoint.X, s.TapPoint.Y); using var tapPen = new Pen(Theme.Warn, 3);
            e.Graphics.DrawLine(tapPen, t.X-7, t.Y, t.X+7, t.Y); e.Graphics.DrawLine(tapPen, t.X, t.Y-7, t.X, t.Y+7);
        }
        using var font = new Font("Segoe UI Semibold", 9f);
        e.Graphics.DrawString("FROM", font, Brushes.White, a.X + r + 3, a.Y - 8);
        e.Graphics.DrawString("TARGET", font, Brushes.White, b.X + r + 3, b.Y - 8);
    }
}
