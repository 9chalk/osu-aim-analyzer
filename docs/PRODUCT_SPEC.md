# Product Specification

## Source and scope

Source: [osu aim analyzer stuff](https://docs.google.com/document/d/1vg8Xf5UHyvgA7PpsN0DMNJQv_QjLkTa8aduJHUdMCHY/edit), explicitly confirmed by the owner as the intended source despite the earlier title “osu! Aim Analyzer — Ideas, Features & Bugs.” The complete returned document contains one tab, **Tab 1** (`t.0`), headed “Functions and features ideas,” and ends “we did the stuff already above.” It contains no separate bug list. Read on 2026-09-28 UTC (2026-09-27 Pacific).

Repository comparison: commit `231daed`, v32 source. Architecture references are `AGENTS.md`, `OsuAimAnalyzer/README.md`, and version notes, checked against code. No separate architecture document existed. These specifications do not change the Google Doc, application, scoring, or database.

AIM IDs are permanent: keep them when reordering, append new IDs, and retain retired IDs with a replacement reference. AIM-001 through AIM-007 cover the source; AIM-008 is a separately identified review finding. “Implemented” means present and connected in source, not runtime-verified. No build, replay import, or UI test was performed for this planning task.

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
- No new scoring formula, training system, cloud service, UI framework, or database redesign is requested. A literal frame-verified shake-off feature would exceed today's summary-metric diagnostic contract and is deferred, not silently specified as approved work.

## Refactoring before further implementation

First add characterization coverage around the pure diagnostic functions in a separate test project. Then make per-play versus historical aggregation explicit and carry play identity in derived streak results (AIM-008). Reuse the same diagnosis results across UI consumers where useful; do not rewrite the large `MainForm` wholesale. Keep rule changes separate from aggregation fixes so changed labels are attributable. Existing telemetry supports the initial work without a database migration.
