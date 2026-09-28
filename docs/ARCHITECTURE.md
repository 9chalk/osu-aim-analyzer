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

## TG2 implemented boundaries

BeatmapDocument owns immutable preserving text/encoding; BeatmapResources inventories references without I/O. BeatmapTransforms returns a new document, audio-rendering requirement and labeled achieved head-spacing measure. It consumes TG1 options and performs no file writes. Unsupported changed content fails explicitly; unchanged documents round-trip without validation/reformatting.

BeatmapParser now shares its existing analysis parsing through ParseLines. The disk API retains existing cache/background behavior. ParseDocument provides an in-memory preview with empty hash/path/star cache and no background filesystem lookup; referenced backgrounds belong to the resource manifest. Source IDs remain document metadata until TG4 assigns generated identities; previews must not be persisted as source plays.

Spacing preserves slider shape by translation and uses control-point/repeat-parity exit proxies. It is not a rendered-curve evaluator or Toolkit's complete grouping/fitting algorithm. Export must additionally resolve implicit assets and external .osb files, validate physical containment and render rate-adjusted audio. There is still no exporter, audio service or practice UI. Recent Play readouts now use the full inner height; Overview retains its hero.

## TG3 implemented boundaries

PracticeSeriesPlanner is a pure, deterministic source-document-to-series planner. It consumes the existing numeric evidence with selected-play identity, bounds recipe changes, rejects incompatible/conflicting evidence, and deduplicates transformed content. Its policy is version 1 in PRODUCT_SPEC.md. No Toolkit star heuristic, production score change or lifetime-streak evidence is used.

MainForm adds a Practice page through the existing inspector navigation. PracticePreviewControl renders the result and exposes preview/cancel/pitch actions. Work, including result formatting, runs off the UI thread. PracticePreviewSession owns request versions/tokens; selection, retry, cancel and disposal invalidate pending work. PracticePreviewSource performs bounded read-only source reads and MD5 checks before planning and before display. Existing resolver/cache and diagnosis/database APIs remain authoritative. There is no new filesystem watcher or persistent preview storage.

The displayed result is an in-memory snapshot of the matching .osu file, not an export contract. External resource existence, .osb/sample discovery, physical path containment, generated identities, publication and audio remain TG4/TG5 work. Played mods are displayed but not baked into output; evidence from modded selected plays uses conservative NM fallback suggestions. Pitch is recorded for future rendering only.

## TG4 implemented boundaries

PracticePackageExporter owns validation, staging, archive verification, provenance and publication. It consumes TG1 identity/options and TG2 document/transforms; the selected set is explicit, bounded and rate-1 only. PracticeExportPaths centralizes containment, invalid/reserved path names, reparse-point rejection and non-overwriting destinations. BeatmapDocument.WithMetadata changes generated IDs/version only; existing analysis parsing/scoring is unchanged.

BeatmapResources is the shared explicit-resource parser for previews and export. Export additionally inspects .osb resources and retains conventional local sample banks, without copying original difficulties or introducing a new Songs index. Unknown resource-affecting constructs fail closed. Byte hashes are checked in the staged ZIP and against source assets before same-directory publication. Provenance lives in the package JSON; no database migration is introduced.

PracticeExportSelectionForm makes the incomplete rate-free subset explicit. MainForm reuses PracticePreviewSession cancellation/version checks for export and suppresses late progress/results. Open package is user-triggered shell import after publication; existing resolver Songs watchers handle subsequent imports. No automatic osu! launch or source-map overwrite occurs. Final import behavior is a manual test gate.
