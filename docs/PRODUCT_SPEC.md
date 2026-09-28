# Product Specification

## Current delivery status

TG5 is **IMPLEMENTED; NEEDS USER TEST**. Practice can export selected slowdown and spacing variants with rendered audio. No item is ACCEPTED. The dated batch records below describe what was available at each stage; TG5 supersedes the earlier audio/export deferrals. TG6 remains deferred.

## Source and scope

Source: [osu aim analyzer stuff](https://docs.google.com/document/d/1vg8Xf5UHyvgA7PpsN0DMNJQv_QjLkTa8aduJHUdMCHY/edit), explicitly confirmed by the owner as the intended source despite the earlier title “osu! Aim Analyzer — Ideas, Features & Bugs.” The complete returned document contains one tab, **Tab 1** (`t.0`), headed “Functions and features ideas,” and ends “we did the stuff already above.” This describes the initial snapshot before the later New stuff section (see Updated source and integration scope). Read on 2026-09-28 UTC (2026-09-27 Pacific).

Repository comparison: commit `231daed`, v32 source. Architecture references are `AGENTS.md`, `OsuAimAnalyzer/README.md`, and version notes, checked against code. No separate architecture document existed. These specifications do not change the Google Doc, application, scoring, or database.

AIM IDs are permanent: keep them when reordering, append new IDs, and retain retired IDs with a replacement reference. AIM-001 through AIM-007 cover the source; AIM-008 is a separately identified review finding. “Implemented” means present and connected in source, not runtime-verified. No build, replay import, or UI test was performed for this planning task.

## Updated source and integration scope

The same owner-confirmed Google Doc was re-read in full on 2026-09-28 UTC. Its current title is **new osu aim analyzer stuff**, and its single tab now includes a **New stuff** section requesting supplemental training-map generation. The original snapshot description above is historical; the appended requirements below supersede its scope limitations. No Google Doc was edited. Baseline is `adc29a1`. See [ARCHITECTURE.md](ARCHITECTURE.md) and [TOOLKIT_V1_3_PORTING_AUDIT.md](TOOLKIT_V1_3_PORTING_AUDIT.md). AIM-001–AIM-008 remain stable; AIM-009–AIM-017 cover the new source requirements. No application implementation is included.

## Current architecture

The single `net8.0-windows` WinForms project keeps UI, parsing, analysis, and SQLite persistence in one assembly. `ReplayReader` and `BeatmapParser` provide input; `AimAnalyzer` derives `TransitionMetric` values; `AnalyzerDatabase` stores transitions keyed by play. `ProductionScoring` and `ScoringConfig` govern scores.

`AimErrorDiagnostics.Diagnose` derives a likely movement cause from stored landing, arrival, stability, braking, straightness, ideal-path, signed error, and correction information. `Analyze` aggregates counts and detects repeated causes. `AimCauseProfileControl`, `PlayInsightBuilder`, `MainForm`, and `AdvancedDiagnosticsForm` present the result. `LifetimeAimAnalysisBuilder` groups per-play history; `AimErrorContextAnalyzer` compares causes and directions with map conditions. Diagnosis is a derived interpretation, separate from production scoring.

## Requirements: diagnostic interpretation

### AIM-001 — Explain movement errors using trajectory metrics

- **Source intent:** “More detailed error breakdowns” using cursor trajectory and movement to explain what caused an error.
- **Behavior:** For each analyzed transition, report a likely cause, supporting metric values, and an understandable explanation; aggregate causes per play. Preserve uncertainty rather than claiming proven physical causation.
- **Status/evidence:** Implemented in `AimErrorDiagnostics.Diagnose`, `Explain`, and `Analyze`. Nine non-clean categories plus clean control, severity, and heuristic confidence already exist. This is not proof that every available metric must enter every rule.
- **Touches:** `AimAnalyzer`, `Models.TransitionMetric`, `AimErrorDiagnostics`, scoring metric interpretation, presentation consumers.
- **Dependencies:** Existing stored telemetry; preserve AIM-002. AIM-003 specializes this capability.
- **Difficulty/risk:** Medium to validate; high if recalibrating rules. Threshold changes can relabel all historical plays without modifying stored data.
- **Acceptance:** Fixed synthetic transitions yield deterministic diagnoses and explanations; clean inputs remain clean; diagnosis does not mutate input metrics or scores.

### AIM-002 — Preserve directional errors alongside causes

- **Source intent:** “Right now we have under,over, error type stuff” is existing context, not a request to delete it.
- **Behavior:** Keep underaim, overaim, lateral, correction, and other landing classes visible separately from movement causes. A direction and a cause may coexist.
- **Status/evidence:** Implemented: `TransitionMetric.ErrorClass`, `AimErrorProfileControl`, and `AimCauseProfileControl` remain separate; `AimErrorContextAnalyzer.Build` accepts `direction:` and cause selections.
- **Touches:** Models, both profile controls, context analysis, detailed table.
- **Dependencies:** Existing landing classification; AIM-001 supplies the additional cause dimension.
- **Difficulty/risk:** Low preservation work; medium regression risk if UI or models conflate the dimensions.
- **Acceptance:** The same transition can show overaim and braking overshoot without either overwriting the other; direction statistics remain unchanged.

### AIM-003 — Distinguish acquiring a target from shaking off it

- **Source intent:** “Sometimes you are on target and shake off the circle.”
- **Behavior:** Identify likely loss of control after initial acquisition, distinguishing it from never reaching the target. Explain which observations support that interpretation.
- **Status/evidence:** Partially implemented under a literal interpretation. `AimErrorDiagnostics.Diagnose` has a Shake-off rule using strong arrival, weak stability, and final landing/error. It infers the event from aggregate metrics; it does not itself inspect frame-by-frame entry followed by exit from the circle.
- **Touches:** `AimErrorDiagnostics`, `AimAnalyzer`, `DiagnosticsEngine`; potentially replay reconstruction and persisted telemetry if exact event evidence is later required.
- **Dependencies:** AIM-001 and AIM-002. Exact-event work requires a separate decision on timing windows and available frames.
- **Difficulty/risk:** Medium for validating the existing inference; high for exact trajectory event detection and historical compatibility.
- **Acceptance:** Inferred shake-off cases are distinguished from late acquisition and clean settling; UI explains the metric evidence. Do not label an inferred event as a frame-verified event.

### AIM-004 — Explain repeated errors across successive objects

- **Source intent:** “If you shake off a bunch of objects in a row but were initially on target, what does that mean?”
- **Behavior:** Identify a same-cause sequence within one play, show object range and count, and provide a cautious explanation of the repeated pattern.
- **Status/evidence:** Partially implemented. `FindLongestStreak` and `RepeatedMeaning` provide ranges and explanations. Current adjacency permits object-index gaps up to two and time gaps up to 1,200 ms. The control shows streaks from two samples; text summaries generally require three. Lifetime use has the AIM-008 risk.
- **Touches:** `AimErrorDiagnostics`, `AimCauseSummary`, `AimCauseStreak`, `AimCauseProfileControl`, `PlayInsightBuilder`, lifetime summaries.
- **Dependencies:** AIM-001/AIM-003; AIM-008 before trusting mixed-play streaks.
- **Difficulty/risk:** Medium. “In a row” is not defined by the source; filtered transitions and skipped objects complicate adjacency.
- **Acceptance:** Streaks never cross plays, break on a different/clean cause, and obey an explicit adjacency policy. Repetition explanations remain hypotheses, not measurements of grip or tension.

## Requirements: presentation

### AIM-005 — Show diagnostics in Recent Play

- **Source intent:** Add diagnostics to “the recent play.”
- **Behavior:** Present the selected play's cause breakdown and repeated-pattern explanation alongside existing directional errors; clear stale information when selection changes or data is absent.
- **Status/evidence:** Implemented in `MainForm`: `playInspectorCauseProfile`, the “Why control broke” panel, and `SetData(inspectorTransitions)`; `PlayInsightBuilder.BuildSections` provides Errors text. A separate run Diagnosis page also exists.
- **Touches:** `MainForm`, `AimCauseProfileControl`, `PlayInsightBuilder`.
- **Dependencies:** AIM-001 through AIM-004.
- **Difficulty/risk:** Low for verification, medium for layout changes in the large `MainForm`.
- **Acceptance:** Changing selected runs changes the cause panel and explanation consistently; empty selections clear them; small window sizes remain readable.

### AIM-006 — Show diagnostics in the detailed table window

- **Source intent:** The other half of “recent play and detailed table window.”
- **Behavior:** Show per-object likely cause, confidence, and supporting explanation, plus a play-level summary and context for reconstructed top errors.
- **Status/evidence:** Implemented in `AdvancedDiagnosticsForm`: cause profile, “Likely cause” and “Cause conf.” columns, `AimErrorDiagnostics.Diagnose` calls, and reconstructed-path explanation text.
- **Touches:** `AdvancedDiagnosticsForm`, `DiagnosticsEngine`, shared diagnostic and profile code.
- **Dependencies:** AIM-001 through AIM-004; existing reconstruction for path views.
- **Difficulty/risk:** Low/medium. Reconstructed samples and persisted transitions must refer to the same objects.
- **Acceptance:** The same transition has the same cause in the table and Recent Play; explanations identify relevant metrics; unavailable reconstruction does not fabricate a path.

### AIM-007 — Extend the analysis window's error views

- **Source intent:** Integrate diagnostics into “the existing error types thing in the analysis window.”
- **Behavior:** Show historical cause distribution alongside direction, allowing cause selection and inspection of related conditions; respect the selected history window.
- **Status/evidence:** Implemented in source: `MainForm` lifetime cause profile, dominant-cause summary, cause history series, and cause/direction selector; `LifetimeAimAnalysisBuilder` and `AimErrorContextAnalyzer` supply data. The streak aspect is limited by AIM-008.
- **Touches:** Lifetime analysis, context analysis, `MainForm`, cause profile; existing training diagnosis is supporting functionality, not a new source requirement.
- **Dependencies:** AIM-001/AIM-002; AIM-004/AIM-008 for repeated-pattern display.
- **Difficulty/risk:** Medium: mixed-play aggregation, denominators, filtering, and UI refresh cost.
- **Acceptance:** Historical counts match filtered transitions; clearly distinguish percentage of all jumps from percentage of diagnosed problem jumps; no streak crosses play boundaries.

## Review finding, not a Google Doc request

### AIM-008 — Isolate streaks by play

- **Origin/status:** Code-review finding; not runtime-reproduced. `Analyze` sorts all input by map-relative `TimeMs`; `FindLongestStreak` checks cause, object gap, and time gap without checking `PlayId`. `MainForm` passes mixed-play `data.Transitions` into the lifetime profile and summary. `TransitionMetric` already has `PlayId`, but `AimCauseStreak` does not carry it.
- **Required outcome:** Compute candidate streaks per play, then select a lifetime result without combining plays. Retain the owning play so object ranges have context. Keep overall cause counts aggregated across the selected history.
- **Touches:** Diagnostic aggregation/result contracts, lifetime consumers, profile and summary rendering.
- **Dependencies:** Existing AIM-004 behavior; prerequisite for its historical correctness and AIM-007 verification.
- **Difficulty/risk:** Medium; low schema risk if results remain derived. Changing streak output is a future behavior fix, not part of this documentation task.
- **Acceptance:** Two plays with overlapping times/indices cannot form one streak; interleaved records do not split a valid within-play streak; single-play results and aggregate cause counts remain stable.

## Duplicates, contradictions, and assumptions

- The shake-off example and its repetition expand AIM-001; they are not three independent diagnostic engines. AIM-005 and AIM-006 split one sentence because the UI surfaces need independent acceptance checks.
- “We did the stuff already above” is a completion annotation, not another feature. Code largely supports it; literal acquisition evidence and streak correctness prevent declaring the entire intent fully validated.
- Assume “caused” means an inferred movement explanation. The existing architecture cannot prove physiological causes such as grip tension from aggregate replay telemetry. Stronger claims require additional evidence and explicit scope.
- Assume existing directional labels must survive. Replacing them would conflict with both source context and the current two-dimensional design.
- Assume the “detailed table window” is `AdvancedDiagnosticsForm` and the “analysis window” is Aim Analysis. These are the matching existing surfaces.
- “All those metrics” means relevant available movement evidence, not a mandate to include every scalar or invent new telemetry. Exact shake-off timing, allowed skipped objects, minimum repetition count, and confidence calibration remain open requirements. Preserve current thresholds until reviewed.
- The original diagnostics section requests no new scoring formula or training generator. The later supplemental-map section below adds generation; it does not request cloud services, a new UI framework, or production scoring changes. A literal frame-verified shake-off feature would exceed today's summary-metric diagnostic contract and is deferred, not silently specified as approved work.

## Refactoring before further implementation

First add characterization coverage around the pure diagnostic functions in a separate test project. Then make per-play versus historical aggregation explicit and carry play identity in derived streak results (AIM-008). Reuse the same diagnosis results across UI consumers where useful; do not rewrite the large `MainForm` wholesale. Keep rule changes separate from aggregation fixes so changed labels are attributable. Existing telemetry supports the initial work without a database migration.

## Toolkit-derived supplemental training backlog

All features below are unimplemented; existing supporting capabilities are called out explicitly. Python implementations are behavioral references, not drop-in C# modules. Strategy labels are DIRECT REUSE, ADAPT, REIMPLEMENT, ALREADY EXISTS, and CONSOLIDATE.

### AIM-009 — Supplemental training for the selected map

**Intent:** Generate supplemental maps from individual-map diagnosis to help improve the selected map. **Toolkit:** TrainerTab.generate, trainer_core.build_practice_osu/export_practice, SpacingTab.generate and spacing exports. **Strategy:** REIMPLEMENT orchestration; ADAPT transforms; CONSOLIDATE MainForm selected-play context, BeatmapResolver and AimTrainingDiagnosisEngine rather than introducing another history/selection system. **Dependencies:** AIM-010–AIM-016 and resolved source/resources. **Risk:** High; stale selection and original/generated identity confusion. **Tests:** Immutable selection snapshot, changed/missing source, cancellation and correct source hash. **Changes:** New planner/service and native UI; parser/export/storage through dependencies; no production scoring change or mandatory existing-history schema migration.

### AIM-010 — Evidence-guided useful adjustments

**Intent:** Use “what seems to improve” this type of map to choose useful demands, neither barely changed nor trivially easy. **Toolkit:** Rate/stat/spacing options execute adjustments but contain no learned recommendation system. **Strategy:** REIMPLEMENT variant selection; CONSOLIDATE AimTrainingDiagnosisEngine.BuildCategory/BuildRunDiagnosis, AimCategoryDiagnosis.Factors/Routes, LifetimeAimAnalysisBuilder, TrainingEngine and AnalyzerDatabase. Generalize structured run-level evidence; never parse BuildRunDiagnosis prose as an API. **Dependencies:** Comparable history, explicit target bands and low-evidence policy; AIM-008 if streaks inform decisions. **Risk:** High: controlled-range associations are not evidence that practice caused improvement. Initial proposals must state uncertainty. **Tests:** Sparse history, conflicting factors, deterministic bounded recommendations and unchanged scoring. **Changes:** Structured diagnosis/planner architecture and explanation UI. Current-history suggestions need no schema migration; longitudinal intervention/outcome learning requires separately specified storage and validation.

### AIM-011 — Lower BPM with comparable spacing and selected difficulty reductions

**Intent:** Lower BPM while retaining relevant spacing, optionally easing diagnosed AR/CS/OD/HP demands. **Toolkit:** trainer_core.build_practice_osu, scaled_ar/scaled_od, _retime_hitobject/_retime_timing/_retime_event/_retime_bookmarks and generate_audio. **Strategy:** ADAPT pure transformations; CONSOLIDATE ModUtils/AimAnalyzer time/radius helpers; REIMPLEMENT audio jobs and preserving-document writes. **Dependencies:** Preserving parser, explicit base/effective/generated-stat contract, AIM-010 and shared exporter. Scale variable-BPM timing segments rather than flattening them. **Risk:** High: double clock scaling, slider/SV timing, AR/OD compensation, rounding, pitch and unsupported storyboard timing. CS changes normalized spacing, so retain CS by default when spacing is the controlled variable. **Tests:** Multi-BPM, sliders/spinners, sentinels, rate 1.0, DT/NC/HT and source immutability. **Changes:** Parser, transformation/audio services, generated resources and preview UI; provenance manifest, no compulsory history-table change.

### AIM-012 — Plain slowdown

**Intent:** “Just plain slow down the map,” without unrelated geometry edits. Proposed default: serialized difficulty settings stay unchanged while timing/audio slow down, and effective AR/OD are shown. Pitch preservation is a proposal, not a source requirement. **Toolkit:** build_practice_osu, generate_audio and _atempo_chain. **Strategy:** ADAPT the same rate transform as AIM-011; CONSOLIDATE options/renderer, not another slowdown engine. **Dependencies:** Document/rate contract and export, but not a learned model. **Risk:** Medium/high audio/timing risk. **Tests:** Expected timestamps/duration, unchanged coordinates/stored stats, cancellation and missing audio. **Changes:** Same service/storage boundaries as AIM-011, distinct recipe/UI label. Deduplicate if AIM-011 proposes identical settings.

### AIM-013 — Lower spacing with unchanged BPM

**Intent:** Reduce spacing enough to be useful while retaining challenge and BPM. **Toolkit:** spacing_core.apply_pattern_transform, BeatmapDocument.transform, _build_layout, _find_closest_fitting_multiplier and TransformResult. **Strategy:** ADAPT geometry; CONSOLIDATE Analyzer normalization and recommendation evidence; use a preserving document rather than HitObjectData alone. **Dependencies:** AIM-010, geometry/document boundary and exporter. Preview achieved spacing and capped/repositioned groups. **Risk:** High: approximate slider exits, fallback shape distortion and playfield fitting. **Tests:** Unchanged times/BPM/audio, slider repeat parity/length, close jumps, bounds, repeated preview immutability and achieved reduction. **Changes:** Parser, geometry service, preview and generated-file storage; no initial schema migration. Flips are not requested.

### AIM-014 — Approximately five complementary variants

**Intent:** “Make like a total of 5 per selected map.” Target five distinct explained difficulties in one series; propose fewer with a reason if constraints/evidence cannot support five. **Toolkit:** Single-map generators exist, but no five-map planner. **Strategy:** REIMPLEMENT deterministic series planning; CONSOLIDATE shared options, diagnosis and source identity. **Dependencies:** AIM-010–AIM-013 and AIM-016; bounded recipe definitions. **Risk:** Medium/high: inventing unjustified recipes, duplicate options/audio and partial publication. **Tests:** Unique recipes, stable ordering, sparse evidence, count explanation and all-or-nothing publication of the selected set. **Changes:** Planner, series preview and versioned provenance manifest. Exact count and remaining recipe choices remain open; no database migration required for initial export provenance.

### AIM-015 — Recent Play generation tab

**Intent:** Put the workflow in Dashboard Recent Play. Show variants/reasons, explicit generation, progress/errors and a finished-package opening action. **Toolkit:** TrainerTab/SpacingTab worker/debounce concepts, not Tk widgets. **Strategy:** REIMPLEMENT native page with MainForm.playInspectorPages, ToolkitUi and Theme; CONSOLIDATE existing selected-play state. Existing Training prose is ALREADY EXISTS support, not a generator. **Dependencies:** Planner, preview and exporter. **Risk:** Medium: stale selection, UI blocking, retry/cancel lifecycle. **Tests:** Empty states, rapid selection, layout, progress, cleanup and import handoff. **Changes:** UI/job coordination; no independent schema. Do not auto-generate or replace the inspected replay's map with the foreground osu! selection.

### AIM-016 — Separate mapset retaining song title and background

**Intent:** Put the new maps in a separate mapset with identical visible song name and background image. **Toolkit:** spacing_core.generate_new_mapset_package, trainer_core.export_practice/_find_background and merger export collision concepts. **Strategy:** REIMPLEMENT staged multi-difficulty export; ADAPT resource/CSV handling; CONSOLIDATE BeatmapResolver invalidation and AppSettings. BackgroundPath parsing partly ALREADY EXISTS. **Dependencies:** AIM-014, preserving document, resource manifest and optional audio renderer. **Risk:** High: reused original IDs, name collisions, nested media, deleted temporary paths and unrelated source difficulties. Do not copy Toolkit's title suffix or package contents blindly. **Tests:** Same title/background bytes, new local identities, only planned difficulties, nested assets, repeat exports, integrity and failure cleanup. **Changes:** Parser, exporter, resource/provenance storage and native open action. No writes into the original set; existing-mapset exports are out of scope. Schema changes conditional on future outcome queries, not initial packaging.

### AIM-017 — Broader diagnostics generation later

**Intent:** A more general version later in the diagnostics tab, with greater scope. **Toolkit:** Same trainer/spacing primitives; no diagnosis-wide planner. **Strategy:** REIMPLEMENT later orchestration; CONSOLIDATE AimTrainingDiagnosisEngine, Aim Analysis context and AIM-009–AIM-016 services. **Dependencies:** Proven selected-map workflow; define categories, candidate-map selection, output count and exact diagnostics surface. **Risk:** High scope uncertainty. **Tests:** Future multi-map provenance isolation, bounded batch size and cancellation. **Changes:** Likely UI/orchestration; schema/storage TBD. Explicitly deferred; do not infer downloading, merger or automatic longitudinal learning.

## Coverage, consolidation and product decisions

AIM-001–AIM-008 remain ALREADY EXISTS/native correctness work. Toolkit has no replacement replay-diagnosis engine. AIM-009–AIM-017 cover all new source obligations; “etc” is an extension marker, not a hidden requirement. DIRECT REUSE applies to existing production memory_reader.ps1/setup_native.ps1, already byte-identical to Toolkit; retain them without duplication.

Generalize one settings/path owner, resolver/hash index, selected-play context, time/mod model, structured diagnosis API, preview-job lifecycle, document adapter and exporter. No parallel database or Toolkit application shell. Merger, flips, random backgrounds and original-mapset writes are audit opportunities, not this backlog's requested features.

Open choices: exact five versus approximate target; useful recipe/target bands; pitch and stat-compensation defaults; base map versus baked played mods; low-evidence behavior; and the later diagnostics scope. Preserve original song/background; use difficulty names and fresh generated identities for distinction. Label suggestions as association-based until actual improvement outcomes are defined. Existing stored diagnoses and scores must not change merely because generation is added.

## TG1 implementation status — 2026-09-28 UTC

TG1 infrastructure is **IMPLEMENTED; NEEDS USER TEST**. None of AIM-009–AIM-017's complete user-facing generation features is implemented or accepted. The foundation portions of AIM-009/AIM-010 (source identity and structured run evidence), AIM-011–AIM-013 (explicit option contracts), and AIM-016 (published-package result contract) are IMPLEMENTED only; transformation/export/UI behavior remains deferred.

The engine now exposes BuildRunDiagnosisData with detached RunDiagnosisResult/RecommendationEvidence while preserving BuildRunDiagnosis text. Numeric evidence avoids parsing localized prose. The existing PlayerInsightsEngine.TrainingResponseForRun already reports recent sequence associations; these are preserved in the result, not replaced by a new learning system. ModUtils and AimAnalyzer formulas remain the shared implementation with characterization tests, not copied Toolkit formulas.

Verification: 43 native tests pass, including pre-refactor diagnosis text snapshots, structured/in-memory-database equivalence, invalid contract inputs and mutation isolation. Debug build and Release single-file publish succeed with the pre-existing WFAC010 DPI warning. Manual real-history UI checks are NEEDS USER TEST. See PROJECT_STATUS.md at the repository root and TG1_NOTES.md in OsuAimAnalyzer/ for risks and steps. TG2 status is recorded below; no status is ACCEPTED.

## TG2 implementation status — 2026-09-28

**IMPLEMENTED:** preserving BeatmapDocument, referenced-asset BeatmapResources inventory, shared BeatmapParser.ParseDocument preview adapter, and pure BeatmapTransforms. AIM-011/AIM-012 now have internal clock/stat transformation support; AIM-013 has internal spacing reduction support; AIM-016 has document/resource prerequisites. Complete user-facing generation features remain unimplemented, not ACCEPTED.

The source document retains comments, unknown sections, line endings, UTF-8/UTF-16 BOM and untouched fields. Identity is byte-preserving. Changed standard maps support timing offsets/red-line beat lengths, inherited velocity preservation, object/spinner timestamps, bookmarks, preview/audio lead-in, breaks and samples. Generated HP/CS/AR/OD are explicit 0–10 values, with no implicit mod baking or compensation. Rate previews require later audio rendering.

Spacing translates each slider's entire shape, preserving repeats, curve markers and length. Exit positions use Toolkit's last-control-point/repeat-parity approximation; achieved spacing is explicitly a consecutive-head metric, not exact rendered slider geometry. This conservative first service supports reductions, rejects out-of-bounds geometry instead of clamping, and does not implement Toolkit grouping/fitting or spacing expansion. A planner must handle unsupported candidates explicitly.

Retiming rejects video, storyboard commands and unknown sections. Identity documents still preserve them. The resource inventory handles quoted/nested audio, backgrounds and explicit samples/hitsounds, reports unsafe paths, and does not certify completeness or asset existence. External .osb discovery, implicit sample-set resolution, physical containment/symlink validation and publication belong to TG4/TG5. No writer, audio dependency, schema migration or source-map mutation was introduced.

**NEEDS USER TEST:** Recent Play readouts now occupy full inner height; the hero remains on Overview. Resize/switch testing passes automatically, but real-history readability and DPI checks remain manual. TG2 has no new navigation entry. Full suite: 71 passed, including all 43 TG1 cases.

## TG3 implementation status — 2026-09-28

**IMPLEMENTED; NEEDS USER TEST:** selected-play series planning and the Recent Play **Practice** preview page (AIM-009/AIM-010/AIM-014 and the preview portion of AIM-015). The user approved the preview recipe defaults during implementation. Actual map generation, audio rendering and package opening remain TG4/TG5; no complete export feature is ACCEPTED.

PracticeSeriesPlanner reuses TG1 source/options/evidence and TG2 BeatmapTransforms. Policy v1 targets up to five content-distinct variants: mild slowdown, stronger slowdown, spacing reduction, slowdown plus spacing, and slowdown plus eased AR when evidence supports it. The engine has no OD-specific factor, so OD/CS/HP remain unchanged. Sparse evidence normally gives four explicitly labeled conservative proposals; unsupported, conflicting, duplicate or unchanged recipes are omitted with reasons. Original input is always the basis for each recipe.

Preview policy: qualifying numeric evidence needs at least 10 error and 20 comparison samples, confidence at least 55%, lift at least 1.1, finite ordered quartiles and an observed value above the controlled upper quartile. These are conservative product thresholds, not statistical or causal guarantees. Stronger rate and spacing target the upper-quartile/observed ratio bounded to 0.80–0.95; mild rate is halfway to 1. Defaults are 0.95/0.90 rate and 0.90 spacing. AR eases by 0.25–1.0 only with compatible evidence and a lower target. Reliable below-band/conflicting evidence blocks reductions of that variable. No lifetime streak or longitudinal learning is used.

Targets are explicitly NM/source-relative. Modded selected plays receive conservative defaults rather than applying their effective values to unmodded targets. Played mods remain provenance/context. Serialized stats do not automatically receive DT/HT compensation when timestamps are authored. The preview reports source rate/BPM changes, requested/achieved head spacing, before/after stats, reasons, sample counts, uncertainty and later audio needs. Pitch is an explicit preview selector (preserve by default), with no audio output or persisted setting yet.

PracticePreviewSession cancels superseded work and rejects late results on selection changes, retry, cancel or disposal. Existing BeatmapResolver is reused only for cached resolution when the stored path is unavailable; no new Songs scan/index is introduced. Source bytes are bounded to 16 MiB and verified against replay MD5 before planning and again before display. A displayed preview remains a snapshot; subsequent edits require rebuilding, and future export must revalidate independently. Resource/export completeness is not claimed.

Tests: 87 pass, covering all prior regression cases plus policy, conflicting/sparse evidence, mod boundaries, content deduplication, unsupported recipes, source freshness, cancellation/stale completion and the actual MainForm preview/retry/selection-reset flow. User confirmed the TG2 Recent Play layout works; TG3 visual testing remains pending.

## TG4 implementation status — 2026-09-28

**IMPLEMENTED; NEEDS USER TEST:** explicit spacing/stat-only package export (AIM-013 and export infrastructure for AIM-009/AIM-014/AIM-015/AIM-016). The user confirmed TG3's preview looked alright. Full slowdown-series export still requires TG5; no item is ACCEPTED.

Practice now offers **Export spacing/stat maps…** after a successful preview. A selection dialog lists only eligible rate-1 variants and states how many slowdown variants are unavailable. The current planner normally provides one eligible Reduced spacing recipe; the shared exporter supports up to five selected distinct spacing/stat variants. No recipe is silently dropped by the exporter: a selected rate-dependent, unchanged, duplicate or stale recipe fails the operation. Save location is explicit, overwrites are refused, and Open package is a separate user action after successful publication.

PracticePackageExporter recomputes from hash-verified source/options, validates preview equivalence, stages a ZIP beside the destination, verifies entry hashes, and rechecks source/resources before a non-overwriting rename. Output is outside the source mapset, configured Songs tree and reference/; linked/reparse-point paths and unsafe archive-relative names are rejected. Original files, score history and schema remain unchanged. Existing Songs watcher/hash invalidation handles a later user import; exporting itself does not rebuild/scavenge Songs.

Generated .osu files retain song title, artist and resource paths. BeatmapID becomes 0, BeatmapSetID becomes -1, and unique package/variant labels distinguish difficulty versions. Only selected generated difficulties are packaged, with referenced nested audio/background/video/sample assets and preserved supported .osb files. All top-level normal/soft/drum hitsound and slider-sample banks are conservatively retained to preserve edge/tick/index fallback; no unrelated original difficulty or arbitrary mapset file is copied. Media is copied byte-for-byte, not decoded or rendered.

Resource limits: 16 MiB per .osu/.osb document, 256 MiB per asset, 1 GiB total preflight resources and 20,000 asset entries. Sprites, frame animations, standard non-resource storyboard commands and explicit samples are supported unchanged at rate 1. Storyboard variables, unknown event commands, unsupported media extensions, missing assets and links fail explicitly. The archive provenance JSON records schema/package IDs, source hash/play/mods, options, recipe reasons, generated hashes and resource hashes without another database.

Publication is the commit point: cancellation before it cleans owned staging; cancellation after a successful rename does not delete the finished user artifact. Displayed preview/source changes are revalidated for each export. Normal editing races are detected through rechecks; this is not a hostile concurrent-filesystem security boundary. Import behavior, audio playback and separate-mapset presentation still need actual osu! testing.

### TG4 availability clarification

A preview can contain only slowdown variants when spacing transformation is unsupported. Such a preview has zero exportable maps in TG4. The Practice toolbar now displays this count and the specific spacing omission reason; Export/Open clicks explain unavailable prerequisites. No package is implied to exist before successful publication. The user's reported two-slowdown/out-of-bounds case is covered by a native UI action test.

## Spacing compatibility correction

IMPLEMENTED; NEEDS USER TEST in osu!: spacing now fits bounded groups (16 objects, split at spinners or source-time gaps over 1 second), translates groups without altering slider vectors, and eases the requested multiplier toward the source when required for feasibility. Only whole-object/group translations are performed; no point clamping or slider-length/repeat changes. Object heads must remain in the playfield. Existing off-screen slider anchors are allowed within each object's original envelope, without increasing its extent; anchors are not treated as exact rendered paths. Exact curve evaluation remains out of scope.

Preview and provenance report repositioned groups, relaxed groups and original off-screen anchors. Achieved head spacing remains authoritative; unchanged/ineffective recipes are still omitted. A read-only check of the reported map, MD5 c252e57acfbd38812c12023f44882b22, now produces four variants. Reduced spacing achieves 0.9115x head spacing, with one repositioned group, no relaxed groups and three existing off-screen anchors. Source bytes were verified unchanged. Personal map content was not added to tests or Git.

Native suite: 105 passed. Synthetic cases cover boundary repositioning, off-screen anchor preservation, full-width-slider relaxation, source-time grouping under combined rate/spacing, deterministic output and unsupported off-screen heads. TG5 is not started.


## TG5 implementation status — 2026-09-28

**IMPLEMENTED; NEEDS USER TEST:** source-relative slowdown audio (AIM-011), explicit pitch/stat policy (AIM-012), selected-series export (AIM-009/AIM-014), Recent Play generation (AIM-015), and preserved title/background with fresh generated identities (AIM-016). AIM-010 evidence policy and AIM-013 spacing behavior are reused unchanged. AIM-017 remains deferred; no item is ACCEPTED.

Practice → Build preview → **Export practice maps…** now permits all supported preview variants. Preserve pitch remains the default; Change pitch with rate lowers pitch during slowdown. Changing pitch invalidates the preview. Export renders only the user's selection and does not silently drop a selected recipe on failure. The planner still offers up to five distinct variants, normally four with sparse evidence; the optional eased-AR recipe requires qualifying evidence. HP/CS/OD and source-relative mod semantics are unchanged.

PracticeAudioRenderer runs the pinned FFmpeg distribution described in AUDIO_RUNTIME.md. MP3/Ogg/WAV input becomes stereo 44.1 kHz PCM16 WAV. The exporter snapshots/hashes source audio, deduplicates within a package by source content/rate/pitch/pipeline, rewrites each generated AudioFilename, and verifies the archive and unchanged original resources before publication. Schema-2 package provenance records rendered durations, pitch/rate, renderer identity and audio checksums. No application database/schema or scoring changes occur.

Renderer failures/cancellation clean owned audio/ZIP staging before publication. Missing tools still allow spacing/stat export; the dialog explicitly counts unavailable rate variants. Rate export rejects external .osb files and unsupported embedded video/storyboard content rather than publishing unsynchronized content. Source and output duration are limited to 30 minutes, further constrained by 256 MiB per asset and 1 GiB total resources. WAV output can make packages much larger than the original compressed song.

Verification: 120 native tests pass, including real synthetic audio frequency/timing, MP3/Ogg decoding, live cancellation, full-series/MainForm export, audio sharing and pitch separation, resource mutation and failure cleanup. Original diagnosis snapshots remain unchanged. The user confirmed the spacing follow-up works. Actual osu! import, synchronization and listening quality for TG5 remain NEEDS USER TEST.
