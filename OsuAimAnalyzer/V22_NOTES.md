# v22 — song-position proficiency / performance / difficulty graphs

v22 adds a synchronized per-song analysis layer without changing the v21 production scoring profile.

## Three aligned local curves

Both the Dashboard recent/selected-play inspector and the detailed double-click diagnostics window now show three lanes on one song-time axis:

1. **Proficiency** — a centered local window of transition scores is aggregated with the exact v21 production difficulty weighting and proficiency resolution remap.
2. **Aim performance** — the same local window is run through the production hard-section execution model. Its local telemetry-demand shape is preserved, while one whole-play calibration factor carries the replay's SR anchoring and consistency level into the curve so the y-values remain comparable with the play's displayed Aim Performance.
3. **Map difficulty** — local telemetry demand from the production Aim Performance model (cursor velocity, spacing, angle/reversal and visible-object density), shown as a multiplier. `1.00×` is the production model's reference demand; this is not presented as official section star rating.

The local window is centered around each transition (up to eight transitions on either side, limited to roughly 3.2 seconds each direction). This removes unreadable per-object noise while keeping short breakdowns and difficulty spikes visible. Extremely long maps are envelope-downsampled for rendering, preserving local proficiency lows and difficulty highs. Long song breaks are displayed as gaps.

Hovering anywhere on the timeline locks one vertical crosshair across all three lanes and reports song time, object number, local proficiency, local Aim Performance, local difficulty, BPM, spacing and error class.

## Dashboard recent-play redesign

The selected-play pane is now a full-height right rail beside the Trend + Analyzed plays stack, so it gets substantially more dashboard space. The old small title + plain background strip has been replaced by an osu!-inspired performance card using local assets only:

- beatmap background banner with dark readability overlay;
- performance header;
- map + difficulty / player metadata;
- star, mod and AR badges;
- large grade and production proficiency;
- accuracy, miss count and Aim Performance summary.

The existing run history and Overview / Training / Comparison / Errors content are preserved as compact sub-tabs beneath the song timeline. The left/right dashboard divider remains draggable.

## Detailed diagnostics layout

Double-clicking a play now opens the same three-lane song timeline under the plain-English diagnosis. Object-switch diagnostics and previous exact-difficulty runs are placed in two tabs beneath it, leaving enough vertical room for the graphs without removing either dataset. Top-error cursor-path reconstruction remains unchanged.

## Scoring

No v22 scoring constants changed. `ProductionScoring.Profile` is identical to v21. The new timeline is a visualization/diagnostic projection of that model, not a new score used by collections, training zones, database history, or dashboard aggregate cards.
