# osu! Aim Analyzer v32

v32 hardens every WinForms split container against transient pre-layout sizes. The v31 Insights fix deferred the divider, but the Insights container still declared large `Panel1MinSize` / `Panel2MinSize` values during construction; WinForms can validate those against a tiny initial width and throw the same `SplitterDistance` exception before the divider helper runs. v32 keeps construction-time minimums safe, centralizes clamped splitter assignment in `ToolkitUi.SetSplitterDistanceSafe`, and routes Replay Tools splitter resize handlers through the same guard.

# osu! Aim Analyzer v31

Windows/.NET 8 replay analyzer for osu!standard / osu!stable. It watches or imports local `.osr` replays, resolves their `.osu` difficulty, reconstructs cursor movement between hitobjects, stores transition-level telemetry in SQLite, and turns that history into aim diagnostics and training recommendations.


## v31 — startup splitter hotfix

v31 fixes a WinForms startup crash introduced by the Insights layout in v30. The Insights split container no longer assigns `SplitterDistance` while its client width is still at the designer/default size. Its initial divider is now applied lazily after layout has produced a legal client size and is clamped against `Panel1MinSize`, `Panel2MinSize`, and `SplitterWidth`. This prevents `SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize` on launch or unusually small host sizes. No analysis, scoring, diagnosis, or database behavior changed.




## v29 — objective diagnosis + smoother diagnostics UI

**Aim analysis → Error conditions** now turns the existing correlations into an evidence-backed training diagnosis. The analyzer reuses its existing aim-type presets, ranks them by the player’s own matching run/transition volume, and lets the player click through each category for the selected movement cause or landing class. Probable contributors are calculated from the same stored star/BPM/AR/spacing/density/local-demand telemetry and compared against the player’s **controlled history on similar maps**. Each factor shows an evidence-confidence score, the observed value, the player’s controlled range, the measured error-rate lift, and a concrete adjustment. Confidence is explicitly an evidence score from effect strength + controlled-range separation + sample support, not a claim of literal causality. The page also gives separate **main-volume**, **limit-test**, and **mastery-target** guidance from the production proficiency thresholds and the player’s own history.

That same model is now available per run. Recent Play has a lazy-loaded **Diagnosis** page, and the double-click diagnostics window has a **Diagnosis** subtab comparing just that run with similar historical maps. The detailed window’s top is modernized with the same performance hero used by Recent Play; the old text summary moves into an Overview subtab.

All normal time-series charts and song timelines now use presentation-only smoothing with a faint bloom and brighter inner stroke. Exact hover/readout values remain unchanged, while raw play points use a separate muted color so the trend line and observations are visually distinct. No scoring constants or database schema changed. See `V29_NOTES.md`.


## v28 — recent-play cleanup + faster analysis navigation

v28 puts the exact-difficulty **Runs** table back into the normal Recent Play Overview, directly under the landing-error visual, while retaining the vertical rail for alternate readouts and tools. The separate Runs rail page is removed, the rail is narrower, Follow latest is moved up with navigation, and Top-error count now belongs to the Top errors page beside Generate / refresh. This removes the detached bottom-left footer visible in v27.

The compact song timeline now renders a single readable min–max range when there is not enough lane height for separate endpoint labels, and the compact landing-error view tightens its stat/bar spacing so lower error rows do not clip.

**Aim analysis → Error conditions** now opens with a stable 210 px error selector rather than an initially-hidden splitter that could collapse to its minimum width. Navigation is also lighter: Error Conditions reuses cached lifetime telemetry, selected condition results are cached for the active history window, and the six raw Advanced-breakdown graphs are only generated when that breakdown is actually opened. Changing play-by-play/daily grouping redraws only those advanced graphs. No scoring constants or database schema changed. See `V28_NOTES.md`.


## v27 — error-condition profiling + inspector navigation

v27 turns the trajectory diagnostics into a second layer of historical analysis. **Aim analysis → Error conditions** lets you click either an inferred movement cause (Shake-off, Late acquisition, Stopped short, Braking overshoot, Correction loop, Curved approach, Lateral drift, Unstable braking, General imprecision) **or** a landing class (Overaim, Underaim, Lateral, Correction, Plain error, Clean) and inspect the map conditions where it appears most often. The view compares **star rating, BPM, effective AR, CS4-normalized spacing, visible-object density, jump angle, and local aim demand** against the player's own all-jump baseline. Each metric reports the most-associated range, the error rate inside that range, the player's overall rate, and a relative-tendency multiplier; a map table then shows the exact difficulty/mod combinations contributing the most examples. This is association analysis, not a claim of causation.

The Dashboard recent-play inspector is also reorganized around a **vertical navigation rail**. The hero card stays visible while Overview, Summary, Runs, Training, Compare, Errors, Top errors, and Advanced each get the entire remaining inspector body. Runs now shows up to roughly two dozen nearby attempts instead of being squeezed into a short bottom strip. Top-error cursor reconstruction and Advanced diagnostics can render directly inside the recent-play rail rather than always opening over the dashboard. The Overview itself is shorter and cleaner: the timeline, cause panel, and landing-error profile no longer compete with the runs table for height.

The v26 cause-profile overlap shown at short control heights is fixed by reserving a dedicated footer area before drawing the cause bars. No scoring constants or database schema changed. Existing replay history is sufficient for the new condition analysis. See `V27_NOTES.md`.


## v26 — trajectory-cause diagnostics

v26 adds a second diagnostic layer on top of the existing landing-direction labels. **Where the cursor ended** (overaim / underaim / lateral / correction / plain error) remains unchanged, but the analyzer now also estimates **why control broke** from the trajectory-derived mechanics already stored for every transition.

The new likely-cause model combines Landing / centering, Arrival timing, Stability / shake, Braking / deceleration, Straightness, Ideal-path match, signed axial/lateral center error, and the existing correction classifier. It recognizes patterns such as **Shake-off**, **Braking overshoot**, **Stopped short**, **Late acquisition**, **Correction loop**, **Curved approach**, **Lateral drift**, **Unstable braking**, and **General imprecision**. These are explicitly presented as inferred movement diagnoses, not new score components. Production proficiency and Aim Performance are unchanged.

A useful special case is **Shake-off**: relatively good target acquisition / arrival combined with weak final stability and a degraded landing. That lets the app distinguish “I never got there” from “I got onto the circle, then jittered/drifted off before the hit.” The summary also detects repeated same-cause streaks across consecutive objects and explains what a section-wide pattern usually implies.

The Dashboard recent-play rail and double-click diagnostics window now include a **Why control broke** visualization beside the existing song timeline and landing map. Object diagnostics add **Likely cause**, confidence, and explanation tooltips, and the reconstructed Top Errors view shows the inferred cause for each cursor path.

Aim Analysis now includes a lifetime cause profile and changes the top summary card from dominant landing direction to **Dominant cause**. The Advanced breakdown retains landing-direction history and adds a separate **movement-cause history** graph so direction and cause never get conflated. Historical plays work immediately because v26 derives the cause layer from metrics already stored in the database; no schema migration or replay re-import is required. See `V26_NOTES.md`.


## v25 — readable aim profile

**Aim analysis** now opens on an at-a-glance visual profile instead of raw timelines. The primary screen combines a spider-style **Aim fingerprint**, the real lifetime landing-error cloud, and visual mechanic cards for Centering, Arrival timing, Straightness, Stability / shake, Braking, Path efficiency, and **Raw aim skill**. Each card compares the selected history window with the most recent 20 plays and explains the mechanic in normal player language.

Raw aim skill uses the existing top-100 unique-map Aim Rating so it represents demonstrated difficulty-adjusted capability rather than an average of easy plays. The radar uses the same production component data and adds Raw aim as its capability axis.

All v24 time-series graphs are preserved behind **Advanced breakdown**: Aim Performance, proficiency, center error, error mix, mechanic timelines, and the exact lifetime-vs-recent metric table. Timeline grouping moved into that advanced section because it only affects those graphs. No scoring constants or database schema changed. See `V25_NOTES.md`.


## v24 — lifetime aim analysis + leaner navigation

The top-level **Diagnostics** and **Training** tabs are hidden for now to reduce navigation bloat. Their engines and builders remain in the source; no underlying analysis code was deleted. The visible top-level workflow is now Dashboard / Aim analysis / Replay tools / Collections / Settings.

**Aim analysis** has been rebuilt as a single scrollable lifetime-analysis workspace using the same dark performance-card visual language as the v23 selected-play panel. It now includes:

- **Aim Performance over time**;
- **Proficiency over time**;
- **center error over time** in hit-circle-radius units (`R`), with mean / median / P90 curves;
- **error-direction history** for Overaim / Underaim / Lateral / Correction / Clean;
- a combined timeline for every production proficiency component: Landing / centering, Arrival timing, Straightness, Braking / deceleration, Stability / shake control, and Ideal-path match;
- a mechanics rundown showing the production base weight, lifetime average, latest-20-play average, and change for each component;
- summary cards for play count, average proficiency, average Aim Performance, average center error, and dominant directional error.

The History selector can still narrow the view, while the Timeline selector switches between play-by-play and daily-average trends. These are analysis-only views and do not alter production scoring.

## v23 — build hotfix + landing-error profile

v23 fixes the v22 `SongTimelineControl` build failure caused by mixing a `List<SongTimelinePoint>` with `SongTimelinePoint[]` in a null-coalescing expression. The timeline data assignment now has one explicit `IReadOnlyList` path and builds cleanly under normal C# type rules.

The recent/selected-play rail and double-click diagnostics window now also include a **Landing error profile**. Every analyzed jump is rotated into the same incoming-jump coordinate frame and plotted around a normalized hit circle:

- left = **underaim**;
- right = **overaim**;
- vertical displacement = **lateral error**;
- each dot is one transition's stored center error;
- the pink arrow is the play's signed mean bias;
- the dotted ring is average absolute distance from target center;
- the side panel reports average / median / P90 center error in hit-circle radii and the Overaim / Underaim / Lateral / Correction / Plain error / Clean distribution.

This uses the analyzer's already-stored `AxialError`, `LateralError`, and `ErrorClass` telemetry, so it does **not** require replay reconstruction and does not change scoring. The dashboard's right rail also gets a little more default width, and the full diagnostics window is larger to fit the timeline plus error profile.

## v22 — per-song aim timeline + performance-style recent play card

The selected-play inspector and the double-click diagnostics window now include a synchronized three-lane **song timeline**:

- **Proficiency** — rolling local production proficiency using the same tuned transition scores, difficulty weights, aggregation, and final resolution curve as the main analyzer.
- **Aim performance** — rolling local demonstrated aim capability. Local geometry/execution determines the shape; the whole-play SR anchor/consistency calibration keeps the line in the same numerical neighborhood as the displayed play Aim Performance.
- **Map difficulty** — the production performance model's local telemetry demand curve (velocity, spacing, angle and visible-object density), shown as a multiplier where `1.00×` is the model reference demand. This is intentionally a local aim-demand diagnostic, not an official per-section osu! star rating.

All three lanes share the same song-time axis and hover crosshair, so a difficulty spike can be compared directly with the player's local proficiency/performance response. Long breaks are drawn as gaps instead of misleading connecting lines. Subtle horizontal references show the whole-play proficiency/performance while the proficiency lane also retains production zone references.

The Dashboard gives the recent/selected-play inspector substantially more default space. Its header has also been rebuilt around an osu!-style performance-card hierarchy: beatmap background banner, map/difficulty metadata, star/mod badges, large grade + proficiency, and compact accuracy/miss/Aim Performance stats. Run history and the existing Overview / Training / Comparison / Errors readouts now live in compact sub-tabs below the graphs.

The detailed double-click diagnostics view gets the same aligned song timeline and moves Object diagnostics / Previous runs into tabs so the graphs have useful vertical room without deleting any existing diagnostics. See `V22_NOTES.md`.

## v21 — production tuner scoring + Replay Tools

The standalone analyzer now ships the finalized **Aim Tuner v9** scoring as its fixed production model. Dashboard/history scoring uses the tuned difficulty-aware proficiency aggregation, final proficiency resolution curve, sustained hard-section Aim Performance model, duration-aware consistency bonus, and SR demand floor. The scoring controls are intentionally **not** exposed in the production app.

A new **Replay tools** workspace adds three tuner workflows:

- **Replay test** — multi-replay import/cache, local PC replay browser, legacy-vs-current comparison, and per-transition inspection.
- **Quick checker** — stress-test a highlighted or manually selected difficulty over simulated raw-proficiency and miss-count ranges, then save synthetic cases beside real replay tests.
- **Debug visualizer** — inspect a selected jump's player path vs ideal minimum-jerk path and see raw/adjusted scoring inputs.

Quick Checker supports optional osu!stable song-select detection via `setup-song-select-reader.bat`; manual `.osu` selection works without it. Existing SQLite history is rescored from stored transition telemetry when v21 first starts. See `V21_NOTES.md` for the exact migration/scoring details.

## Dashboard layout

The Dashboard uses draggable split panes:

- drag the horizontal divider under **Trend** to trade space between the long-term trend and the play table/recent-play inspector;
- drag the vertical divider between **Analyzed plays** and the performance-style selected-play inspector to change their widths.

The selected-play area remains a full-height right rail and v27 gives it a little more default width. Below the fixed performance hero, a narrow vertical navigation rail gives Overview, Summary, Training, Compare, Errors, Top errors and Advanced alternate full-body pages. The normal Overview keeps the synchronized timeline, cause analysis, landing-error profile, and the exact-difficulty Runs table together so attempt comparison remains visible without leaving the main post-play view.

## Build

1. Install the **.NET 8 SDK**.
2. Extract the project.
3. Run `build-release.bat`.
4. The self-contained executable is written under `bin\Release\net8.0-windows\win-x64\publish\`.

## Main workspaces

### Dashboard

- Session / day / long-term graphing.
- Filter by grade, miss count, BPM, spacing, stars, density and time range.
- Plot proficiency, play aim performance, Aim Rating, PP estimate, straightness, landing, arrival, stability, braking/deceleration and inferred aim tension.
- Optional multi-line overlays for proficiency, aim performance, PP estimate and tension.
- Aim Rating is the **average of the best play on each unique difficulty, up to the top 100**. Easy plays cannot lower it and repeated farming of one exact difficulty does not fill the rating.
- Double-click any play for advanced diagnostics and previous-run comparison.
- A persistent **Latest / selected play** inspector fills the dashboard's right-hand play-list space. It can follow the newest analyzed play automatically or pin any row you click.
- The inspector uses an osu!-inspired performance card and shows synchronized local proficiency / Aim Performance / map-difficulty curves, plus training suitability, comparison with earlier runs, dominant error types, inferred tension, and on-demand top-error cursor/tap reconstruction.

### Diagnostics / Training engines (hidden)

The former top-level Diagnostics and Training pages are not shown, but their source and engines are retained for future reuse. Existing skill presets include:

- NM farm · jump aim
- DT farm · jump aim
- High-AR speed aim
- Wide low-BPM jump aim
- Dense farm / reading aim
- Fast compact / medium aim
- Cross-screen jump aim
- HR precision jump aim

The cohort table prominently includes star rating, effective AR, BPM, spacing, visible-object density, control subscores, inferred tension and the main limiter.

The selected cohort is split into four readable sub-tabs:

- **Overview**
- **Mechanics**
- **Demand / context**
- **What to do**

Training functionality retained in source:

- Uses the same skill presets as Diagnostics.
- Recommendation leniency control from strict to broad.
- A prominent list of maps from **your own analyzed history** that fit the selected skill preset.
- Recommended training bands for stars, AR, BPM, spacing and visible-object density.
- Plain-English map-picking guideline for each skill preset.
- Session quota view showing how much current-session volume landed in Mastered / Controlled / Challenging / Breakdown zones.

## Replay-derived mechanics

Proficiency weights are mildly context-aware: very fast aim places a little less weight on post-arrival stability and a little more on timing/centering, while wide aim shifts a small amount of weight toward landing/braking. Existing history is automatically rescored from stored transition components when the scoring model version changes.

Every aim transition can track:

- path straightness / curvature
- landing / centering
- arrival timing
- post-arrival stability / shake
- braking / deceleration quality
- overaim / underaim / lateral / corrective errors
- CS4-normalized spacing
- BPM and movement interval
- visible-object density
- effective AR
- inferred aim tension

### Aim tension

Aim tension is **inferred from cursor behavior**, not directly measured muscle tension. The model is intentionally stricter for fast/high-AR aim. Visible post-arrival shake, corrective reversals, abrupt braking and path jitter raise the score, and similar errors are penalized more strongly around high BPM / AR10+ where control margin is smaller.

The 0–100 labels are currently:

- `<14` relaxed
- `14–26` normal
- `27–41` elevated
- `42–57` tense
- `58+` over-tense

These thresholds and weights live in `ScoringConfig.cs` / `Humanize.cs` and are deliberately easy to tune against real replays.

## Spacing reference

Spacing is normalized to CS4 to make maps more comparable. Approximate references:

- ~73 px = 1 CS4 circle diameter
- ~146 px = 2 circle diameters
- ~219 px = 3 circle diameters
- ~292 px = 4 circle diameters

The UI generally shows both pixels and circle diameters so recommendations are easier to visualize.

## PP note

The current **PP estimate** is a local trend/reference estimate derived from modded star rating, accuracy, misses and common mods. It is intentionally labelled `PP est.` and is **not an exact current official osu! performance calculation**. `PpUtils.cs` isolates this calculation so it can later be replaced by the current official `ppy/osu` performance calculator or another compatible implementation without changing the dashboard/database design.

## Local-map / merger support

Replay-to-map resolution tries `osu!.db` first, refreshes it, then hashes recent `.osu` files in `Songs`, and finally builds a cached full Songs hash index. This is intended to pick up newly generated maps from the toolkit merger/trainer even before stable has fully refreshed its own database.

## Data storage

Analysis is stored in SQLite. History scans read replay headers first and skip previously imported replays before decompressing cursor data.

## v10 dashboard run history

The Latest / selected play inspector now includes a compact run-history table for the exact beatmap difficulty. It shows the selected/current run alongside recent attempts with accuracy, misses, proficiency, aim performance and inferred tension. `Δ Prof.` and `Δ Aim` compare each attempt with the immediately previous attempt; positive deltas are green and negative deltas are red.

## v12 proficiency tuning

- Proficiency descriptions now have much finer bands from 700-1000, where most training plays live.
- Smooth pathing, braking quality, and post-arrival stability carry more weight in proficiency.
- High-BPM aim still receives a small settling-time normalization, but shake is no longer discounted as aggressively.
- Underaim that remains inside the circle is penalized less, especially when the cursor is closer to center than edge. Outside-circle / miss-like underaim remains harsh.
- Existing stored transitions are automatically rescored through scoring version 4; replay re-import is not required for the weighting/underaim changes.

## v13 dashboard refinements

- Dashboard proficiency card/table now show the fine-grained 700–1000 proficiency tier labels directly.
- Latest/selected play insights are split into Overview, Training, Comparison, and Errors tabs.
- Dashboard opens with a smaller default trend pane so the play/inspector workspace is more visible.
- Replay watcher stability polling is faster, and the selected-play inspector is updated before the heavier dashboard redraw.

## v14 proficiency model notes

- **Ideal-path match:** each analyzed jump now builds a synthetic center-to-center, minimum-jerk computer trajectory over the player's actual movement window and compares the replay cursor to it. This is a modest component of proficiency, not the whole score. Old database rows receive a conservative proxy until the replay is analyzed again.
- **Large-circle centering normalization:** low-CS / large-circle maps no longer get the full benefit of their larger absolute hit radius when calculating proficiency. Hit geometry is unchanged; only the proficiency centering leniency is tightened. Small-circle maps are not made easier.
- **Dashboard proficiency tiers:** the main play list, average card, recent-run history and detailed summaries all use the same fine-grained `Humanize.ProficiencyTier` mapping. The separate `Training Zone` label remains Mastered / Controlled / Challenging / Breakdown because it describes training use, not the proficiency tier.

## v17 aim-analysis radar

The Aim Analysis radar is now an **uncapped capability profile**, not another copy of the 0–1000 proficiency score. Each axis is calculated from the actual replay-transition subsets relevant to that skill (BPM, spacing, interval, angle, AR, velocity, map difficulty, stability, landing, braking, ideal-path match and inferred tension). The analyzer computes a sustained per-play score, then uses the best several qualifying plays rather than averaging every easy object in the library.

`1000` is a visible reference ring, not a maximum. A stronger player can score above 1000 on any axis by maintaining comparable execution quality at materially greater mechanical demand. The radar dynamically expands its scale when an axis exceeds 1000. The aspect table includes the replay evidence and demand multiplier behind each axis so the score is auditable rather than a cosmetic personality chart.


## v18 responsiveness / layout pass

- Dashboard Miss/BPM/Spacing/★/Density ranges moved into an **Advanced filters** drop-down.
- Replay watcher now begins parsing after a short ~35 ms stability cadence and retries partial headers safely instead of waiting on a long fixed delay.
- Fresh trainer/merger `.osu` files are watched and hashed in the background as they appear, avoiding repeated full `osu!.db` refreshes on the replay hot path.
- Cursor resampling now walks replay frames sequentially rather than binary-searching every sample, reducing post-play analysis cost.
- Selected-play inspector avoids rebuilding the same history/insights/background on every dashboard redraw.
- Diagnostics, Training, and Aim Analysis cache unchanged results, so switching tabs back and forth does not recompute the entire dataset.
- Data grids and tab controls use double buffering; several GDI paint allocations were also removed to reduce flicker/artifacts during tab/window switches.

## v19 — live proficiency collections

The **Collections** tab can route the latest analyzed run of a difficulty into analyzer-managed osu!stable collections according to configurable proficiency ranges.

- editable minimum / maximum proficiency bands;
- automatic routing after each newly analyzed replay;
- optional exclusive mode, so replaying a map into a different band moves it out of the other analyzer-managed bands;
- `Rebuild from history` uses the most recent analyzed run for each beatmap difficulty;
- a timestamped backup of the original `collection.db` is created before the analyzer's first write each app session;
- manual backup / restore controls are included;
- a `collection.db` watcher re-merges analyzer-managed memberships if osu! writes its own collection state later in the session.

### Important osu!stable limitation

`collection.db` is an on-disk collection database. osu!stable keeps its active collection state in memory and does not expose a supported external hot-reload API for changes another application writes into `collection.db`. The analyzer therefore **does not restart osu! after every play**: it writes/merges the file continuously and protects its managed memberships against later game writes. The file is ready for the next client load, but a collection membership written externally may not become visible inside an already-running stable client until the client next reloads its collection state.

The analyzer-managed collection format follows stable's documented `collection.db` structure: database version, collection count, collection names, and beatmap MD5 hashes.

## v20 collection history sorting

The Collections tab now has a **Sort previous plays** control with Today / 7 days / 30 days / 90 days / All time ranges. **Categorize history** takes the latest analyzed run for every exact beatmap difficulty in that window and routes it through the active proficiency collection rules in one batch. This operation is additive: map difficulties outside the selected window are left alone. `Rebuild ALL history` remains available when the analyzer-managed collections should be regenerated from scratch.
