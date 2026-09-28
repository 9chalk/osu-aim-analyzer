# v24 — lifetime aim analysis + streamlined navigation

## Navigation

Diagnostics and Training are hidden from the top-level tab bar. Their implementation remains in `MainForm.cs`, `DiagnosticsEngine.cs`, and `TrainingEngine.cs`; this release only removes them from the visible navigation.

Visible workspaces:

- Dashboard
- Aim analysis
- Replay tools
- Collections
- Settings / Import

## Aim analysis rebuild

The old radar/profile view is replaced by a scrollable history dashboard that matches the visual hierarchy of the v23 recent-play performance panel.

### History graphs

1. Aim Performance over time.
2. Production proficiency over time.
3. Center error over time, measured in target-circle radii (`R`): mean, median and P90.
4. Error-class share over time: Overaim, Underaim, Lateral, Correction and Clean.
5. All six production proficiency components on one timeline:
   - Landing / centering
   - Arrival timing
   - Straightness
   - Braking / deceleration
   - Stability / shake control
   - Ideal-path match

The History selector supports All time / 30 days / 7 days / Today / Latest session. Timeline resolution can be play-by-play or daily average.

### Mechanical rundown

The bottom table shows each production component's base scoring weight, selected-history average, latest-20-play average, delta, and a plain-English definition. Center error and derived Aim Tension are included as diagnostic rows even though they are not direct base-weight entries.

## Data source

`LifetimeAimAnalysisBuilder` loads stored transition telemetry once for the selected history window. Center-error history is computed from `AxialError` + `LateralError`; all other map-level trend values use the analyzer's stored production play metrics. No scoring constants or database schema changed.
