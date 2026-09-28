# v20

- Added one-click historical collection categorization.
- Choose Today, 7 days, 30 days, 90 days, or All time.
- Preview shows analyzed-play count and unique beatmap-difficulty count before writing.
- Each beatmap difficulty is categorized using its latest analyzed run inside the chosen window.
- Historical sort is additive: it updates maps in the selected window without clearing older managed maps.
- Existing `Rebuild ALL history` remains the full destructive regeneration path.
- Historical categorization uses the same automatic collection.db backup/atomic-write safety path as live routing.
