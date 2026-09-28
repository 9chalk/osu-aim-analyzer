# v21 — production tuner scoring + Replay Tools

v21 promotes the finalized osu! Aim Tuner v9 model into the standalone Aim Analyzer.

## Production scoring

The analyzer now uses the fixed `AimPerformance_SR_Anchored_v1` tuning values for normal replay analysis, database history, dashboards, diagnostics, training, and collection routing. There is no production Scoring Lab UI.

Key scoring behavior brought over from the tuner:

- difficulty-aware proficiency aggregation so sustained hard jump sections matter more than filler;
- final proficiency resolution curve (raw 750 -> displayed 550, raw 900 -> displayed 900);
- sustained hard-section Aim Performance demand;
- hard-section execution rather than whole-map execution as the base capability multiplier;
- bonus-only, duration-aware consistency;
- independent SR-derived demand floor;
- NC/DT-equivalent stable star-cache lookup and rate-based SR fallback for local/practice maps.

Existing SQLite history is automatically rescored from stored transition telemetry after the scoring-version bump. Transition/component scores stay on the raw mechanical 0–1000 scale; only aggregated play proficiency receives the final resolution remap.

## Replay Tools

A new top-level **Replay tools** workspace contains the useful inspection/stress-test features from Aim Tuner v9, without Scoring Lab:

- **Replay test** — import one or many `.osr` files, browse local `Data\\r` replay headers, cache tests, compare the legacy analyzer calculation against current production scoring, and inspect per-transition deltas.
- **Quick checker** — select/auto-detect a highlighted `.osu` difficulty, sweep synthetic raw-proficiency and miss-count cases through the production Aim Performance model, and save synthetic tests into the replay-test cache.
- **Debug visualizer** — inspect player cursor movement against the ideal minimum-jerk path and view raw/adjusted scoring inputs for a selected jump.

Quick Checker can optionally install the same osu!stable song-select reader used by the toolkit. The native reader DLLs are not bundled; `setup-song-select-reader.bat` / `setup_native.ps1` download them when explicitly run. Manual `.osu` selection remains available without the native reader.

## Collection defaults

Because the final proficiency resolution curve intentionally expands the old raw 750–900 region, the stock analyzer-managed collection bands now begin at 500 instead of 700. Exact untouched legacy default bands are migrated automatically; customized rules are preserved.
