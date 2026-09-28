# v25 — readable aim profile + advanced telemetry drawer

## Aim Analysis hierarchy

The default Aim Analysis view is no longer a wall of historical graphs. The underlying v24 timelines are retained unchanged, but they now live behind an **Advanced breakdown** expander.

The primary view is designed to answer "what does my aim look like?" without requiring the player to interpret raw telemetry:

- **Aim fingerprint radar** — Centering, Arrival timing, Straightness, Stability / shake, Braking, Path efficiency, and Raw aim skill on one shape. `1000` is the reference ring; production mechanic components top out at 1000, while Raw aim skill is difficulty-adjusted and can exceed it.
- **Lifetime landing map** — the same real axial/lateral error model used by the per-play landing view, aggregated across the selected history window. Left is underaim, right is overaim, vertical is lateral, and the dot cloud shows actual stored replay errors.
- **Mechanic cards** — each production mechanic is shown with a purpose-built visual metaphor, its real score, a plain-English description, and a recent-20 marker against the selected-history baseline.
- **Raw aim skill** — the profile's top-100 unique-map Aim Rating is surfaced as the difficulty-adjusted capability axis/card instead of treating easy-play averages as skill.
- **Plain-English profile summary** — recent form, average centering error, dominant directional error, strongest mechanic, and mechanic with the most room are summarized directly.

## Advanced breakdown

Expanding **Advanced breakdown** restores the exact v24 diagnostic views:

1. Aim Performance over time.
2. Proficiency over time.
3. Mean / median / P90 center error over time.
4. Error-class mix over time.
5. All production mechanic scores over time.
6. Exact lifetime vs recent mechanic table.

The play-by-play / daily-average timeline selector now lives inside this advanced section because it only affects these graphs.

## Scoring / storage

No scoring constants, replay analysis rules, or database schema were changed in v25. The new visuals consume the same stored play and transition telemetry as v24.
