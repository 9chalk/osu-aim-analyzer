# Aim Analyzer Architecture

## Current production architecture

This describes the v32 source at baseline `adc29a1`. `OsuAimAnalyzer/OsuAimAnalyzer.csproj` is a single .NET 8 Windows Forms executable. No Toolkit feature integration is implemented. Dependencies include Microsoft.Data.Sqlite and SharpCompress; native song selection optionally uses the existing PowerShell reader and two downloaded native packages.

`Program` loads `AppPaths` and `AppSettings`, initializes `AnalyzerDatabase`, and starts `MainForm`. Settings and analysis storage are under the user's local application data. `MainForm` coordinates application views and replay workflows; `ReplayToolsForm` hosts replay tools and the selected-map Quick Checker.

## Data flow and ownership

1. ReplayReader parses replay input. BeatmapResolver resolves identity through osu!.db, fallback hashes, parsed-map cache, and Songs watchers; BeatmapParser produces analysis-oriented BeatmapData.
2. AimAnalyzer reconstructs movement metrics. ProductionScoring, ScoringConfig, and related utilities calculate scores; mod/time transforms must remain consistent with replay coordinates.
3. AnalyzerDatabase persists plays and transition telemetry. LifetimeAimAnalysisBuilder groups history; AimErrorDiagnostics derives causes and streaks; AimErrorContextAnalyzer and AimTrainingDiagnosisEngine derive contextual explanations and recommendations.
4. MainForm, AdvancedDiagnosticsForm, profile controls, and timeline/graph controls present those results. ToolkitUi and Theme are the native presentation infrastructure.

SelectedBeatmapReader is an existing optional stable-memory adapter, consumed by ReplayToolsForm. It is separate from replay ingestion. QuickCheckEngine projects using Analyzer's map/scoring conventions. Cached stars, fallback estimates, pp estimates, and production proficiency have different meanings and must not be conflated.

## Limitations relevant to generation

BeatmapData is not a preserving editor document: hit objects retain heads/times/kinds rather than complete slider and source-section data. There is no shared practice-map exporter, audio renderer, or five-variant recommendation planner. Run diagnosis currently exposes prose via BuildRunDiagnosis, while category diagnosis has structured factors/routes. Cross-sectional comfort-band associations are not longitudinal proof of improvement.

## Proposed integration boundaries (not implemented)

Retain existing owners for identity, settings, history, scoring, and UI. Add a preserving BeatmapDocument with an adapter to analysis inputs, immutable transformation options, a structured variant planner, pure rate/spacing transforms, and a staged package exporter. Audio rendering is optional until rate export needs it. Proposed provenance manifests link generated hashes to source hashes/options/evidence without changing original replay records. A database migration is conditional on future persisted provenance/outcome-query requirements, not required for initial in-memory previews.

Generalize SelectedBeatmapReader lifecycle and path discovery only where needed; selected-play generation can use the replay's already-resolved map without requiring a running osu! process. Do not create duplicate watchers, mod constants, database stores, or competing diagnosis engines. See the product spec and TG implementation batches for acceptance and dependencies.

## Reference and branch boundary

Everything under `reference/` is read-only and Git-ignored. The actual extracted v1.3 source currently lives in `reference/osu-toolkit-1.4`; the differently named requested path is absent. Do not rename/edit reference contents or include them in production commits. A Git ignore rule is not an operating-system write lock; the read-only constraint is a contributor rule in AGENTS.md. Integration planning is isolated on `feature/toolkit-integration`; `master` remains at the baseline commit.

## TG1 implemented boundaries

PracticeContracts.cs adds immutable source/option/package-result values within the existing assembly. No new runtime service, file index, database or writer is introduced. Existing resolver output supplies identity. Options distinguish source serialized difficulty from generated serialized difficulty; effective replay math remains in ModUtils/AimAnalyzer and is tested in place.

AimTrainingDiagnosisEngine.BuildRunDiagnosisData shares its core with the existing text API and supports either database-backed or already-loaded comparison transitions. RunDiagnosisResult exposes copied immutable numeric evidence, detached sequence-response strings and DisplayText. Existing UI callers retain BuildRunDiagnosis, so no new UI wiring is required. Category factor numbers are exposed from calculations already performed; thresholds and formulas are unchanged. Confidence/controlled ranges retain existing association/fallback semantics.

OsuAimAnalyzer.Tests references the application assembly directly, targets Windows/.NET 8 and uses xUnit. Tests use synthetic data, four pre-refactor text snapshots and in-memory SQLite. The previously described prose-only limitation is resolved for run-level factors; the preserving parser, planner, exporter and audio services remain proposals. TG1 is IMPLEMENTED / NEEDS USER TEST, not ACCEPTED.
