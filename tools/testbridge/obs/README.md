# OBS take recorder (trailer shoot)

`rec.mjs` is a small Node CLI that records trailer takes with OBS Studio through
obs-websocket v5. It sets up a dedicated OBS profile and scene collection, starts and
stops takes with predictable file names, remuxes each take to MP4, and measures stutter
(duplicated frames, freezes) without anyone watching the footage.

It does not touch the game. It only talks to OBS (WebSocket, port 4455) and runs
ffmpeg/ffprobe.

## One-time owner setup

1. OBS: Tools > WebSocket Server Settings > Enable WebSocket server, port 4455,
   Enable Authentication, set a password, Apply.
2. Store the same password as a Windows user environment variable (PowerShell):

   ```powershell
   [Environment]::SetEnvironmentVariable('OBS_WS_PASSWORD', (Read-Host 'OBS WebSocket password'), 'User')
   ```

   The script reads `OBS_WS_PASSWORD` from its environment; if a shell predates the
   variable, it reads the persisted user value itself. The password is never printed or
   written anywhere. The value must match the OBS dialog exactly (a mismatch gives
   connection error code 4009, authentication failed).
3. Install the dependency once:

   ```powershell
   cd tools\testbridge\obs; npm install
   ```

ffmpeg/ffprobe are taken from the WinGet Gyan.FFmpeg package under
`%LOCALAPPDATA%\Microsoft\WinGet\Packages\`, else from `PATH`.

## Commands

Each command prints one line of JSON. Exit code 0 on success; otherwise non-zero with
`{"ok":false,"error":"..."}`.

| Command | What it does |
|---|---|
| `node rec.mjs status` | OBS and WebSocket versions, profile, scene collection, scene, video settings, output mode and record encoder, record directory, recording state, cursor capture. |
| `node rec.mjs setup` | Idempotent. Creates/switches to the `SkiTrailer` profile and scene collection and configures them (below). Replies with the previous profile and scene collection. |
| `node rec.mjs restore` | Switches back to the profile and scene collection that were current before `setup`. |
| `node rec.mjs cursor on\|off` | Cursor capture of the `Cities2` Game Capture input. |
| `node rec.mjs start <shot> <take>` | For example `start 01 1`. Refuses if the profile is not `SkiTrailer`, if recording, or if `<shot>-t<take>.mkv` exists. |
| `node rec.mjs stop` | Stops, waits until OBS closed the file, renames it to `<shot>-t<take>.mkv`, remuxes a copy to `.mp4` (`-c copy`), and replies with the ffprobe summary. |
| `node rec.mjs probe <file>` | ffprobe summary: duration, codec, size, average frame rate, frame count, file size, color tags. |
| `node rec.mjs check <file> [--sheets all]` | Frame count vs duration x fps, duplicated frames (`mpdecimate` drop count), `freezedetect` intervals (noise -60 dB, d=0.1), and a contact sheet JPEG next to the file (`fps=2,scale=640:-1,tile=6x4`; first sheet only unless `--sheets all`). |

## What `setup` changes in OBS

The owner's existing profile and scene collection are not modified. `setup` works only in:

- Profile `SkiTrailer`: Advanced output mode; recording to
  `%USERPROFILE%\Videos\SkiTrailer\takes`, container MKV, one audio track;
  canvas and output 3840 x 2160 at 60 fps, NV12, Rec.709, limited range (SDR).
  Record encoder NVENC HEVC (`obs_nvenc_hevc_tex`) with constant QP 18, keyframe
  every 1 s, preset P5, tune high quality, two-pass (quarter resolution), main profile.
- Scene collection `SkiTrailer`: scene `Game` with one Game Capture input `Cities2`
  that captures the window of `Cities2.exe` (`Cities#3A Skylines II:UnityWndClass:Cities2.exe`),
  fitted to the canvas, cursor capture off when first created.

The record encoder settings live in `recordEncoder.json` in the profile folder
(`%APPDATA%\obs-studio\basic\profiles\SkiTrailer\`), which the WebSocket cannot set.
`setup` writes that file while another profile is current and then switches back to
`SkiTrailer`, so OBS loads it. No OBS restart is needed.

To get back: `node rec.mjs restore` (names stored in `takes\.rec-setup.json`), or pick
the profile and scene collection in the OBS menus.

## Where takes go

- Takes: `%USERPROFILE%\Videos\SkiTrailer\takes\<shot>-t<take>.mkv` plus `.mp4` copy.
- Cuts (for edits): `%USERPROFILE%\Videos\SkiTrailer\cuts\`.
- State files: `takes\.rec-setup.json` (previous profile/collection names) and
  `takes\.rec-take.json` (the take being recorded). Names only, no secrets.

## Measured (OBS 33.0.1, obs-websocket 5.7.6, RTX 5090, 2026-11-10)

- Test takes of about 4.4 s: HEVC Main, yuv420p, 3840 x 2160, `60/1`, 262 and 271
  frames (frame count = duration x 60 exactly), bt709 primaries/transfer/matrix, tv
  range. Keyframes at 0, 1, 2, 3, 4 s. Streams: HEVC video plus one AAC track.
- Encoder settings in effect come from OBS's log at record start
  (`%APPDATA%\obs-studio\logs\`, block `[obs-nvenc: 'advanced_video_recording'] settings:`):
  `rate_control CQP, cqp 18, keyint 60, preset p5, tuning hq, multipass qres,
  profile main, b-frames 2, lookahead false, aq true`. This is the check to repeat after
  changing `recordEncoder.json`; ffprobe cannot show the QP.
- `check` on a 4.4 s 4K take takes about 3 s.

## Frame-exact takes: `dedupe`

`node rec.mjs dedupe <take.mkv> [--qp 14]` is for takes recorded in the bridge's capture mode
(`{op:"fps",target:30,capture:60}`, see `../README.md`, "Filming"). It drops repeated frames with ffmpeg `mpdecimate` and
writes the unique frames as `<take>-x60.mp4` at exactly 60 fps (HEVC, constant QP 14). The reply lists the hold lengths (how
many OBS frames each game frame stayed on screen: 1 to 3 is normal at 30 game frames a second) and every hold of 4 or more
frames, which marks a stall of the game during the take. Measured on a 25.6 s take: 1,536 recorded frames, 769 unique,
holds 1: 113, 2: 542, 3: 113, no long hold. Do not use it on a take with a locked camera and little motion: `mpdecimate`
would also drop frames that only look alike.

## Limits

- `start` refuses unless the current OBS profile is `SkiTrailer`, so a take is never
  recorded with another profile's settings. Run `setup` first.
- The `recordEncoder.json` route works without an OBS restart, but only because
  `setup` briefly switches to the previous profile and back. Switching profiles makes OBS
  re-save that profile's `basic.ini` with defaults it normally leaves implicit (for example
  `RecEncoder=nvenc`, `[Hotkeys]`); the values the owner set do not change. Switching scene
  collections re-saves the collection file (version stamp, preview zoom).
- Right after `CreateProfile`, OBS may briefly reject requests while it resets video
  (seen once). `setup` waits after creating the profile and retries once; it is idempotent
  and reports `"changes": []` when nothing needed changing.
- The WebSocket cannot delete a scene collection. To start over, remove the `SkiTrailer`
  collection in OBS (Scene Collection > Remove) while another collection is current.
- Game Capture hooks the game process (graphics hook) as soon as `Cities2.exe` has a
  window, whether recording or not.
- `check` reads `mpdecimate` drops: on static or nearly static footage (menus, loading,
  a black screen) most frames count as duplicates, so the number only means stutter on
  footage with continuous camera motion. `freezedetect` intervals have the same caveat.
- The contact sheet samples 2 frames per second (24 per sheet, 12 s of footage); the
  first sheet only unless `--sheets all`.
- The `.mp4` copy tags HEVC as `hvc1` for editor and Apple compatibility; frame rate in
  the MP4 reads as about 60.009 in ffprobe because of MP4 timebase rounding. Use the MKV
  as the master.
