# Release screenshots

This folder holds no images. The published screenshots live in two places, as the same files:

- `mod/SkiResortMod/Properties/Screenshots/` is the Paradox Mods listing set. `PublishConfiguration.xml` lists the files
  in the order the listing shows them. `Properties/Thumbnail.png` (1024 x 1024) is the thumbnail.
- `docs-site/public/shots/` is the docs site set. A page shows one with `<Shot src="name.jpg" label="..." />`.

Rules for a new or retaken shot:

- Take it in the game, on the build that ships. Never use a mock-up or a shot of an older build.
- Save an SDR 3840 x 2160 PNG, then publish a 1920 x 1080 JPEG (a crop of a hub detail may be smaller).
- Show no third-party mod panels or buttons, no placeholder names ("Ski area 1") and no test-city numbers.
- A shot that is on the listing has the same name and the same bytes in both folders.
- Keep only published files in the repository.
