// Cuts a trailer from recorded takes with ffmpeg, from one edit list (JSON). No editor program, so a cut is a re-run.
//
//   node tools/trailer/cut.mjs <edit.json> [--preview] [--only <clip id>] [--keep]
//
// Pass 1 renders every clip of the list to its own file (trim, speed, a slow zoom or reframe, captions, the end card).
// Pass 2 joins the clips (hard cuts or crossfades) and lays the music under them. --preview renders at half size.
// The edit list (see edit-main.json): { size, fps, takes, work, out, fonts: {regular, bold}, music, clips: [...] }.
// A clip: { id, src, in, dur, speed, parts: [{in, dur, speed}], zoom: {from, to, cx, cy}, crop: {cx, cy},
//           fadeIn, fadeOut, xfade, dim, captions: [{lines, at, dur, pos, size}], logo: {file, at, h, y} }
// Times are seconds. `dur` is the clip's length in the cut; with `speed` the source span is dur x speed.
// Paths may start with %USERPROFILE%. Takes are big and live outside the repository.

import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const args = process.argv.slice(2);
const editFile = args.find((a) => !a.startsWith('--') && a.endsWith('.json'));
if (!editFile) { console.error('usage: node cut.mjs <edit.json> [--preview] [--only <clip id>] [--keep]'); process.exit(2); }
const preview = args.includes('--preview');
const only = args.includes('--only') ? args[args.indexOf('--only') + 1] : null;
const keep = args.includes('--keep');

const expand = (p) => path.resolve(p.replace(/%USERPROFILE%/gi, os.homedir()));
const edit = JSON.parse(fs.readFileSync(editFile, 'utf8'));
const scale = preview ? 0.5 : 1;
const W = Math.round(edit.size[0] * scale / 2) * 2;
const H = Math.round(edit.size[1] * scale / 2) * 2;
const FPS = edit.fps || 60;
const takes = expand(edit.takes);
const work = expand(edit.work) + (preview ? '-preview' : '');
fs.mkdirSync(work, { recursive: true });

function tool(name) {
  const pkgs = path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local'), 'Microsoft', 'WinGet', 'Packages');
  try {
    for (const d of fs.readdirSync(pkgs).filter((n) => n.startsWith('Gyan.FFmpeg'))) {
      for (const sub of fs.readdirSync(path.join(pkgs, d))) {
        const p = path.join(pkgs, d, sub, 'bin', `${name}.exe`);
        if (fs.existsSync(p)) return p;
      }
    }
  } catch { /* PATH */ }
  return name;
}
const FFMPEG = tool('ffmpeg');
const FFPROBE = tool('ffprobe');

function run(cmd, argv, cwd) {
  return new Promise((resolve, reject) => {
    const p = spawn(cmd, argv, { windowsHide: true, cwd });
    let out = '';
    let err = '';
    p.stdout.on('data', (d) => { out += d; });
    p.stderr.on('data', (d) => { if (err.length < 60000) err += d; });
    p.on('error', reject);
    p.on('close', (code) => (code === 0 ? resolve(out) : reject(new Error(`${path.basename(cmd)} failed (${code}): ${err.slice(-1500)}`))));
  });
}

async function frames(file) {
  const out = await run(FFPROBE, ['-v', 'error', '-count_frames', '-select_streams', 'v:0', '-show_entries', 'stream=nb_read_frames', '-of', 'csv=p=0', file]);
  return Number(out.trim());
}

// drawtext reads its text from a file, so quotes, colons and percent signs need no escaping. Font files are copied
// into the work folder and named without a drive letter (a colon in a filter argument starts a new option).
const fontFiles = {};
for (const [k, f] of Object.entries(edit.fonts)) {
  const name = `font-${k}${path.extname(f)}`;
  fs.copyFileSync(expand(f), path.join(work, name));
  fontFiles[k] = name;
}

const n = (x) => Number(x.toFixed(4));
const encode = ['-c:v', 'hevc_nvenc', '-preset', 'p6', '-rc', 'constqp', '-qp', preview ? '20' : '13', '-pix_fmt', 'yuv420p',
  '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709', '-tag:v', 'hvc1'];

async function renderClip(clip, index) {
  const target = path.join(work, `clip-${String(index).padStart(2, '0')}-${clip.id}.mp4`);
  const parts = clip.parts || [{ in: clip.in || 0, dur: clip.dur, speed: clip.speed || 1 }];
  const total = parts.reduce((a, p) => a + p.dur, 0);
  const count = Math.round(total * FPS);
  const src = path.join(takes, clip.src);
  if (!fs.existsSync(src)) throw new Error(`clip ${clip.id}: take not found: ${src}`);
  const inputs = ['-i', src];
  const chains = [];
  const labels = [];
  parts.forEach((p, i) => {
    const span = p.dur * (p.speed || 1);
    chains.push(`[0:v]trim=start=${n(p.in)}:duration=${n(span)},setpts=(PTS-STARTPTS)/${n(p.speed || 1)},fps=${FPS}[p${i}]`);
    labels.push(`[p${i}]`);
  });
  let last = 'j';
  chains.push(parts.length > 1 ? `${labels.join('')}concat=n=${parts.length}:v=1:a=0[j]` : `${labels[0]}null[j]`);

  // Frame: fill the output size from the source (a crop center for the vertical cut), then an optional slow zoom.
  const cx = clip.crop?.cx ?? 0.5;
  const cy = clip.crop?.cy ?? 0.5;
  const z0 = clip.zoom?.from ?? 1;
  const z1 = clip.zoom?.to ?? 1;
  const zx = clip.zoom?.cx ?? cx;
  const zy = clip.zoom?.cy ?? cy;
  const zoomExpr = z0 === z1 ? `${z0}` : `(${z0}+(${z1 - z0})*(3*pow(t/${n(total)},2)-2*pow(t/${n(total)},3)))`;
  // Scale so the frame covers W x H at zoom 1, times the zoom; then crop W x H around the center point.
  chains.push(`[${last}]scale=w='ceil(max(${W}/iw,${H}/ih)*iw*${zoomExpr}/2)*2':h='ceil(max(${W}/iw,${H}/ih)*ih*${zoomExpr}/2)*2':eval=frame:flags=lanczos,` +
    `crop=${W}:${H}:'min(max(iw*${zx}-${W / 2},0),iw-${W})':'min(max(ih*${zy}-${H / 2},0),ih-${H})',setsar=1[f]`);
  last = 'f';

  const post = [];
  if (clip.dim) {
    const at = clip.dim.at ?? 0;
    const ramp = clip.dim.ramp ?? 0.8;
    post.push(`drawbox=x=0:y=0:w=iw:h=ih:t=fill:color=black@${clip.dim.amount ?? 0.4}:enable='gte(t,${at + ramp})'`);
    // A short ramp: steps of the same box, so the dim comes in over `ramp` seconds without a second input.
    for (let s = 1; s <= 6; s++) {
      const a = ((clip.dim.amount ?? 0.4) * s / 7).toFixed(3);
      post.push(`drawbox=x=0:y=0:w=iw:h=ih:t=fill:color=black@${a}:enable='between(t,${n(at + ramp * (s - 1) / 6)},${n(at + ramp * s / 6)})'`);
    }
  }
  for (const [ci, cap] of (clip.captions || []).entries()) {
    const size = Math.round((cap.size || 84) * H / 2160);
    const fade = cap.fade ?? 0.35;
    const a0 = cap.at ?? 0.5;
    const a1 = a0 + (cap.dur ?? 2.6);
    const alpha = `if(lt(t,${a0}),0,if(lt(t,${a0 + fade}),(t-${a0})/${fade},if(lt(t,${n(a1 - fade)}),1,if(lt(t,${n(a1)}),(${n(a1)}-t)/${fade},0))))`;
    const lineGap = Math.round(size * 1.28);
    cap.lines.forEach((line, li) => {
      const textFile = `cap-${index}-${ci}-${li}.txt`;
      fs.writeFileSync(path.join(work, textFile), line, 'utf8');
      const font = fontFiles[cap.font || (cap.pos === 'center' ? 'bold' : 'regular')];
      const lines = cap.lines.length;
      let x;
      let y;
      if (cap.pos === 'center') {
        x = '(w-text_w)/2';
        y = `${Math.round(H * (cap.y ?? 0.44))}+${(li - (lines - 1) / 2) * Math.round((cap.lineSizes?.[li] ? cap.lineSizes[li] * H / 2160 : size) * 1.35)}-text_h/2`;
      } else {
        x = `${Math.round(W * 0.05)}`;
        y = `${Math.round(H * 0.865) - (lines - 1 - li) * lineGap}`;
      }
      const thisSize = cap.lineSizes?.[li] ? Math.round(cap.lineSizes[li] * H / 2160) : size;
      const box = cap.pos === 'center' ? '' : `:box=1:boxcolor=0x1d2b44@0.78:boxborderw=${Math.round(size * 0.34)}`;
      post.push(`drawtext=fontfile=${font}:textfile=${textFile}:x=${x}:y=${y}:fontsize=${thisSize}:fontcolor=0xf0fbff:alpha='${alpha}'` +
        `:shadowcolor=black@0.55:shadowx=0:shadowy=${Math.max(2, Math.round(size * 0.05))}${box}`);
    });
  }
  if (clip.fadeIn) post.push(`fade=t=in:st=0:d=${clip.fadeIn}`);
  if (clip.fadeOut) post.push(`fade=t=out:st=${n(total - clip.fadeOut)}:d=${clip.fadeOut}`);
  if (clip.logo) {
    inputs.push('-loop', '1', '-i', expand(clip.logo.file));
    const lh = Math.round((clip.logo.h ?? 0.2) * H / 2) * 2;
    const at = clip.logo.at ?? 0.5;
    chains.push(`[1:v]scale=-2:${lh},format=rgba,fade=t=in:st=${at}:d=0.5:alpha=1[lg]`);
    chains.push(`[${last}]${post.length ? post.join(',') + ',' : ''}null[pp]`);
    chains.push(`[pp][lg]overlay=x=(W-w)/2:y=${Math.round(H * (clip.logo.y ?? 0.2))}:enable='gte(t,${at})':shortest=0[v]`);
  } else {
    chains.push(`[${last}]${post.length ? post.join(',') : 'null'}[v]`);
  }
  await run(FFMPEG, ['-hide_banner', '-loglevel', 'error', '-y', ...inputs, '-filter_complex', chains.join(';'), '-map', '[v]',
    '-frames:v', String(count), '-r', String(FPS), '-an', ...encode, path.basename(target)], work);
  const got = await frames(target);
  if (got !== count) throw new Error(`clip ${clip.id}: ${got} frames, expected ${count} (the take is shorter than in + dur x speed?)`);
  return { file: target, frames: count, seconds: count / FPS };
}

const clips = edit.clips.filter((c) => !c.skip);
const rendered = [];
for (const [i, clip] of clips.entries()) {
  if (only && clip.id !== only) continue;
  const r = await renderClip(clip, i);
  rendered.push({ ...r, clip });
  console.log(`clip ${clip.id}: ${r.frames} frames (${r.seconds.toFixed(3)} s)`);
}
if (only) process.exit(0);

// Pass 2: the join. A clip's `xfade` is the crossfade into the NEXT clip; the pair then overlaps by that long.
const inputs = [];
rendered.forEach((r) => inputs.push('-i', r.file));
const chains = [];
let label = '[0:v]';
let length = rendered[0].seconds;
const starts = [0];
for (let i = 1; i < rendered.length; i++) {
  const x = rendered[i - 1].clip.xfade || 0;
  const outLabel = `[x${i}]`;
  if (x > 0) {
    chains.push(`${label}[${i}:v]xfade=transition=fade:duration=${x}:offset=${n(length - x)}${outLabel}`);
    starts.push(length - x);
    length += rendered[i].seconds - x;
  } else {
    chains.push(`${label}[${i}:v]concat=n=2:v=1:a=0${outLabel}`);
    starts.push(length);
    length += rendered[i].seconds;
  }
  label = outLabel;
}
chains.push(`${label}format=yuv420p[v]`);
const out = expand(edit.out).replace(/\.mp4$/, preview ? '-preview.mp4' : '.mp4');
fs.mkdirSync(path.dirname(out), { recursive: true });
const audio = [];
if (edit.music?.file && fs.existsSync(expand(edit.music.file))) {
  const m = edit.music;
  inputs.push('-i', expand(m.file));
  const fo = m.fadeOut ?? 2.5;
  chains.push(`[${rendered.length}:a]atrim=start=${m.in || 0}:duration=${n(length)},asetpts=PTS-STARTPTS,volume=${m.gainDb ?? 0}dB,afade=t=in:st=0:d=${m.fadeIn ?? 0.05},afade=t=out:st=${n(length - fo)}:d=${fo}[a]`);
  audio.push('-map', '[a]', '-c:a', 'aac', '-b:a', '384k', '-ar', '48000');
} else {
  // A silent track, so players and uploads see an audio stream. The music goes here.
  inputs.push('-f', 'lavfi', '-t', String(n(length)), '-i', 'anullsrc=channel_layout=stereo:sample_rate=48000');
  audio.push('-map', `${rendered.length}:a`, '-c:a', 'aac', '-b:a', '128k');
}
const final = preview
  ? ['-c:v', 'libx264', '-preset', 'veryfast', '-crf', '20', '-pix_fmt', 'yuv420p']
  : ['-c:v', 'libx264', '-preset', 'slow', '-crf', String(edit.crf ?? 15), '-pix_fmt', 'yuv420p', '-profile:v', 'high', '-bf', '2', '-g', String(FPS / 2),
    '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709'];
await run(FFMPEG, ['-hide_banner', '-loglevel', 'error', '-y', ...inputs, '-filter_complex', chains.join(';'), '-map', '[v]', ...audio,
  ...final, '-r', String(FPS), '-movflags', '+faststart', out]);
const total = await frames(out);
console.log(JSON.stringify({ ok: true, out, frames: total, seconds: n(total / FPS), size: `${W}x${H}`,
  clips: rendered.map((r, i) => ({ id: r.clip.id, start: n(starts[i]), seconds: n(r.seconds) })) }));
if (!keep && !preview) { /* clip files stay in the work folder for the short cuts; delete the folder by hand when done */ }
