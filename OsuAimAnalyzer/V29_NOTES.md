# v29 — objective diagnosis + modern run diagnostics + smoother graphs

## Error Conditions → objective diagnosis

The unused right side of **Aim analysis → Error conditions** is now an interpretation/training workspace instead of empty space.

- Existing aim presets from `DiagnosticsEngine` are reused as the player's aim-type categories.
- Categories are ranked by the player's own matching transition/run volume for the selected history window.
- Selecting an error/cause and then an aim type produces an evidence-backed diagnosis using only telemetry already stored by the analyzer.
- Each candidate contributor compares the error sample against the player's **controlled history on the same aim type**.
- The analysis uses star rating, BPM, effective AR, CS4-normalized spacing, visible density, and local aim demand.
- Every factor reports an **evidence confidence** score derived from effect-size/lift, separation from the player's controlled range, and sample support. It is explicitly not presented as a literal causal probability.
- The diagnosis translates the result into **main training volume**, **limit testing**, and **mastery target** guidance based on the production proficiency thresholds and the player's demonstrated history.

Example output can now say that late acquisition on a selected aim type is associated with spacing above the player's controlled interquartile range, quantify the percent difference and error-rate lift, then recommend moving spacing/BPM/AR toward the controlled band while keeping the other demands similar.

## Per-run diagnosis

The same comparison model is available for an individual play.

- Recent Play gets a new **Diagnosis** rail page. It is lazy-built only when opened so ordinary dashboard selection stays responsive.
- Double-click diagnostics gets a **Diagnosis** subtab using the exact current run versus the player's similar-map controlled history.
- The per-run readout says whether the exact map is appropriate for main volume, already mastered, or better treated as limit testing.

## Detailed-play UI

The old text-heavy header in the double-click diagnostics window is replaced by the same osu!-inspired performance hero used in Recent Play: map background, difficulty/mod badges, grade, production proficiency, accuracy, misses and Aim Performance. The previous plain-English summary remains available under **Overview** instead of competing with the header.

## Graph rendering

`GraphControl` and the three-lane `SongTimelineControl` now use presentation-only smoothing:

- exact analyzed points and hover/readout values remain unchanged;
- trend lines use a soft weighted curve so section-level shape is easier to read;
- lines get a faint bloom plus a lighter center stroke;
- raw play points use a separate muted point color instead of matching the trend line.

No production scoring constants or database schema changed.
