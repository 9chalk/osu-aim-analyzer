# v32 hotfix

- Removed oversized construction-time minimums from the Insights split container.
- Added `ToolkitUi.SetSplitterDistanceSafe(...)` as the only direct splitter-distance writer.
- Routed Dashboard deferred split initialization through the safe setter.
- Routed all Replay Tools resize-driven splitters through the safe setter.
- A transient small client size now causes the layout pass to skip/retry instead of throwing.
- No scoring, database, Insights, diagnosis, or training-response logic changed.
