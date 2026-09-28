# v27 — error-condition profiling + recent-play navigation

## Aim Analysis: Error conditions

Aim Analysis now has two subtabs: **Profile** and **Error conditions**. Error conditions keeps both v26 diagnostic layers but answers a different question: *what kind of map demand tends to bring this failure out?*

The left list contains both **movement causes** (Shake-off, Late acquisition, Stopped short, Braking overshoot, Correction loop, Curved approach, Lateral drift, Unstable braking, General imprecision) and **landing classes** (Overaim, Underaim, Lateral, Correction, Plain error, Clean). Select any one. The right side compares it against the player's full analyzed-jump baseline across:

- star rating;
- transition BPM;
- effective AR;
- CS4-normalized jump spacing;
- visible-object density;
- jump angle;
- local aim-demand / challenge.

For every metric the UI shows the range with the strongest useful association, matching-error rate inside that range, overall rate, relative tendency (for example 1.50×), typical value on matching samples, baseline typical value, and sample counts. Small bins are suppressed from becoming the headline association so one or two unusual jumps do not dominate the result.

A second table aggregates the exact beatmap difficulty + mod combinations where the selected cause appears, showing run count, diagnosed-jump count/rate, star rating, AR, BPM and spacing. This makes it possible to distinguish things such as “late acquisition is mostly an AR10.3+/high-BPM problem” from “late acquisition is distributed across everything I play.” The text explicitly treats these as replay-history correlations rather than proof of causation.

## Dashboard recent-play rail

The old bottom horizontal tabs and action strip were competing with the timeline/cause/error panels for vertical space. v27 replaces them with a narrow vertical rail:

- Overview
- Summary
- Runs
- Training
- Compare
- Errors
- Top errors
- Advanced

The performance hero remains fixed at the top. The selected page gets the entire body below it. **Runs** therefore gets a full-height table and now exposes up to about 24 nearby attempts on the exact difficulty. Top Errors reconstructs and embeds the cursor-path viewer in the same rail; Advanced embeds the existing detailed diagnostics form. Selecting a different play clears stale embedded tools and returns to Overview.

Overview is compacted to the synchronized song timeline, movement-cause panel, and landing-error profile. The landing/cause visualizations no longer share space with the run table.

## Layout bug fix

AimCauseProfileControl now reserves a footer region before drawing ranked cause bars. This prevents the final bar label from painting over the explanatory footer at the shorter heights used in the recent-play panel.

## Scoring / storage

No production scoring constants changed. No database schema migration is required; all new analysis uses existing PlayRow and TransitionMetric telemetry.
