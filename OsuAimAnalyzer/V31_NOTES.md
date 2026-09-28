# v31 — startup splitter hotfix

- Fixed the v30 startup crash: `SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize`.
- Root cause: the new Insights split container set `SplitterDistance = 410` immediately while the control still had its small pre-layout width.
- Insights now uses the existing deferred `SetInitialSplit(...)` helper, which waits for a valid size and clamps the divider against both panel minimums and the splitter width.
- No scoring, analysis, database, Insights, or diagnosis logic changed.
