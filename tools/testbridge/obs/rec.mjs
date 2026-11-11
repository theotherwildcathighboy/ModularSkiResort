#!/usr/bin/env node
// Trailer take recorder: drives OBS Studio through obs-websocket v5.
// Every command prints exactly one line of JSON to stdout.
// Exit code 0 on success; non-zero with {"ok":false,"error":...} otherwise.
// The WebSocket password comes from the OBS_WS_PASSWORD user environment
// variable and is never printed, logged, or written anywhere.

import { OBSWebSocket } from 'obs-websocket-js';
import { execFileSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

// ---------- constants ----------

const WS_URL = process.env.OBS_WS_URL || 'ws://127.0.0.1:4455';
const NAME = 'SkiTrailer'; // profile and scene collection name
const SCENE = 'Game';
const INPUT = 'Cities2';
const WIDTH = 3840;
const HEIGHT = 2160;
const FPS = 60;
const ROOT_DIR = path.join(os.homedir(), 'Videos', 'SkiTrailer');
const TAKES_DIR = path.join(ROOT_DIR, 'takes');
const CUTS_DIR = path.join(ROOT_DIR, 'cuts');
const SETUP_STATE = path.join(TAKES_DIR, '.rec-setup.json'); // previous profile + collection names
const TAKE_STATE = path.join(TAKES_DIR, '.rec-take.json'); // the take being recorded
const PROFILES_DIR = path.join(process.env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming'),
  'obs-studio', 'basic', 'profiles');

// Game Capture window string: Title:Class:Executable, ':' in the title escaped as '#3A'.
const GAME_WINDOW = 'Cities#3A Skylines II:UnityWndClass:Cities2.exe';

// Record encoder: NVENC HEVC, constant QP. Keys are those of the obs-nvenc plugin (OBS 31+).
const REC_ENCODER = 'obs_nvenc_hevc_tex';
const REC_ENCODER_SETTINGS = {
  rate_control: 'CQP',
  cqp: 18,
  keyint_sec: 1,
  preset: 'p5',
  tune: 'hq',
  multipass: 'qres',
  profile: 'main',
  bf: 2,
  lookahead: false,
  adaptive_quantization: true,
};

// ---------- helpers ----------

class UserError extends Error {}

function out(obj, code = 0) {
  process.stdout.write(JSON.stringify(obj) + '\n');
  process.exitCode = code;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function getPassword() {
  let pw = process.env.OBS_WS_PASSWORD;
  if (!pw) {
    // The calling shell may predate the variable: read the persisted user value.
    try {
      pw = execFileSync('powershell', ['-NoProfile', '-Command',
        "[Environment]::GetEnvironmentVariable('OBS_WS_PASSWORD','User')"],
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], windowsHide: true }).trim();
    } catch {
      pw = '';
    }
  }
  return pw || undefined;
}

async function connect() {
  const obs = new OBSWebSocket();
  try {
    await obs.connect(WS_URL, getPassword());
  } catch (e) {
    // Never include anything that could carry the password.
    const code = e && e.code ? ` (code ${e.code})` : '';
    throw new UserError(`cannot connect to OBS WebSocket at ${WS_URL}${code}: ` +
      'is OBS running with the WebSocket server enabled and OBS_WS_PASSWORD set?');
  }
  // Name the failing request in errors (request data never includes the password).
  const call = obs.call.bind(obs);
  obs.call = async (type, data) => {
    try {
      return await call(type, data);
    } catch (e) {
      throw new UserError(`OBS request ${type} failed (code ${e.code ?? '?'}): ${e.message || 'no comment'}`);
    }
  };
  return obs;
}

function toolPath(name) {
  const local = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
  const pkgs = path.join(local, 'Microsoft', 'WinGet', 'Packages');
  try {
    for (const d of fs.readdirSync(pkgs).filter((n) => n.startsWith('Gyan.FFmpeg'))) {
      for (const sub of fs.readdirSync(path.join(pkgs, d))) {
        const p = path.join(pkgs, d, sub, 'bin', `${name}.exe`);
        if (fs.existsSync(p)) return p;
      }
    }
  } catch { /* fall back to PATH */ }
  return name;
}
const FFMPEG = toolPath('ffmpeg');
const FFPROBE = toolPath('ffprobe');

function run(cmd, args) {
  return new Promise((resolve, reject) => {
    const p = spawn(cmd, args, { windowsHide: true });
    let so = '';
    let se = '';
    const lines = [];
    let partial = '';
    p.stdout.on('data', (d) => { so += d; });
    p.stderr.on('data', (d) => {
      // Keep stderr as lines without holding megabytes of debug output.
      partial += d;
      const parts = partial.split(/\r?\n|\r/);
      partial = parts.pop();
      for (const l of parts) lines.push(l);
      if (se.length < 20000) se += d;
    });
    p.on('error', reject);
    p.on('close', (code) => {
      if (partial) lines.push(partial);
      if (code !== 0) reject(new UserError(`${path.basename(cmd)} failed (${code}): ${se.slice(-800)}`));
      else resolve({ stdout: so, lines });
    });
  });
}

function parseRate(r) {
  if (!r || r === '0/0') return null;
  const [n, d] = r.split('/').map(Number);
  return d ? n / d : n;
}

async function probe(file) {
  if (!fs.existsSync(file)) throw new UserError(`file not found: ${file}`);
  const { stdout } = await run(FFPROBE, ['-v', 'error', '-count_frames', '-select_streams', 'v:0',
    '-show_entries',
    'stream=codec_name,profile,pix_fmt,width,height,avg_frame_rate,r_frame_rate,nb_read_frames,color_space,color_transfer,color_primaries,color_range:format=duration,size,format_name',
    '-of', 'json', file]);
  const j = JSON.parse(stdout);
  const s = (j.streams && j.streams[0]) || {};
  const f = j.format || {};
  const fps = parseRate(s.avg_frame_rate);
  return {
    path: path.resolve(file),
    duration: f.duration ? Number(f.duration) : null,
    codec: s.codec_name || null,
    profile: s.profile || null,
    pixFmt: s.pix_fmt || null,
    width: s.width || null,
    height: s.height || null,
    avgFrameRate: s.avg_frame_rate || null,
    fps: fps ? Math.round(fps * 1000) / 1000 : null,
    frames: s.nb_read_frames ? Number(s.nb_read_frames) : null,
    color: { space: s.color_space || null, transfer: s.color_transfer || null,
      primaries: s.color_primaries || null, range: s.color_range || null },
    container: f.format_name || null,
    sizeBytes: f.size ? Number(f.size) : fs.statSync(file).size,
  };
}

function readJson(p) {
  try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch { return null; }
}

function writeJson(p, obj) {
  fs.mkdirSync(path.dirname(p), { recursive: true });
  fs.writeFileSync(p, JSON.stringify(obj, null, 2) + '\n');
}

async function waitFor(fn, timeoutMs, what) {
  const t0 = Date.now();
  for (;;) {
    if (await fn()) return;
    if (Date.now() - t0 > timeoutMs) throw new UserError(`timed out waiting for ${what}`);
    await sleep(100);
  }
}

async function currentNames(obs) {
  const p = await obs.call('GetProfileList');
  const c = await obs.call('GetSceneCollectionList');
  return { p, c };
}

async function switchProfile(obs, name) {
  const { currentProfileName } = await obs.call('GetProfileList');
  if (currentProfileName === name) return;
  await obs.call('SetCurrentProfile', { profileName: name });
  await waitFor(async () => (await obs.call('GetProfileList')).currentProfileName === name, 10000,
    `profile ${name}`);
}

async function switchCollection(obs, name) {
  const { currentSceneCollectionName } = await obs.call('GetSceneCollectionList');
  if (currentSceneCollectionName === name) return;
  await obs.call('SetCurrentSceneCollection', { sceneCollectionName: name });
  await waitFor(async () => (await obs.call('GetSceneCollectionList')).currentSceneCollectionName === name,
    15000, `scene collection ${name}`);
  await sleep(500); // let the frontend finish loading sources
}

function safeProfileDir(name) {
  // OBS names the profile folder after the profile name with unsafe characters removed;
  // for 'SkiTrailer' this is the name itself.
  return path.join(PROFILES_DIR, name);
}

// ---------- commands ----------

async function cmdStatus() {
  const obs = await connect();
  try {
    const v = await obs.call('GetVersion');
    const { p, c } = await currentNames(obs);
    const scene = await obs.call('GetCurrentProgramScene');
    const vs = await obs.call('GetVideoSettings');
    const dir = await obs.call('GetRecordDirectory');
    const rec = await obs.call('GetRecordStatus');
    const param = async (category, name) =>
      (await obs.call('GetProfileParameter', { parameterCategory: category, parameterName: name })).parameterValue;
    const mode = await param('Output', 'Mode');
    const output = { mode };
    if (mode === 'Advanced') {
      output.recEncoder = await param('AdvOut', 'RecEncoder');
      output.recFormat = await param('AdvOut', 'RecFormat2');
      const encFile = path.join(safeProfileDir(p.currentProfileName), 'recordEncoder.json');
      output.recordEncoderJson = readJson(encFile);
    } else {
      output.recQuality = await param('SimpleOutput', 'RecQuality');
      output.recFormat = await param('SimpleOutput', 'RecFormat2');
    }
    let cursor = null;
    try {
      const s = await obs.call('GetInputSettings', { inputName: INPUT });
      cursor = s.inputSettings.capture_cursor !== false;
    } catch { /* input absent in this collection */ }
    out({
      ok: true,
      obsVersion: v.obsVersion,
      obsWebSocketVersion: v.obsWebSocketVersion,
      rpcVersion: v.rpcVersion,
      profile: p.currentProfileName,
      sceneCollection: c.currentSceneCollectionName,
      scene: scene.currentProgramSceneName ?? scene.sceneName,
      video: {
        base: `${vs.baseWidth}x${vs.baseHeight}`,
        output: `${vs.outputWidth}x${vs.outputHeight}`,
        fps: vs.fpsNumerator / vs.fpsDenominator,
      },
      output,
      recordDirectory: dir.recordDirectory,
      recording: rec.outputActive,
      recordingPaused: rec.outputPaused,
      captureCursor: cursor,
    });
  } finally {
    await obs.disconnect();
  }
}

async function cmdSetup() {
  fs.mkdirSync(TAKES_DIR, { recursive: true });
  fs.mkdirSync(CUTS_DIR, { recursive: true });
  const obs = await connect();
  const changes = [];
  try {
    if ((await obs.call('GetRecordStatus')).outputActive) throw new UserError('recording is active; stop it first');

    const { p, c } = await currentNames(obs);
    // Remember where the owner came from, unless we are already on SkiTrailer (re-run).
    let prev = readJson(SETUP_STATE) || {};
    if (p.currentProfileName !== NAME) prev.profile = p.currentProfileName;
    if (c.currentSceneCollectionName !== NAME) prev.sceneCollection = c.currentSceneCollectionName;
    if (!prev.profile) prev.profile = p.profiles.find((n) => n !== NAME) || null;
    if (!prev.sceneCollection) prev.sceneCollection = c.sceneCollections.find((n) => n !== NAME) || null;
    writeJson(SETUP_STATE, { profile: prev.profile, sceneCollection: prev.sceneCollection });

    // ---- profile ----
    if (!p.profiles.includes(NAME)) {
      await obs.call('CreateProfile', { profileName: NAME }); // creates and switches
      changes.push('created profile');
      await sleep(1000); // OBS resets video and outputs for the new profile
    }
    await switchProfile(obs, NAME);

    const set = async (category, name, value) => {
      const cur = (await obs.call('GetProfileParameter', { parameterCategory: category, parameterName: name }))
        .parameterValue;
      if (cur !== String(value)) {
        await obs.call('SetProfileParameter',
          { parameterCategory: category, parameterName: name, parameterValue: String(value) });
        changes.push(`${category}.${name}`);
      }
    };
    await set('Output', 'Mode', 'Advanced');
    await set('Output', 'FilenameFormatting', '%CCYY-%MM-%DD %hh-%mm-%ss');
    await set('AdvOut', 'RecType', 'Standard');
    await set('AdvOut', 'RecFilePath', TAKES_DIR);
    await set('AdvOut', 'RecFormat2', 'mkv');
    await set('AdvOut', 'RecEncoder', REC_ENCODER);
    await set('AdvOut', 'RecUseRescale', 'false');
    await set('AdvOut', 'RecTracks', '1');
    await set('SimpleOutput', 'FilePath', TAKES_DIR);
    await set('Video', 'ColorFormat', 'NV12');
    await set('Video', 'ColorSpace', '709');
    await set('Video', 'ColorRange', 'Partial');

    const vs = await obs.call('GetVideoSettings');
    if (vs.baseWidth !== WIDTH || vs.baseHeight !== HEIGHT || vs.outputWidth !== WIDTH ||
        vs.outputHeight !== HEIGHT || vs.fpsNumerator / vs.fpsDenominator !== FPS ||
        changes.some((x) => x.startsWith('Video.'))) {
      await obs.call('SetVideoSettings', {
        baseWidth: WIDTH, baseHeight: HEIGHT, outputWidth: WIDTH, outputHeight: HEIGHT,
        fpsNumerator: FPS, fpsDenominator: 1,
      });
      changes.push('video settings');
    }

    // recordEncoder.json is only read when OBS loads a profile, and the WebSocket has no request
    // for it. Write it while another profile is current, then switch back so OBS loads it.
    const encFile = path.join(safeProfileDir(NAME), 'recordEncoder.json');
    const curEnc = readJson(encFile);
    const encOk = curEnc && Object.entries(REC_ENCODER_SETTINGS).every(([k, val]) => curEnc[k] === val);
    const outputChanged = changes.some((x) => x.startsWith('Output.') || x.startsWith('AdvOut.'));
    if (!encOk || outputChanged) {
      const other = prev.profile && prev.profile !== NAME ? prev.profile
        : (await obs.call('GetProfileList')).profiles.find((n) => n !== NAME);
      if (!other) throw new UserError('need a second profile to reload SkiTrailer');
      await switchProfile(obs, other);
      if (!fs.existsSync(path.dirname(encFile))) throw new UserError(`profile folder not found: ${path.dirname(encFile)}`);
      writeJson(encFile, { ...(curEnc || {}), ...REC_ENCODER_SETTINGS });
      await switchProfile(obs, NAME);
      changes.push('recordEncoder.json (profile reloaded)');
    }

    // ---- scene collection ----
    const cl = await obs.call('GetSceneCollectionList');
    if (!cl.sceneCollections.includes(NAME)) {
      await obs.call('CreateSceneCollection', { sceneCollectionName: NAME }); // creates and switches
      changes.push('created scene collection');
    }
    await switchCollection(obs, NAME);

    const scenes = (await obs.call('GetSceneList')).scenes.map((s) => s.sceneName);
    if (!scenes.includes(SCENE)) {
      await obs.call('CreateScene', { sceneName: SCENE });
      changes.push('created scene');
    }
    const inputs = (await obs.call('GetInputList')).inputs.map((i) => i.inputName);
    const captureSettings = {
      capture_mode: 'window',
      window: GAME_WINDOW,
      priority: 2, // match the executable first
      capture_cursor: false,
      allow_transparency: false,
      anti_cheat_hook: true,
    };
    let itemId;
    if (!inputs.includes(INPUT)) {
      const r = await obs.call('CreateInput', {
        sceneName: SCENE, inputName: INPUT, inputKind: 'game_capture',
        inputSettings: captureSettings, sceneItemEnabled: true,
      });
      itemId = r.sceneItemId;
      changes.push('created game capture');
    } else {
      // Keep the current cursor choice on re-runs.
      const { capture_cursor: _c, ...rest } = captureSettings;
      await obs.call('SetInputSettings', { inputName: INPUT, inputSettings: rest, overlay: true });
      try {
        itemId = (await obs.call('GetSceneItemId', { sceneName: SCENE, sourceName: INPUT })).sceneItemId;
      } catch {
        itemId = (await obs.call('CreateSceneItem', { sceneName: SCENE, sourceName: INPUT })).sceneItemId;
        changes.push('added game capture to scene');
      }
    }
    // Fit the capture to the canvas whatever resolution the game runs at.
    await obs.call('SetSceneItemTransform', {
      sceneName: SCENE, sceneItemId: itemId,
      sceneItemTransform: {
        positionX: 0, positionY: 0, alignment: 5, rotation: 0,
        boundsType: 'OBS_BOUNDS_SCALE_INNER', boundsWidth: WIDTH, boundsHeight: HEIGHT, boundsAlignment: 0,
      },
    });
    await obs.call('SetCurrentProgramScene', { sceneName: SCENE });

    const after = await obs.call('GetVideoSettings');
    out({
      ok: true,
      previous: { profile: prev.profile, sceneCollection: prev.sceneCollection },
      profile: NAME,
      sceneCollection: NAME,
      scene: SCENE,
      video: { base: `${after.baseWidth}x${after.baseHeight}`, output: `${after.outputWidth}x${after.outputHeight}`,
        fps: after.fpsNumerator / after.fpsDenominator },
      recordDirectory: (await obs.call('GetRecordDirectory')).recordDirectory,
      recordEncoder: REC_ENCODER,
      recordEncoderSettings: readJson(encFile),
      changes,
      restoreWith: 'node rec.mjs restore',
    });
  } finally {
    await obs.disconnect();
  }
}

async function cmdRestore() {
  const prev = readJson(SETUP_STATE);
  if (!prev || (!prev.profile && !prev.sceneCollection)) throw new UserError(`no saved state in ${SETUP_STATE}`);
  const obs = await connect();
  try {
    if ((await obs.call('GetRecordStatus')).outputActive) throw new UserError('recording is active; stop it first');
    if (prev.profile) await switchProfile(obs, prev.profile);
    if (prev.sceneCollection) await switchCollection(obs, prev.sceneCollection);
    const { p, c } = await currentNames(obs);
    out({ ok: true, profile: p.currentProfileName, sceneCollection: c.currentSceneCollectionName });
  } finally {
    await obs.disconnect();
  }
}

async function cmdCursor(arg) {
  if (arg !== 'on' && arg !== 'off') throw new UserError('usage: cursor on|off');
  const obs = await connect();
  try {
    await obs.call('SetInputSettings', { inputName: INPUT, inputSettings: { capture_cursor: arg === 'on' }, overlay: true });
    const s = await obs.call('GetInputSettings', { inputName: INPUT });
    out({ ok: true, input: INPUT, captureCursor: s.inputSettings.capture_cursor !== false });
  } finally {
    await obs.disconnect();
  }
}

function takeName(shot, take) {
  if (!/^[A-Za-z0-9_]+$/.test(shot || '') || !/^[0-9]+$/.test(take || '')) {
    throw new UserError('usage: start <shot> <take>  (shot: letters/digits/_, take: digits)');
  }
  return `${shot}-t${take}`;
}

async function cmdStart(shot, take) {
  const base = takeName(shot, take);
  const mkv = path.join(TAKES_DIR, `${base}.mkv`);
  if (fs.existsSync(mkv)) throw new UserError(`take already exists: ${mkv}`);
  const obs = await connect();
  try {
    if ((await obs.call('GetRecordStatus')).outputActive) throw new UserError('a recording is already active');
    const { currentProfileName } = await obs.call('GetProfileList');
    if (currentProfileName !== NAME) {
      throw new UserError(`current OBS profile is "${currentProfileName}", not "${NAME}"; run "node rec.mjs setup" first`);
    }
    await obs.call('StartRecord');
    await waitFor(async () => (await obs.call('GetRecordStatus')).outputActive, 10000, 'recording to start');
    const startedAt = new Date().toISOString();
    writeJson(TAKE_STATE, { shot, take, startedAt });
    out({ ok: true, shot, take, startedAt, target: mkv });
  } finally {
    await obs.disconnect();
  }
}

async function renameWhenUnlocked(from, to, timeoutMs) {
  const t0 = Date.now();
  for (;;) {
    try {
      fs.renameSync(from, to);
      return;
    } catch (e) {
      if (Date.now() - t0 > timeoutMs) throw new UserError(`cannot rename ${from}: ${e.code || e.message}`);
      await sleep(250);
    }
  }
}

async function cmdStop() {
  const state = readJson(TAKE_STATE);
  const obs = await connect();
  let outputPath;
  try {
    if (!(await obs.call('GetRecordStatus')).outputActive) throw new UserError('no recording is active');
    let stoppedPath = null;
    const onState = (e) => {
      if (e.outputState === 'OBS_WEBSOCKET_OUTPUT_STOPPED') stoppedPath = e.outputPath || stoppedPath || '';
    };
    obs.on('RecordStateChanged', onState);
    const r = await obs.call('StopRecord');
    outputPath = r.outputPath;
    await waitFor(async () => stoppedPath !== null || !(await obs.call('GetRecordStatus')).outputActive,
      30000, 'recording to stop');
    // Prefer the STOPPED event: it fires after the file is closed.
    await waitFor(async () => stoppedPath !== null, 5000, 'RecordStateChanged STOPPED').catch(() => {});
    obs.off('RecordStateChanged', onState);
    outputPath = stoppedPath || outputPath;
  } finally {
    await obs.disconnect();
  }
  if (!outputPath || !fs.existsSync(outputPath)) throw new UserError(`OBS output file not found: ${outputPath}`);

  const base = state ? takeName(state.shot, String(state.take)) : path.basename(outputPath, path.extname(outputPath));
  const mkv = path.join(TAKES_DIR, `${base}${path.extname(outputPath) || '.mkv'}`);
  if (fs.existsSync(mkv)) throw new UserError(`target exists, left OBS file in place: ${outputPath}`);
  await renameWhenUnlocked(outputPath, mkv, 15000);
  try { fs.unlinkSync(TAKE_STATE); } catch { /* none */ }

  const mp4 = mkv.replace(/\.[^.]+$/, '.mp4');
  await run(FFMPEG, ['-hide_banner', '-loglevel', 'error', '-y', '-i', mkv, '-map', '0', '-c', 'copy',
    '-tag:v', 'hvc1', '-movflags', '+faststart', mp4]).catch(async () =>
    // AV1 or other codecs: no hvc1 tag.
    run(FFMPEG, ['-hide_banner', '-loglevel', 'error', '-y', '-i', mkv, '-map', '0', '-c', 'copy',
      '-movflags', '+faststart', mp4]));
  const info = await probe(mkv);
  out({ ok: true, ...info, mp4, startedAt: state ? state.startedAt : null });
}

async function cmdProbe(file) {
  if (!file) throw new UserError('usage: probe <file>');
  out({ ok: true, ...(await probe(file)) });
}

async function cmdCheck(file, rest) {
  if (!file) throw new UserError('usage: check <file> [--sheets all]');
  const allSheets = rest.includes('--sheets') && rest[rest.indexOf('--sheets') + 1] === 'all';
  const info = await probe(file);
  const expected = info.duration && info.fps ? Math.round(info.duration * info.fps) : null;

  // One decode pass: freezedetect sees every frame, then mpdecimate logs keep/drop per frame.
  const { lines } = await run(FFMPEG, ['-hide_banner', '-nostats', '-loglevel', 'debug', '-i', file, '-map', '0:v:0',
    '-vf', 'freezedetect=n=-60dB:d=0.1,mpdecimate', '-fps_mode', 'passthrough', '-f', 'null', '-']);
  let dropped = 0;
  let kept = 0;
  const freezes = [];
  let open = null;
  for (const l of lines) {
    if (/mpdecimate/.test(l)) {
      if (/\bdrop pts:/.test(l)) dropped++;
      else if (/\bkeep pts:/.test(l)) kept++;
    }
    let m;
    if ((m = l.match(/lavfi\.freezedetect\.freeze_start: ([\d.]+)/))) open = { start: Number(m[1]) };
    else if ((m = l.match(/lavfi\.freezedetect\.freeze_duration: ([\d.]+)/)) && open) open.duration = Number(m[1]);
    else if ((m = l.match(/lavfi\.freezedetect\.freeze_end: ([\d.]+)/)) && open) {
      open.end = Number(m[1]);
      freezes.push(open);
      open = null;
    }
  }
  if (open) freezes.push({ ...open, end: null, untilEnd: true });

  // Contact sheet(s) next to the file.
  const stem = file.replace(/\.[^.]+$/, '');
  const sheetArgs = allSheets
    ? ['-vf', 'fps=2,scale=640:-1,tile=6x4', '-q:v', '3', `${stem}-sheet-%02d.jpg`]
    : ['-vf', 'fps=2,scale=640:-1,tile=6x4', '-frames:v', '1', '-q:v', '3', `${stem}-sheet.jpg`];
  await run(FFMPEG, ['-hide_banner', '-loglevel', 'error', '-y', '-i', file, '-map', '0:v:0', ...sheetArgs]);
  const dir = path.dirname(path.resolve(file));
  const prefix = path.basename(stem) + '-sheet';
  const sheets = fs.readdirSync(dir).filter((n) => n.startsWith(prefix) && n.endsWith('.jpg'))
    .map((n) => path.join(dir, n)).sort();

  out({
    ok: true,
    path: info.path,
    duration: info.duration,
    fps: info.fps,
    frames: info.frames,
    expectedFrames: expected,
    frameDelta: expected !== null && info.frames !== null ? info.frames - expected : null,
    mpdecimate: { kept, dropped, droppedPct: kept + dropped ? Math.round(1000 * dropped / (kept + dropped)) / 10 : null },
    freezes,
    freezeCount: freezes.length,
    frozenSeconds: Math.round(freezes.reduce((a, f) => a + (f.duration || 0), 0) * 1000) / 1000,
    sheets,
  });
}

// A take recorded in capture mode ({op:"fps",target:30,capture:60} in the bridge): the game renders 30 frames a second of real
// time and each one is 1/60 s of game time, so OBS (60 fps) holds every game frame for 1 to 3 of its own frames. This drops the
// repeats and writes the unique frames as an exact 60 fps clip. A hold of 4 or more OBS frames means the game stalled there
// (the picture is still complete: no game frame is lost, the stall only shows in the hold list).
async function cmdDedupe(file, rest) {
  if (!file) throw new UserError("usage: dedupe <file> [--qp 14]");
  const qp = rest.includes("--qp") ? String(Number(rest[rest.indexOf("--qp") + 1]) || 14) : "14";
  const info = await probe(file);
  const { lines } = await run(FFMPEG, ["-hide_banner", "-nostats", "-i", file, "-map", "0:v:0", "-vf", "mpdecimate,showinfo",
    "-fps_mode", "passthrough", "-f", "null", "-"]);
  const times = [];
  for (const l of lines) {
    const m = l.match(/Parsed_showinfo.*pts_time:([\d.]+)/);
    if (m) times.push(Number(m[1]));
  }
  const holds = {};
  const long = [];
  for (let i = 1; i < times.length; i++) {
    const n = Math.round((times[i] - times[i - 1]) * (info.fps || 60));
    holds[n] = (holds[n] || 0) + 1;
    if (n >= 4) long.push({ at: Math.round(times[i - 1] * 100) / 100, frames: n });
  }
  const target = file.replace(/\.[^.]+$/, "") + "-x60.mp4";
  await run(FFMPEG, ["-hide_banner", "-loglevel", "error", "-y", "-i", file, "-map", "0:v:0", "-vf", "mpdecimate,setpts=N/60/TB",
    "-r", "60", "-an", "-c:v", "hevc_nvenc", "-preset", "p6", "-rc", "constqp", "-qp", qp, "-pix_fmt", "yuv420p",
    "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-tag:v", "hvc1", target]);
  const result = await probe(target);
  out({ ok: true, source: info.path, sourceFrames: info.frames, unique: times.length, holds, longHolds: long.slice(0, 40),
    longHoldCount: long.length, path: result.path, frames: result.frames, duration: result.duration, fps: result.fps,
    sizeBytes: result.sizeBytes });
}

// ---------- main ----------

const [cmd, ...args] = process.argv.slice(2);
const commands = {
  status: () => cmdStatus(),
  // setup is idempotent: one retry covers OBS still settling after creating the profile
  // (seen once on the first run: a request right after CreateProfile failed).
  setup: async () => {
    try {
      await cmdSetup();
    } catch (e) {
      if (/recording is active/.test(e.message)) throw e;
      await sleep(2000);
      await cmdSetup();
    }
  },
  restore: () => cmdRestore(),
  cursor: () => cmdCursor(args[0]),
  start: () => cmdStart(args[0], args[1]),
  stop: () => cmdStop(),
  probe: () => cmdProbe(args[0]),
  check: () => cmdCheck(args[0], args.slice(1)),
  dedupe: () => cmdDedupe(args[0], args.slice(1)),
};

try {
  if (!commands[cmd]) throw new UserError('usage: node rec.mjs status|setup|restore|cursor on|off|start <shot> <take>|stop|probe <file>|check <file> [--sheets all]|dedupe <file> [--qp 14]');
  await commands[cmd]();
} catch (e) {
  const msg = e instanceof UserError ? e.message : (e && e.message) || String(e);
  out({ ok: false, error: msg }, 1);
}
