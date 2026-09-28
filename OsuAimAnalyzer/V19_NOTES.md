# v19 — Proficiency collection routing

- Adds a **Collections** tab.
- Editable proficiency-range → collection-name rules.
- Automatic routing of each newly analyzed beatmap difficulty by the run's proficiency score.
- Optional exclusive ranges: the latest run moves the difficulty among analyzer-managed proficiency collections.
- `Rebuild from history` classifies each map by its latest analyzed run.
- Reads/writes osu!stable `collection.db` using the documented binary format.
- Creates a timestamped backup before the first analyzer write each app session.
- Manual backup and restore controls.
- Keeps an analyzer-owned shadow state and watches `collection.db`; if osu! later writes its in-memory collections back to disk, analyzer-managed memberships are merged back in.

## Stable client visibility

The file is updated continuously and osu! does not need to be restarted *per play*. However, stable does not expose a supported external hot-reload API for `collection.db`; externally written memberships may not appear in an already-running client's Collections view until stable next reloads its collection state (normally the next client start). The app calls this out in the Collections tab rather than silently risking/forcing client restarts.
