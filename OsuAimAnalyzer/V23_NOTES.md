# v23 — build hotfix + landing-error visualization

## Build fix

The v22 build failed at `SongTimelineControl.cs` because a null-coalescing expression tried to choose between `List<SongTimelinePoint>` and `SongTimelinePoint[]`. v23 replaces that expression with an explicit null branch, keeping the field typed as `IReadOnlyList<SongTimelinePoint>`.

## Landing error profile

The Dashboard recent/selected-play rail and the double-click diagnostics window now show a normalized landing-error scatter plot. The analyzer already stores each transition's center error as `AxialError` and `LateralError`, measured in target-circle-radius units. v23 rotates those errors into the same frame for every jump: incoming movement is left-to-right, so negative axial error reads as underaim, positive axial error as overaim, and the perpendicular axis reads as lateral error.

The visualization shows:

- one dot per analyzed transition (downsampled only for rendering on very long maps);
- the actual hit-circle boundary at `1.0R`;
- a faint outer `1.4R` guide for near misses;
- a dotted ring for average absolute center error;
- a mean signed-bias arrow;
- average, median and P90 center error;
- error-class percentages for Overaim, Underaim, Lateral, Correction, Plain error and Clean.

The Errors text in the recent-play tabs and the detailed plain-English summary also report average center error and mean axial/lateral bias numerically.

## Layout

The dashboard right rail now starts at roughly 42% of the dashboard workspace width instead of 36%, while the splitter remains draggable. The detailed diagnostics window opens larger and is scrollable at smaller window sizes.

## Scoring

No production scoring constants or database schema changed. The new profile is purely a visualization of transition telemetry already stored by the analyzer.
