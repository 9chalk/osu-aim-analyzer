# v30 — Insights + dynamic training response + coaching diagnosis

## Insights
- New top-level **Insights** tab discovers meaningful history-level signals that are not already explicit diagnostics.
- Categories include improvement, mod effects, player-specific aim quirks, and training-response sequences.
- Findings are ranked by effect size, consistency/sample support, and practical usefulness.
- Clicking an insight shows its evidence, caveat, and an exact-data visual aid.
- Mod comparisons use nearest difficulty-matched non-mod plays rather than raw global averages when possible.

## Dynamic training response
- The analyzer now looks at the plays surrounding large same-map-family proficiency changes.
- If an intervening practice variant is followed by an unusually large retest gain, it is surfaced as a **training-response** insight.
- Demand differences (effective BPM, CS4-normalized spacing, AR) are shown so successful practice variants can become repeatable stepping stones.
- The same sequence information is appended to per-run diagnosis when relevant.
- Sequence findings are explicitly associations, not claimed causality.

## Diagnosis coaching UI
- Error Conditions diagnosis is now a high-contrast coaching surface with titled cards for the objective diagnosis, strongest measurable signal, training routes, mastery target, and evidence caveat.
- Multiple remedy routes are generated. Priority goes to the **nearest already-controlled map of the same aim type**; secondary routes isolate the strongest contributor by holding other demands similar.
- The contributor table remains available underneath for exact numeric evidence.

## Window/layout cleanup
- Main window default/minimum size increased for the new analysis surfaces.
- Error-condition diagnosis receives more horizontal and vertical space.
- Detailed diagnostics opens at a screen-friendlier 1380×900 and keeps minimum dimensions that still reflow cleanly.
