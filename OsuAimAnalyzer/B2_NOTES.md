# B2 — Play-scoped error streaks

IMPLEMENTED; NEEDS USER TEST. No item is ACCEPTED.

Historical diagnostics no longer merge transitions from different plays into a repeated-error streak or let an unrelated play break a valid sequence. Streak labels identify the owning play and object range. Overall cause counts, per-transition diagnoses, direction labels, scoring and SQLite data remain unchanged.

The existing maximum gaps (two objects and 1,200 ms) remain. Duplicate/backward objects break adjacency; unknown play IDs contribute counts but cannot produce a reliably owned streak. Equal-length ties use the lowest play ID then earliest local sequence. The profile tooltip/accessibility text includes the complete location.

Validation: 132 tests pass, including 12 new tests and unchanged TG1 snapshots. Restore, Debug build and Windows x64 publish succeed; the existing WFAC010 DPI warning remains. Reference material/master are unchanged.

Launch publish-streak-fix/OsuAimAnalyzer.exe. In Aim Analysis, inspect Why control breaks and the history summary with several history filters. Compare Recent Play and detailed summaries; repeated patterns should name one play. Check narrow-window/DPI readability and hover for full text. Original practice exports still omit optional video/storyboard media. TG6 has not begun.
