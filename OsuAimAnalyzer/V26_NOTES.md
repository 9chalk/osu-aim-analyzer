# v26 — trajectory-cause diagnostics

## Direction is not cause

The existing error classes answer **where the cursor ended relative to the incoming jump axis**: Overaim, Underaim, Lateral, Correction, Plain error or Clean. v26 keeps that layer intact and adds a separate **likely movement cause** layer.

The cause layer consumes telemetry already calculated from the cursor trajectory for each transition:

- Landing / centering
- Arrival timing
- Stability / shake
- Braking / deceleration
- Straightness
- Ideal-path match
- signed axial and lateral center error
- existing correction classification

No scoring weights are changed. The cause model is heuristic diagnostic interpretation and is labeled as likely / inferred in the UI.

## Cause classes

- **Shake-off** — target acquisition was comparatively good, but final settling/stability degraded and the cursor moved away before the hit.
- **Braking overshoot** — the cursor carried excessive motion through the target and finished beyond center.
- **Stopped short** — movement amplitude ended short of target center.
- **Late acquisition** — controlled target acquisition occurred too late.
- **Correction loop** — the cursor crossed the target axis and reversed/corrected again near the hit.
- **Curved approach** — straightness and ideal-path efficiency both indicate an unnecessarily bent/wandering approach.
- **Lateral drift** — displacement is primarily sideways to the incoming jump axis.
- **Unstable braking** — the final slowdown is the strongest failure signature even when direction is not a clean over/under case.
- **General imprecision** — the landing is meaningfully off-center without one stronger timing, settling, path or braking signature.

## Repeated-pattern detection

Consecutive transitions with the same likely cause are grouped into streaks. Three or more consecutive events are called out explicitly. This makes patterns like several shake-offs in one fast section read as a section-level settling/control failure rather than a list of isolated directional errors.

## UI

### Dashboard recent / selected play

A new **Why control broke** panel sits between the song timeline and landing-direction profile. It shows the primary likely cause, share of diagnosed problem jumps, cause distribution, and longest repeated-cause streak. The Errors text tab now leads with movement cause before directional landing statistics.

### Double-click play diagnostics

The same cause panel appears in the detailed play window. Object diagnostics add Likely cause and Cause confidence columns; hovering those cells shows the per-transition explanation. Top Error cursor-path reconstruction also labels each visualized transition with its likely cause and explanation.

### Aim Analysis

The lifetime snapshot is now three-way: Aim fingerprint / landing-direction map / movement-cause profile. The summary card reports **Dominant cause**. Advanced breakdown keeps the directional error graph and adds a separate movement-cause history graph for the five most common causes in the selected history window.

## Compatibility

v26 adds no database columns and does not change scoring. Existing history is diagnosable immediately from the stored transition mechanics, so old plays do not need to be re-imported.
