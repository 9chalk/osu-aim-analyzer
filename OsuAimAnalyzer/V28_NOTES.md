# v28 — recent-play cleanup + Error Conditions layout/performance pass

## Recent-play Overview

- **Runs return to Overview**, directly under the landing-error visualization, so exact-difficulty attempt comparison is visible in the normal post-play workflow again.
- The separate Runs rail page is removed. The run grid is slightly denser (shorter rows/header and narrower columns) and still keeps the current attempt highlighted plus chronological deltas.
- The vertical rail is narrower and remains useful for Summary / Training / Compare / Errors / Top errors / Advanced.
- **Follow latest** now sits with the rail controls instead of floating at the bottom of an otherwise empty column.
- **Top-error count** moved onto the Top errors page beside a Generate / refresh button. This removes the detached bottom-left footer/artifact from v27.

## Compact diagnostic rendering fixes

- The three-lane song timeline no longer paints separate min/max values on top of each other when the lane is short. Compact lanes show one `min–max` range line; taller detailed views keep separate endpoints.
- The landing-error profile uses a tighter compact vertical rhythm so its two-column error bars fit at recent-play height rather than clipping the final rows.

## Error Conditions default layout

The error selector no longer depends on a SplitContainer being sized while its tab is hidden. That could initialize the left selector at its tiny minimum width and make the feature look almost missing. v28 uses a stable **210 px error-type column** with the summary / demand table / map table filling the rest, matching the intended wide layout immediately on first open.

## Responsiveness

- Entering **Error conditions** reuses the already-built lifetime analysis instead of force-rebuilding it.
- Per-condition association results are cached for the current history window, so revisiting an error/cause is immediate.
- Advanced lifetime graphs are now **lazy-rendered only when Advanced breakdown is open**. Normal Aim Analysis no longer builds six hidden graph datasets on every refresh.
- Changing the Advanced timeline grouping redraws only those graphs instead of reloading/reanalyzing lifetime transition telemetry.
- Recent-play page switches batch layout visibility changes to reduce unnecessary WinForms relayout/repaint work.

No production scoring constants or database schema changed.
