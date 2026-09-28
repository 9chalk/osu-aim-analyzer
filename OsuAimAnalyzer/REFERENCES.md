# References

This project interoperates with osu!stable local files and uses the public osu! ecosystem as implementation references.

- osu! replay (`.osr`) format documentation: https://osu.ppy.sh/wiki/en/Client/File_formats/osr_(file_format)
- Legacy osu! database structures: https://github.com/ppy/osu/wiki/Legacy-database-file-structure
- Official osu! difficulty/performance implementation (MIT): https://github.com/ppy/osu
- Official osu-tools performance calculator example (MIT): https://github.com/ppy/osu-tools
- StreamCompanion, including its PP calculator modules (MIT): https://github.com/Piotrekol/StreamCompanion

The current build does **not** copy StreamCompanion's PP implementation; its local `PpUtils` value is explicitly an estimate and is isolated for replacement with an official/current calculator later.

- osu! legacy database file structure (`collection.db`): https://github.com/ppy/osu/wiki/Legacy-database-file-structure
