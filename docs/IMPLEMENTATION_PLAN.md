# Implementation Plan

## Scope and ordering

This is a proposed future plan for [PRODUCT_SPEC.md](PRODUCT_SPEC.md), based on the complete owner-confirmed Google Doc and repository commit `231daed`. No implementation is authorized by creating this plan. The source was refreshed on 2026-09-28 UTC; see PRODUCT_SPEC.md AIM-009–AIM-017 and TOOLKIT_V1_3_PORTING_AUDIT.md. The original diagnostics requests are largely present. The newly read supplemental-map requests are not implemented; their Toolkit-derived TG batches below complement this original B-series plan. Stable AIM IDs are defined in the specification.

Dependency order: **B1 → B2 → B3 → B4**. B4 is conditional on a requirements decision, not an automatic follow-on. Each batch must leave a buildable Windows application. No broad architecture rewrite or scoring recalibration is required.

## B1 — Characterize existing diagnostics and establish a regression baseline

**Recommended first implementation batch.** Covers AIM-001–AIM-007; prepares AIM-008.

- Add a separate test project referencing the existing application assembly; choose and document one test framework (none is currently configured).
- Use synthetic `TransitionMetric` fixtures for clean control, shake-off, late acquisition, directional errors, cause ties, empty input, repeated causes, and clean/cause changes breaking a sequence.
- Characterize existing single-play thresholds and count denominators. Record the distinction between two-sample profile streaks and three-sample prose summaries without changing either yet.
- Demonstrate AIM-008 with overlapping times and object indices from different `PlayId` values. Keep the known defect explicitly documented; do not encode its incorrect output as the desired product contract. Land the permanent failing-then-passing regression with B2.
- Capture source-level scoring invariants and a manual smoke checklist for Recent Play, detailed diagnostics, and history filtering. Use synthetic or permissioned small assets, never a private replay-history dump.

**Dependencies/refactoring:** Existing model and diagnostic functions are public; start with a test project reference. Extract a small pure boundary only if compilation/testability proves it necessary. Do not move the whole app into a new architecture.

**Risk:** Low/medium; the Windows target requires a Windows test environment. Synthetic coverage characterizes rules but does not establish real-world classification accuracy.

**Exit:** Application and tests build; new characterization tests pass; mixed-play defect has a documented reproducer; no production code behavior changes or scoring changes. Record actual results rather than assuming the baseline already compiles.

## B2 — Correct play-scoped repeated-pattern aggregation

Covers AIM-008 and the remaining correctness work in AIM-004/AIM-007. Requires B1.

- Separate within-play streak discovery from cross-play counts and summary selection.
- Group by `PlayId`, order transitions consistently within each play, and ensure adjacency cannot cross a play boundary. Preserve existing gap thresholds initially; do not conflate a correctness fix with a new definition of “in a row.”
- Add owning play identity to the derived streak result and update consumers so a historical object range is not presented without play context.
- Preserve per-transition diagnoses, production proficiency, aim performance, persisted telemetry, and directional statistics.

**Refactoring:** Limit changes to diagnostic aggregation/result types and direct consumers in `AimCauseProfileControl`, `MainForm`, `PlayInsightBuilder`, and `AdvancedDiagnosticsForm`. No database migration is expected for derived results.

**Risk:** Medium: output contracts and historical summaries change. Use deterministic tie handling and test empty/missing data.

**Exit:** Regression cases prove no cross-play merging and no interleaving-induced loss of valid streaks; single-play outputs and all-history cause counts remain unchanged. A testable Windows build displays play context correctly. This is the first proposed user-visible bug-fix batch; it is not implemented now.

## B3 — Verify existing presentation end to end and fix demonstrated gaps

Covers AIM-002, AIM-005, AIM-006, AIM-007, and presentation of AIM-001/AIM-004. Requires B2 for trustworthy historical streak results.

- Exercise selection changes, no-data cases, history filters, narrow layouts, and detail-window opening with controlled fixtures or permissioned local replay/map pairs.
- Compare the same transition across Recent Play, detailed table/tooltips, and reconstructed top-error text. Compare historical counts against known fixture counts.
- Verify direction and cause remain separate and percentage denominators are understandable.
- Fix only reproduced stale-state, inconsistency, or readability failures. Shared diagnostic presentation helpers are appropriate if duplication causes a real mismatch; adding new pages is not a goal.

**Risk:** Medium: much UI coordination lives in `MainForm`, and path reconstruction depends on matching input files.

**Exit:** A build passes the B1/B2 suite and the documented smoke matrix; UI fixes have before/after evidence. If existing UI already passes, close the items as verified without cosmetic changes. Preserve scoring outputs.

## B4 — Decide whether stronger shake-off evidence is needed (conditional)

Covers the unresolved literal interpretation of AIM-003 and possible AIM-001/AIM-004 explanation refinement. Requires B1–B3 and an explicit scope decision.

**Decision:** Is the current clearly labeled inference sufficient, or should the product prove target entry followed by exit from cursor frames? Do not treat the second option as already requested in implementable detail.

If inference is sufficient, validate representative examples and close the feature without new telemetry. If exact evidence is selected:

1. Define target radius, acquisition/exit timing window, hit-time reference, allowed sample gaps, and handling of missing replay/map data.
2. Add derived acquisition/exit evidence behind the diagnostic boundary, keeping the production score independent.
3. Decide whether evidence is reconstructed on demand or persisted. Persistence requires a versioned schema/backfill policy; missing evidence in old plays must remain “unavailable,” not “clean.”
4. Test paired trajectories (acquire-and-leave, never acquire, stay centered), timing/mod transformations, sparse samples, and historical fallback before surfacing verified-event language.

**Risk:** High: precision of replay reconstruction, false certainty, and historical compatibility. Split telemetry and UI work into separate buildable changes once the decision is made.

**Exit:** Either documented acceptance of existing inference, or an explicitly scoped, tested evidence feature with honest fallback behavior. No unrelated score tuning.

## Validation commands and build discipline

From the repository root on Windows with .NET 8:

```powershell
dotnet restore OsuAimAnalyzer/OsuAimAnalyzer.csproj
dotnet build OsuAimAnalyzer/OsuAimAnalyzer.csproj
dotnet run --project OsuAimAnalyzer/OsuAimAnalyzer.csproj
```

Native test-project path and command (introduced in TG1):

```powershell
dotnet test OsuAimAnalyzer.Tests/OsuAimAnalyzer.Tests.csproj
```

Use `OsuAimAnalyzer\build-release.bat` for a release/publish smoke check when shipping a batch. Record build and test outcomes, known failures, and manual UI evidence. Run targeted checks per change rather than claiming `dotnet test` currently provides coverage. No build or test was run as part of this documentation-only task.

## Decisions and completion rules

- Use inferred causes by default; physiological explanations remain hypotheses.
- Retain current streak thresholds until minimum count and skipped-object policy are decided. The play-boundary bug does not require waiting on those threshold decisions.
- Preserve existing screens and directional labels; do not reimplement AIM-005–AIM-007 just because they appear in the source backlog.
- Keep Google Doc completion notes separate from verification status. Only mark an item verified after its acceptance checks pass.
- Commit each coherent batch separately, report behavior and validation, and update the specification without renumbering IDs. This planning change itself adds only the two Markdown files and leaves application and Google Doc content unchanged.

## Toolkit-derived feature delivery plan

This TG sequence is the authoritative schedule for the newly requested supplemental-map workflow, superseding the audit's generic T0–T5 capability roadmap. Preserve original B1–B4 diagnostics work; do not renumber AIM IDs. Toolkit code stays read-only under reference/. Audit/plan changes do not authorize implementation.

### TG1 — Shared contracts and native test foundation (IMPLEMENTED; NEEDS USER TEST)

**Scope:** Foundation for AIM-009–AIM-017, especially AIM-010–AIM-013. Reuse B1's separate native test project approach and public model boundaries. Define immutable source identity, transform options (base/effective/generated units), structured recommendation evidence and durable generation-result contracts. Characterize existing mod/AR/CS/rate math and diagnosis outputs before extracting shared helpers. Expose structured run-level diagnosis without changing the existing prose or scoring. No map writes, audio tools, schema migration or new UI.

**Reuse:** CONSOLIDATE AimTrainingDiagnosisEngine, ModUtils/AimAnalyzer helpers, BeatmapResolver and selected-play context. DIRECT REUSE existing native scripts; no additional selection reader needed. Toolkit fixtures inform tests rather than becoming runtime dependencies.

**Exit:** Windows app builds; deterministic native tests establish source identity, no mutation, clock/mod semantics and diagnostic compatibility. Document decisions still required for variant count, pitch, compensation and evidence bands. These product decisions do not block building contracts/tests. Medium refactoring risk; keep helper extraction narrow and behavior-preserving.

### TG2 — Preserving documents, resources and pure transformations

**Scope:** AIM-011–AIM-013 and parser prerequisites for AIM-016. Requires TG1. Add a preserving beatmap document separate from BeatmapData, a resource manifest and adapter to existing analysis inputs. ADAPT Toolkit retiming/spacing algorithms into pure native services. CONSOLIDATE shared formulas and parser metadata handling. Support rate 1.0/unchanged transforms first, then slowdown/stat and spacing transforms in separately buildable changes.

**Exit/tests:** Round-trip retained content, multi-BPM and inherited timing, sliders/repeats/control points, malformed/unsupported events, invariant rounding, unchanged source files, and no cached-star leakage into transformed previews. No file generation or audio dependency yet. High correctness risk: unsupported storyboard behavior must be explicit, not silently wrong.

### TG3 — Evidence-backed series planner and Recent Play preview

**Scope:** AIM-009, AIM-010, AIM-014, preview portion of AIM-015. Requires TG1/TG2; B2 is needed before using lifetime streak evidence. REIMPLEMENT the planner using structured Analyzer diagnosis and shared transforms. Target approximately five complementary variants, with recipe/count decisions resolved before final planner behavior. Show deltas, reasons, uncertainty and achieved spacing; do not publish maps yet.

**Exit/tests:** A meaningful preview build with no disk generation. Tests cover sparse/conflicting history, deterministic bounded choices, duplicate prevention, source/mod contracts, stale selection rejection and UI responsiveness. No unsupported promise that suggestions cause improvement. No initial schema migration; preview models remain in memory. Toolkit's star heuristic is optional, separately labeled and never substituted into production scoring.

### TG4 — One staged multi-difficulty export pipeline

**Scope:** AIM-009, AIM-013, AIM-014, AIM-016 and final generation action in AIM-015. Requires TG2/TG3. REIMPLEMENT exporter using a validated resource manifest and provenance manifest; retain identical title/background, fresh generated identities and only planned difficulties. First deliver a spacing/stat-only testable build without FFmpeg, then complete rate audio as TG5. Shared output supports multiple variants and uses existing resolver invalidation; no separate Songs scanner/history store.

**Exit/tests:** Valid .osz, nested resources, title/background preservation, no original-set mutation, no stale temp paths, collisions, cancellation and atomic publication. Persist provenance outside analysis tables initially; defer database migration until outcome queries actually need it. Incomplete/rate-dependent recipes are clearly unavailable in this intermediate build, never silently omitted from a promised completed series. High filesystem/identity risk.

### TG5 — Audio rendering and complete selected-map series

**Scope:** AIM-011/AIM-012 and full AIM-009/AIM-014/AIM-015. Requires TG4. ADAPT Toolkit rate/filter intent, REIMPLEMENT cancellable native process management with one explicitly selected audio binary distribution. Deduplicate audio by source content and transform options, not filename alone. Resolve pitch/stat defaults before shipping. Finish the five-variant-or-explained-fewer workflow and optional user-triggered package open.

**Exit/tests:** Audio duration/pitch policy, timestamp alignment, failure/cancel cleanup, deduplicated resources, package import smoke checks in osu!stable and unchanged original replay analysis. Production app remains one WinForms application. No Python/Pillow/Tk/mandatory tosu dependency. High integration risk; keep renderer failure independent from analysis startup.

### TG6 — Broader diagnostics generation (deferred)

AIM-017 follows a proven TG5 workflow and explicit scope decisions. Reuse planner/transforms/exporter; define map/category selection, count and outcome tracking before committing to storage changes. Merger, flips, random backgrounds and writing into existing mapsets are not prerequisites and are not scheduled by this source request.

## Dependency and change summary

TG1 → TG2 → TG3 → TG4 → TG5; TG6 is deferred. T1 from the audit (discovery/selection hardening) can be a separate later batch: selected-play generation already has a resolved map and does not require a running osu! process. Do not delay this workflow by building an unrelated song-select subsystem.

| Area | Required integration change | Storage/schema impact |
| --- | --- | --- |
| AIM-010 evidence | Structured run diagnosis and planner, preserve existing outputs | None initially; longitudinal outcomes separately scoped |
| AIM-011–AIM-013 | Preserving parser, canonical options, pure transforms | In-memory previews; generated media only at export |
| AIM-014 series | Deduplicated recipes and provenance | Versioned manifest; no duplicate analysis database |
| AIM-015 UI | One native Recent Play page and cancellable jobs | None independently |
| AIM-016 export | Staged new-set writer and resource validation | New user-selected package; original mapset untouched |
| AIM-017 general workflow | TBD after scope | TBD, not assumed |

## Repository isolation and release gate

The branch is feature/toolkit-integration, based on master at adc29a1. Ignore reference/ and the local Toolkit archive, and retain AGENTS.md's read-only boundary. Audit completion is static review, not runtime certification. No source from reference/ belongs in the planning commit. Before each future feature commit, review staged paths and confirm no reference/source archive was included. Run native build/tests appropriate to the change and report results. TG1 is IMPLEMENTED and NEEDS USER TEST. TG2 infrastructure is IMPLEMENTED with the support limits below; TG3 preview is IMPLEMENTED; see its completion record below.

## TG1 completion record — 2026-09-28 UTC

- IMPLEMENTED: immutable PracticeSourceIdentity, SerializedDifficulty, PracticeTransformOptions and PublishedPracticePackage contracts. Source identity snapshots existing resolver output; it is not another resolver. Rate is relative to source map time; played mods are retained as context, never automatically baked in. Explicit pitch/stat values avoid deciding unresolved product defaults.
- IMPLEMENTED: RunDiagnosisResult and immutable RecommendationEvidence with numeric medians/quartiles, units, counts, confidence, lift and explanation. Existing BuildRunDiagnosis delegates to the same computation and returns its unchanged text. Already-loaded and database-backed inputs share one implementation.
- IMPLEMENTED: separate Windows-targeted xUnit project; 43 cases covering snapshots, numerical semantics, validation, immutability, synthetic history, in-memory SQLite and empty/clean/evidence-bearing diagnosis. Four text snapshots were captured from the pre-refactor implementation and retained as regression fixtures.
- VERIFIED: baseline and updated application builds succeed; Release tests pass 43/43; Windows x64 self-contained single-file publish succeeds. Existing WFAC010 high-DPI warning remains, intentionally outside TG1.
- NEEDS USER TEST: real-history Recent Play and detailed Diagnosis regression smoke checks; no interactive UI verification claimed. No item is ACCEPTED.
- At TG1 completion, TG2 was NOT STARTED: no preserving parser, geometry/rate transformation, generation, audio dependency, new UI, scoring change or schema migration. B2 streak correction is still separate.

Contracts validate finite/nonnegative difficulty values and positive finite rate/spacing; supported export ranges are future validation, not silently clamped here. PublishedPracticePackage carries only an absolute package path and archive entry names; a future exporter must ensure actual publication and durable storage. Source MD5/path validation does not detect later file edits; future generation must revalidate content. Controlled evidence retains the current engine's high-percentile fallback when controlled samples are sparse. Sequence-response messages remain associations; TG1 adds no causal learning.
## TG2 completion record — 2026-09-28

**IMPLEMENTED:** BeatmapDocument.cs, BeatmapResources.cs, BeatmapTransforms.cs and the existing parser's in-memory adapter. Pure rate/stat/spacing preview operations reuse TG1 options and the analysis parser; no second parser for analysis metadata, Songs index, star cache or database is introduced. Toolkit concepts were adapted into C# without copying its runtime or exporter.

Verification: 71 native tests pass (27 new document/transform/resource cases plus one STA layout case, retaining 43 TG1 tests). Cases cover byte preservation, UTF encoding, source-file immutability, red/green timing, curve types/control points/repeats, combined transforms, culture/rounding, malformed/unsupported content, resource traversal, stale options and cached-star isolation.

Support boundary: standard maps, explicit difficulty edits, positive finite rate and spacing reductions. Slider exits are documented control-point proxies. Out-of-bounds layouts are rejected; grouping/auto-fit is not implemented. No video/storyboard retiming. Resource manifests are references with issues, not export-ready packages; implicit sample assets, external storyboards and physical filesystem validation must be resolved at TG4. Rate audio is deferred to TG5. These limits must be surfaced by TG3, not silently bypassed.

Recent Play layout fix is separate from transform work: readout tabs hide the Overview hero and fill available height; explicit layout sizing and immediate page layout cover switching/resizing. Manual visual checks remain NEEDS USER TEST. No item is ACCEPTED.

At TG2 completion, the next batch was **TG3 — Evidence-backed series planner and Recent Play preview** (implemented below). Use these services with explicit unsupported-candidate handling; resolve recipe/count/evidence policy before exposing planner results. TG4/TG5 remain prerequisites for actual playable exports.

## TG3 completion record — 2026-09-28

**IMPLEMENTED; NEEDS USER TEST.** PracticeSeriesPlanner.cs implements the user-approved up-to-five recipe preview policy, using RecommendationEvidence and BeatmapTransforms. PracticePreviewControl.cs adds the native Recent Play Practice page. PracticePreviewSession.cs owns stale-request cancellation and bounded, hash-verified source reads. MainForm coordinates existing selection, resolver and diagnosis; no second history store, source index or Toolkit runtime is added. BeatmapTransforms accepts cooperative cancellation inside its loops.

Policy details and limitations are recorded in PRODUCT_SPEC.md. Sparse evidence gets labeled conservative proposals; reliable conflicting/lower-demand evidence blocks affected reductions. No OD-specific factor exists, so the fifth recipe eases AR only when supported. Modded-play evidence is not translated into NM targets. Variants are deduplicated by transformed content, have bounded changes and explain omissions. Counts below five are expected, not silent partial completion. No scoring/DB changes or lifetime-streak evidence are used.

Verification: all 87 native tests pass, including deterministic/bounded policy, weak/invalid/conflicting evidence, DT/NC/HT/HR/EZ boundaries, unsupported-video fallback, duplicate prevention, changed/missing source, request cancellation and stale completions. STA tests exercise MainForm's real worker with synthetic SQLite/source data, retry after file change, selection reset, responsive UI dispatch and layout at three sizes. Existing diagnosis snapshots remain unchanged.

Manual gate: launch the TG3 build, select a play, open Practice, build/rebuild a preview, try cancel and another play, compare sparse/modded cases, and resize/read explanations. No item is ACCEPTED. At TG3 completion TG4 was not started; its spacing/stat export is implemented in the record below. TG5 remains required for playable slowdown exports.

## TG4 completion record — 2026-09-28

**IMPLEMENTED; NEEDS USER TEST:** PracticePackageExporter, PracticeExportPaths and PracticeExportSelectionForm integrate with the existing Practice page/session. The preserving document gains metadata upserts; shared resource inspection supports unchanged storyboard resources. Source document reads are shared and bounded. No Toolkit source/runtime, FFmpeg, schema change or production scoring change was added.

Exit coverage: selected multi-difficulty .osz output, nested/background bytes and title preservation, local IDs/unique difficulty names, provenance, original-file immutability, deterministic resource inclusion, collision refusal, cancellation/failure cleanup, source/resource freshness, path/junction containment and MainForm publish/open state. All 101 tests pass, including prior diagnosis/preview regressions. Real osu! import and media playback remain manual, not certified by synthetic asset tests.

The intermediate UI explicitly exports only the chosen rate-1 subset. The current planner normally offers Reduced spacing alone; the service supports several spacing/stat variants without duplicating pipelines. Rate recipes remain preview-only and cannot be passed through the exporter. Unsupported resources fail the whole selected set rather than produce incomplete packages. Supported resource types and size/containment limits are in PRODUCT_SPEC.md.

Next: **TG5 — Audio rendering and complete selected-map series**, NOT STARTED. Extend this one staged exporter with explicitly managed audio rendering, content/options deduplication and timestamp checks. Preserve the selected-subset/complete-series distinction until every requested rate recipe can be published. Pitch is already explicit; serialized stat/mod semantics remain the documented source-relative policy.
