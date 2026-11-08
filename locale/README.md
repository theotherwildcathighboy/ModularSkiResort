# Translating Modular Ski Resort

`en-US.json` lists every text the mod registers: each key and its English string. Version 1.0 ships English only. The game
does not load other languages from this folder yet. That comes in a later patch, so we keep a translation sent now until then.

## How to translate

1. Copy `en-US.json` to `<locale-id>.json`, using the game's locale id (`de-DE`, `fr-FR`, `es-ES`, `ja-JP`, ...).
2. Keep every key exactly as it is. Translate only the value on the right of each key.
3. Keep placeholders such as `{N}`, `{NAME}` or `{PCT}` unchanged. You may move them to fit your grammar.
   Keep icon tags such as `<icon:green>` or `<mark:open>` unchanged too. You may move an `<icon:...>` tag, but keep a `<mark:...>`
   tag at the start of its sentence.
4. Keep numbers and units (`km`, `m`, `%`) as they are, unless your language writes them differently. Keep key names such as
   `Ctrl+Shift+G`.
5. Translate every value. Do not delete keys.

The file is plain UTF-8 JSON, one key per line. Any text editor works.

## Regenerating the template

`en-US.json` is generated from the two English dictionaries in the mod source. Never edit it by hand:

    node tools/export-locale.js           write locale/en-US.json
    node tools/export-locale.js --check   exit 1 when the file is out of date

The check runs in CI and in `tools/preflight.ps1`, so a change to an English text fails the build until you
regenerate the file and commit it.
