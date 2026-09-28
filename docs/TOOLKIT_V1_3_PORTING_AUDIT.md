# Toolkit v1.3 Porting Audit

## Scope, provenance, and limits

Production is `OsuAimAnalyzer/`; `reference/osu-toolkit-1.4/` is read-only reference material after extraction. The supplied ZIP is `osu_toolkit_unified_v1_3_source(1).zip`. Despite the requested destination name, its README identifies **osu! Toolkit 1.3** and its changelog ends at 1.3. This audit applies to that actual source, not an independently verified 1.4 release.

Comparison baseline: Analyzer commit `adc29a1`, `AGENTS.md`, `docs/PRODUCT_SPEC.md`, and `docs/IMPLEMENTATION_PLAN.md`. Toolkit entry point is `OsuToolkit.pyw` → `App`, with `TrainerTab`, `MergerTab`, and `SpacingTab`; cores are Python modules. Analyzer is one .NET 8 WinForms application with SQLite persistence. Findings are static source review, not executed feature tests. No reference scripts, installers, exporters, or tests were run; no production behavior was changed.

Disposition terminology: **retain** means use Analyzer's existing implementation; **adapt algorithm** means translate isolated behavior to C# with tests and native UI; **rewrite integration** means preserve useful behavior but redesign ownership, I/O, or lifecycle. None of the Python modules is a direct C# drop-in.

## 1. Selected-map detection and polling

**Toolkit:** `merger_core.py` provides `read_osu_memory`, `_parse_reader_json`, `resolve_selected_osu`, `get_current_from_native`, and `get_current_selection`. `OsuToolkit.pyw` exposes `capture_current_osu_path`, `MapPicker.capture_current`, and `TrainerTab._auto_tick`. Native PowerShell capture reads the selected filename, folder, process path, and PID. Merger capture additionally loads map/audio bytes into `MapSnapshot`; optional tosu fallback uses `/json/v2` and file endpoints.

**Analyzer equivalent:** `SelectedBeatmapReader.TryReadAsync`, `SelectedBeatmapInfo`, and `ReplayToolsForm.DetectSelectedMapAsync`; the Quick Checker already has auto-detection and a 1,400 ms timer. Both `memory_reader.ps1` files have identical SHA-256 hashes; both `setup_native.ps1` files also match exactly.

**Decision:** Retain the existing reader and dependency installation. Adapt Toolkit's robust last-valid-JSON parsing, bounded subprocess timeout, and actionable failure details. Rewrite lifecycle as one shared selection service if more Analyzer views subscribe: cancellation must terminate owned helper processes, output streams should be drained safely, overlapping requests should be prevented, and stale results rejected. Do not create a reader per feature.

**Avoid/conflicts:** No Python runtime or mandatory tosu server. No audio loading just to identify a selected map. Toolkit returns a merger snapshot where Analyzer needs a file identity and metadata. Treat unsupported/lost selection distinctly from a successful stale selection. Reuse `BeatmapResolver`, manual map selection, and existing UI status surfaces.

**Validation:** Native unavailable, malformed/noisy output, timeout, rapid map changes, multiple installs, and same-named difficulties in different folders.

## 2. Installation and Songs-folder discovery

**Toolkit:** `_read_beatmap_directory_from_cfg`, `resolve_selected_osu`, and `get_running_osu_paths` in `merger_core.py`. Derives installation from the running process and reads `BeatmapDirectory` from recent `osu!.*.cfg` files, expanding environment variables and relative paths. Falls back to `Songs` and recursive filename search.

**Analyzer equivalent:** `AppSettings.FillDefaults` uses `%LOCALAPPDATA%/osu!`, then installation-relative `Songs` and `Data/r`; settings permit explicit directories. `SelectedBeatmapReader` receives configured SongsDirectory and returns process metadata but does not use that metadata for config-based discovery. `BeatmapResolver` already indexes osu!.db, hashes fallback paths, caches parsed maps, and watches Songs changes.

**Decision:** Adapt config-path discovery into a read-only C# discovery service feeding existing settings/resolver. Suggested precedence: explicit valid user selection, matching running installation/config, then existing default; report disagreements rather than silently overwriting settings. Reconfigure dependent watchers together when a root changes.

**Avoid/conflicts:** Toolkit's `get_running_osu_paths` calls `songs.mkdir`; discovery must not create folders. Do not copy first-match filename fallback as authoritative identity or run a recursive scan every poll. Reuse the resolver/index and verify folder/hash when ambiguity exists. Neither source establishes a universal registry-based or lazer discovery solution.

**Risk/validation:** Medium. Test absolute/relative/custom paths, multiple config files, quoted/environment paths, absent directories, portable installs, and explicit override precedence.

## 3. Beatmap parsing and representation

**Toolkit:** `merger_core.decode_osu`, `parse_sections`, `kv_section`, `TimingPoint`, `MapSnapshot`, `hitobject_end`, and timing-preserving helpers; `spacing_core.HitObject` and `BeatmapDocument` retain original lines and slider/control-point information. `trainer_core.load_map_info` extracts editable difficulty/audio/background metadata.

**Analyzer equivalent:** `BeatmapParser.Parse`, `BeatmapData`, `HitObjectData`, and `TimingPointData` are analysis projections. Hit objects retain head coordinates, time, index, and kind; this is insufficient to serialize complete sliders or preserve arbitrary file sections. `OsuDbReader` adds cached metadata and mod-specific stars.

**Decision:** Retain Analyzer's analysis-facing model. Before any map writer, add a separate document-preserving representation and an adapter to `BeatmapData`. Adapt parsing/retiming algorithms and tests; do not replace the analysis parser wholesale or serialize its reduced model back into a source map.

**Avoid/conflicts:** No second independent Songs index or Toolkit snapshot cache. Do not pull `merger_core` into production just for parsing: it also contains UI, network, audio, and export concerns. Preserve source encoding behavior, unknown sections, hit sounds, slider parameters, breaks, and IDs according to each operation's contract.

**Risk/validation:** High for write paths. Round-trip fixtures must cover sliders/repeats, spinners, inherited timing, comments, unknown sections, and non-ASCII metadata. Existing Analyzer telemetry and scores must remain unchanged for unmodified input.

## 4. Practice-map rate and difficulty edits

**Toolkit:** `trainer_core.build_practice_osu`, `_retime_hitobject`, `_retime_timing`, `_retime_event`, `_retime_bookmarks`, `scaled_ar`, `scaled_od`, `generate_audio`, and `export_practice`. Supports rate/BPM, HP/CS/AR/OD, optional stat scaling, spinner removal, pitch behavior, and mapset/package outputs. UI entry is `TrainerTab.generate`.

**Analyzer equivalent:** `ModUtils`, `AimAnalyzer.EffectiveApproachRate`, and `QuickCheckEngine` interpret mods/difficulty but do not author practice maps. `TrainingEngine` recommends training; it is not a map generator.

**Decision:** Adapt pure transforms; rewrite export orchestration and UI. Use immutable transformation options and separate preview, render, and export stages. Reuse existing settings, selection, resolver, and score presentation instead of embedding a second standalone Trainer app.

**Conflicts:** A generated rate-adjusted map changes content/hash; it is not the original replay with a DT flag. Record source-to-generated provenance separately from original beatmap identity. Retimed map time and replay/mod clock time must not be scaled twice. Scope initial authoring to osu!standard even though Toolkit trainer permits other modes.

**Avoid:** Python subprocess bridge, Toolkit settings file, and FFmpeg dependency for metadata-only previews. Add audio tooling only when rate-changing generation is explicitly scheduled.

**Risk/validation:** High. Test all timestamp-bearing sections, negative/sentinel values, inherited slider velocity, rounding, pitch modes, missing audio, cancellation, and rate 1.0 preservation.

## 5. Difficulty estimates, projections, and mod semantics

**Toolkit:** `trainer_core.estimate_projected_stars_from_objects` uses head positions/times/types, CS-normalized spacing, velocity/density/angle strains, weighted peaks, and AR/OD/object-count adjustments; it clamps output to 0–15. `_difficulty_objects` caches samples by path/mtime. `spacing_core.estimate_projected_stars` applies the actual spacing transform before estimating. UI updates are debounced and use sequence checks.

**Analyzer equivalent:** `AimAnalyzer.ResolveStarRating` prefers exact/normalized/equivalent stable cache values, then rate-adjusted/base and local fallbacks. `QuickCheckEngine.Build` uses Analyzer semantics for projections; `PpUtils` is an approximate pp model. `ProductionScoring` is the production scoring authority.

**Decision:** Reuse the concept of previewing the transformed geometry, not Toolkit's estimate as a replacement score. If desired, translate the heuristic behind a separately labeled experimental projection provider with algorithm/version/source metadata. Use one preview computation service and reuse Analyzer's existing calculator where it accepts the required inputs.

**Conflicts:** Toolkit projected stars are not official difficulty, pp, proficiency, or Aim Performance. They must not silently replace stored or cached stars. Continuous practice rate is not a mod bitmask. Toolkit's AR inverse chooses 0.1 steps; Analyzer's effective AR is continuous. Keep UI snapping outside canonical math. HR circle-size emulation is not full HR, which also affects other stats and vertical coordinates; spacing flips are separate transformations.

**Avoid:** Duplicate mod constants or conflicting rate/AR/OD helpers. Centralize shared time/radius formulas with explicit base, effective, and generated-stat meanings; do not change current scoring as part of extraction.

**Risk/validation:** Medium/high. Test NM/DT/NC/HT/HR/EZ interpretation, rate 1.0, rounding boundaries, original versus transformed coordinates, stale-preview rejection, and provenance labels. No new difficulty package is required by the supplied Toolkit heuristic.

## 6. Pattern-preserving spacing and flips

**Toolkit:** `spacing_core.apply_pattern_transform`, `_build_layout`, `_find_group_end`, `_find_closest_fitting_multiplier`, `_best_translation`, `try_translate_and_fit`, `BeatmapDocument.transform`, and `TransformResult`. Groups nearby objects, scales spacing while fitting patterns inside bounds, handles slider geometry, and reports actual spacing/capped/repositioned groups. `SpacingTab.generate` exports either beside the map or as a new package.

**Analyzer equivalent:** Computes normalized spacing and displays paths but has no equivalent pattern-editing/export subsystem.

**Decision:** High-value algorithm port to pure C# geometry with new WinForms UI. Depend on the preserving document model and expose requested versus actual transformation statistics. Preview and export must use the same transformed document, not divergent implementations.

**Avoid/conflicts:** No Python geometry runtime or parallel drawing framework. Existing `HitObjectData` loses slider geometry; do not transform only its heads and claim a complete map edit. Avoid double-applying HR mirroring and an explicit vertical flip.

**Risk/validation:** High. Translate `tests/test_spacing.py` cases; verify bounds, slider shapes, close patterns, caps, flips, and source immutability. Decide explicitly whether repeated generation starts from source or already transformed data.

## 7. Backgrounds and mapset resources

**Toolkit:** `trainer_core._find_background` parses CSV Events fields (including named Background events); `TrainerTab.show_background` renders with Pillow. `merger_core.choose_random_background_from_songs` samples top-level map folders before broader searching; v1.3 moves selection off the UI thread. Merger also supports explicit/no backgrounds and generated titles via `mash_title_from_snaps`.

**Analyzer equivalent:** `BeatmapParser` already populates `BackgroundPath`, but uses comma splitting and a narrower `0,0,` pattern. Existing application uses WinForms/System.Drawing; no Pillow is needed.

**Decision:** Adapt robust quoted CSV event parsing and relative resource resolution into shared metadata infrastructure. Use native image loading with clear ownership/disposal and bounded thumbnail caching. Random background selection and title mashups are optional generation conveniences, not prerequisites for selected-map features.

**Avoid/conflicts:** No recursive image scan on UI/polling paths, second mapset index, or silent replacement of an existing mapset's assets. Validate resource paths against the intended mapset root; do not assume arbitrary event filenames are safe package entries.

**Risk/validation:** Low/medium for preview, medium for packaging: commas/quotes, nested paths, missing/corrupt images, large files, and selection changes during load.

## 8. Merger timing, audio, and fades

**Toolkit:** `MapSnapshot`, `gameplay_bounds_for_snapshot`, `plan_object_gap_clips`, `calculate_map_offsets`, `concatenate_wav_clips`, `segment_timing_lines`, `make_merged_osu`, `validate_merged_osu`, `_prepare_merge_artifacts`, and export functions in `merger_core.py`. Preserves an object-to-object gap model, crops/prerolls audio, shifts timing/objects/breaks, and applies transition fades. `MergerTab.export` is the unified UI entry; `merger_core.App` is a legacy standalone UI, not needed.

**Analyzer equivalent:** No marathon-map merger or audio rendering pipeline. `SongTimelineBuilder` is diagnostic visualization, not an audio editing engine.

**Decision:** Defer until practice export infrastructure exists. Adapt the timing planner and validation rules; rewrite audio/process orchestration as a cancellable C# service. Reuse selection, preserving documents, package writer, and job reporting from earlier features.

**Avoid/conflicts:** Do not embed the legacy UI, tosu capture/audio transport, Python WAV handling stack, or entire `merger_core` module. A merged map has new identity and cannot reuse constituent replay telemetry. Keep fades separate from timestamp alignment.

**Risk/validation:** High. Port behavioral cases from `test_merger_core.py`, `test_merger_transition.py`, and `test_fades.py`; check slider/spinner ends, inherited timing, exact requested gaps, silence/clipping boundaries, and zero/nonzero fades.

## 9. Export safety and generated-map identity

**Toolkit:** `trainer_core.export_practice`, spacing export functions, and merger `export_package`/`export_into_existing_mapset` manage .osu/.osz and media. Merger uses unique output assets; Trainer and Spacing have different overwrite/reuse policies. Spacing may replace a generated difficulty; Trainer may reuse an existing rate-named audio file and writes map text before generating audio. Trainer packaging iterates immediate source-directory files, which is not a complete nested-resource manifest.

**Analyzer equivalent:** Resolver hashes/cache/watcher and collection routing already track maps; no common practice-map exporter exists.

**Decision:** Rewrite as one shared export service. Stage all artifacts, validate references, then publish; define collision and cleanup policy explicitly. Preserve source files, use content/options-aware audio reuse, and support nested resources deliberately. Refresh existing resolver infrastructure after successful generation. Retain original-to-generated provenance without impersonating the original hash or attaching its replay history.

**Avoid/conflicts:** Do not copy generated-marker overwrite rules or timestamp naming as universal safety guarantees. No second collection writer or historical database. Existing-mapset and new-mapset metadata/ID policies must be distinct.

**Risk/validation:** High: repeated generation, partial audio failures, cancellation, file locks, nested assets, filename collisions, and no source-file mutation. `test_unique_export.py` and `test_v12_features.py` are useful behavioral references, not proof all export paths are safe.

## 10. Shared utilities, UI jobs, tests, and dependencies

**Reusable behavior:** Explicit away-from-zero millisecond rounding, invariant numeric serialization, path-component sanitation, unit conversions, parser fixtures, and deterministic geometry/timing tests. Prefer existing .NET primitives where their semantics match; test rounding differences rather than mechanically translating Python `round`.

**Job patterns:** `TrainerTab`/`SpacingTab` debounce projections and reject stale sequence numbers. `MergerTab._update_length` serializes duration probes; `_probe_audio_duration_ms` caches durations. Reimplement with tasks/cancellation and UI-thread marshaling, not Tk `after`, daemon threads, or a second event loop. Keys must include content/options and changing files must invalidate cached results.

**Direct code reuse:** The two PowerShell helpers already exist identically in Analyzer. Retain them in place; there is nothing to duplicate. Python tests can guide C# fixtures and expected values; do not claim the Python test suite validates the C# port. `TEST_RESULTS.txt` records historical results only.

**Dependencies to avoid:** Python interpreter/venv, Tkinter/custom canvas widgets, Pillow, PyInstaller/build scripts, `imageio-ffmpeg` as a Python package, mandatory tosu HTTP service, and Toolkit theme/settings persistence. Reuse Analyzer `ToolkitUi`, `Theme`, `AppSettings`, resolver, and existing optional native reader packages. If audio generation is approved, evaluate one explicit FFmpeg distribution/process boundary separately; it is unnecessary for read-only selection, metadata, or geometry previews.

**Attribution inventory:** The reference includes GPLv3 license text; its `THIRD_PARTY.md` records native-package versions, upstream origins, and audio/image dependencies. Preserve provenance when adapting code and review distribution requirements before shipping copied components. This is an inventory of local notices, not a legal determination or a reason to add dependencies now.

## Recommended porting order

| Batch | Scope | Dependency and testable outcome |
| --- | --- | --- |
| T0 | Confirm version provenance; add synthetic compatibility fixtures and shared-contract tests | No behavior change; document 1.3 source under the requested 1.4 directory. Complements existing B1 diagnostics baseline. |
| T1 | Shared selection lifecycle and custom Songs discovery | Existing reader/resolver; test timeout, ambiguity, config paths, and explicit settings precedence. No new native packages. |
| T2 | Preserving beatmap document, resource metadata, shared rate/mod math boundaries | T1 for integration; round-trip and regression tests before any writer; preserve existing Analyzer outputs. |
| T3 | Read-only transformed preview and clearly sourced projections | T2; geometry/stat changes in memory, cancellation and stale-result tests; no saved-map or score changes. |
| T4 | Practice-map and spacing generation with common export service | T2/T3; staged writes, collision/provenance tests; add audio dependency only for rate renders. Split metadata/spacing and audio work into separate buildable changes. |
| T5 | Merger, fades, optional random backgrounds/title helpers | T4; exact-gap/timing/audio integration tests and no overwrites. Lowest initial priority. |

Keep diagnostics AIM-001–AIM-008 distinct from the new practice-generation requirements AIM-009–AIM-016. The latest Google Doc now explicitly requests selected-map practice generation; the mapping and dependency order in PRODUCT_SPEC.md and IMPLEMENTATION_PLAN.md supersede the generic roadmap above. Merger and general standalone tools remain optional, not requested implementation. Prefer T1 as the first user-facing Toolkit-derived enhancement; do T0 validation first. No features, dependencies, or production code have been added by this audit.

## Detailed review addendum

### Actual source location and coverage

The later requested path `reference/osu_toolkit_unified_v1_3_source` does not exist in this checkout. The previously authorized extraction placed the v1.3 archive under `reference/osu-toolkit-1.4`. That tree was inspected without renaming or writing to it. It includes the unified UI, three core modules, native reader/setup scripts, launch/build/diagnostic scripts, attribution, historical test results, eight Python test files, and two icon assets. The unified entry point is `OsuToolkit.pyw`; `merger_core.App` is a second legacy entry point, not a service to embed. No claims are made about an unavailable newer release.

### Process and window assumptions

`memory_reader.ps1` chooses the first `Get-Process -Name "osu!"` result and uses reflection to construct `StructuredOsuMemoryReader`, including singleton, parameterless, and ProcessTargetOptions variants. It resolves the generic TryRead method and emits diagnostic JSON with `ok`, `readOk`, filename, folder, PID, and process path. A nonempty filename is not proof that a fresh read succeeded: consumers should explicitly consider `readOk`. Multiple processes can make the separately chosen process path and reader target ambiguous. This is stable memory integration, not foreground-window detection, OCR, a lazer adapter, or automatic map opening. Shell open of exported packages and Explorer are separate actions. Preserve manual selection and require coherent PID/path association; do not add window hooks without a requirement.

### Slider and pattern assumptions

`spacing_core.HitObject.parse` preserves fields, curve type, repeat count, and control points. `original_exit`/`edited_exit` approximate an odd-repeat slider exit as its last control point and even repeats as its head. They do not evaluate the rendered curve at its declared pixel length. `_build_layout` translates each slider's control points rigidly while scaling inter-object jumps above the close-jump threshold; it does not uniformly scale slider length. `_find_group_end` uses size/time boundaries and keeps close jumps together. `_find_closest_fitting_multiplier` performs 48 fitting iterations; `_apply_fallback` can clamp individual control points when rigid fitting fails, potentially distorting a slider. These are editing heuristics, not validated gameplay slider-path analysis.

Analyzer's reduced hit-object model cannot express these edits. Preserve shape and length where possible, expose capped/repositioned groups, and reject or explicitly flag destructive fallback instead of silently copying it. Test odd/even repeats, declared-length versus control-point endpoints, curved sliders, out-of-bounds controls, close streams, spinners, and input immutability. Do not reuse pattern grouping as evidence of a player's error cause or as a substitute for Analyzer's trajectory diagnostics.

### Parsing, transformation, and validation limitations

Trainer changes object times, spinner/hold ends, red timing beat lengths, timing offsets, breaks/video offsets, preview/audio-leadin, and bookmarks. It preserves green-line beat lengths. This is useful behavior to translate, but `_retime_event` is not a complete storyboard command retimer, and malformed fields can fall through unchanged. A generation service needs an explicit unsupported-content policy; preserving a storyboard verbatim while changing rate may desynchronize it.

`validate_merged_osu` checks nonempty objects/timing, eight timing fields, finite numeric timing values, red/green beat-length signs, red-line availability/order, chronological integer object times, and integer spinner/hold ends. Adapt its validation principles into a shared output validator. Its strict merged-output contract is not automatically the correct acceptance policy for every existing source file.

`plan_object_gap_clips` derives clips from playable bounds, keeps the first intro and final outro, caps incoming preroll at the requested gap, and rejects gameplay truncation. This algorithm is worth preserving if merger work is later requested. It is unnecessary for five variants of one selected map.

### Packaging and lifecycle risks

`spacing_core.generate_new_mapset_package` reloads the source to avoid accumulating transforms and verifies ZIP integrity, but copies only top-level non-.osu assets. Its returned `difficulty_path` points into a temporary directory that is deleted on return; callers must use `package_path`. A native result contract should only expose durable artifacts. Trainer package export can carry unrelated source difficulties; neither exporter directly implements five coordinated variants in one separate set with unchanged visible song title.

For the requested workflow, build a manifest of referenced audio, background, hitsounds, and nested assets; reset generated online identities while keeping source provenance separately. Deduplicate equivalent audio renders across variants. Do not append Toolkit's default “Spacing Practice” title suffix when the user requests an identical song name. Do not import unrelated difficulties, overwrite originals, or reuse stale audio merely because a filename exists.

### Tests and dependency boundaries

Existing test scripts cover trainer transforms/audio, spacing/flips, merger timing/fades, unique exports, v1.2 features, and v1.3 responsiveness. Some are top-level Python assertions rather than discoverable unittest methods. `test_v13_regressions.py` specifically forbids a full Songs walk during a fast background selection and verifies duration-probe cache reuse. Translate useful assertions into native tests; historical TEST_RESULTS is not present-run verification.

Python cores are coupled: trainer imports merger, spacing imports trainer, and merger contains Tk/UI and FFmpeg/network utilities. Importing a core wholesale brings unrelated responsibilities. Extract concepts into native services instead. `imageio-ffmpeg` and Pillow serve audio distribution and image rendering, not diagnostic intelligence. Neither Toolkit nor Analyzer currently provides a validated longitudinal causal model of “what improves this player”; that is new recommendation logic, not reusable Toolkit functionality.

### Consolidation owners

| Concern | Existing owner to retain/generalize | New boundary only where needed |
| --- | --- | --- |
| Installed paths and user choices | AppSettings / AppPaths | Read-only installation discovery feeding existing settings |
| Selected map and native helper | SelectedBeatmapReader | One cancellable selection coordinator |
| Beatmap identity/cache/watchers | BeatmapResolver / OsuDbReader | Resolve-by-path adapter and explicit invalidation |
| Analysis inputs | BeatmapParser / BeatmapData | Preserving BeatmapDocument for writing, adapted to existing model |
| Mod/rate math | ModUtils / AimAnalyzer helpers | Explicit transformation options and common unit conversions |
| Diagnosis/history | AimTrainingDiagnosisEngine / AnalyzerDatabase | Structured recommendation evidence; no duplicate history store |
| Presentation | MainForm / ToolkitUi / Theme | Native inspector page and shared background jobs |
| Map generation | No existing equivalent | Pure transforms, staged package exporter, optional audio renderer |

Use the current implementation plan's TG batches for the newly requested supplemental-map feature. T0–T5 above remain a capability roadmap, not a second competing implementation schedule.
