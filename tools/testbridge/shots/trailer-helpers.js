// Page helpers for filmed takes. Paste tools/testbridge/helpers.js first, then this file (game_eval, once per UI view).
// Each call stays under the 13 s budget of one game_eval: a take is started here and read later with __takeResult().
// The ops behind them: tools/testbridge/README.md, "Filming".

window.__j = async (o) => JSON.parse(await __cmd(o));
window.__sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// The terrain height under a point (the heights op over a 2 m cell).
window.__h = async (x, z) => { const r = await __j({ op: "heights", x0: x - 1, z0: z - 1, x1: x + 1, z1: z + 1, step: 1 }); return r.max; };

// Our buildings (prefab names that start with "Ski") with their positions.
window.__skiBuildings = async () => (await __j({ op: "dump", all: ["Game.Buildings.Building", "Game.Objects.Transform"], none: ["Game.Common.Deleted", "Game.Tools.Temp"], show: ["Game.Objects.Transform"], limit: 20000 }))
  .rows.filter((r) => /^Ski/.test(r.prefab)).map((r) => ({ id: r.entity, p: r.prefab, x: +r.Transform.m_Position.x.toFixed(1), y: +r.Transform.m_Position.y.toFixed(1), z: +r.Transform.m_Position.z.toFixed(1) }));

// The groomers (respawned on every load, so their ids change): entity id and position.
window.__groomers = async () => (await __j({ op: "dump", all: ["Game.Rendering.InterpolatedTransform", "Game.Objects.Transform", "Game.Objects.Stopped"], none: ["Game.Common.Deleted", "Game.Vehicles.Vehicle"], show: ["Game.Objects.Transform"], limit: 500 }))
  .rows.filter((r) => /Groomer/.test(r.prefab)).map((r) => ({ id: r.entity, x: Math.round(r.Transform.m_Position.x), y: Math.round(r.Transform.m_Position.y), z: Math.round(r.Transform.m_Position.z) }));

// One take: `shot` is a shot file's object (tools/testbridge/shots/*.json): { pm: {cam, set}, sim, path: {campath step}, hideUI, pre, post }.
// It enters Photo Mode when the shot has `pm`, places the camera on the path's first key, hides the UI, runs the path and shows the UI
// again. Start the OBS recording before this call and stop it after __takeResult() reports done (tools/testbridge/obs/rec.mjs).
window.__take = async (shot) => {
  if (shot.sim != null) await __cmd({ op: "sim", speed: shot.sim });
  if (shot.pm) {
    await __cmd({ op: "pm", on: true });
    await __sleep(300);
    await __j({ op: "pm", cam: shot.pm.cam, set: shot.pm.set || {} });
    engine.trigger("photoMode.setOverlayHidden", shot.pm.overlay !== true);
  }
  const steps = [{ op: "wait", frames: shot.settle ?? 90 }];
  if (shot.hideUI !== false) steps.push({ op: "ui", hidden: true, seconds: (shot.path.seconds || 5) + 20 });
  steps.push({ op: "wait", frames: shot.pre ?? 45 }, Object.assign({ op: "campath" }, shot.path), { op: "wait", frames: shot.post ?? 45 });
  if (shot.hideUI !== false) steps.push({ op: "ui", hidden: false });
  return __j({ op: "seq", steps });
};
window.__takeResult = async () => { const s = await __j({ op: "seqStatus" }); return { done: s.done, error: s.error, campath: (s.results.find((r) => r.campath) || {}).campath }; };

// Leave Photo Mode: overlay icons back, every property reset.
window.__pmOff = async () => { engine.trigger("photoMode.setOverlayHidden", false); await __cmd({ op: "pm", reset: true }); await __cmd({ op: "pm", on: false }); await __sleep(300); return "pm off"; };

// One logged terrain edit of several strokes. o: { prefab, size (1 to 10), strength (0.35 or less), height (world y, Level and Slope),
// pts: [[x, z], ...], frames (the hold per stroke, default 15), note, snapDir, log }. The camera must show every point.
// It returns the op's `end` reply: changed texels, the largest raise and lowering, the snapshot ids.
window.__terr = async (o) => {
  const cx = o.pts.reduce((a, p) => a + p[0], 0) / o.pts.length, cz = o.pts.reduce((a, p) => a + p[1], 0) / o.pts.length;
  const ext = Math.max(...o.pts.map((p) => Math.max(Math.abs(p[0] - cx), Math.abs(p[1] - cz))));
  const steps = [{ op: "terrain", action: "begin", prefab: o.prefab, size: o.size, strength: o.strength, height: o.height, wx: cx, wz: cz, r: o.r ?? (ext + o.size * 2 + 12), snapDir: o.snapDir, log: o.log, note: o.note || "" }];
  for (const p of o.pts) steps.push({ op: "move", wx: p[0], wz: p[1] }, { op: "wait", frames: 6 }, { op: "down" }, { op: "wait", frames: o.frames ?? 15 }, { op: "up" }, { op: "wait", frames: 4 });
  steps.push({ op: "wait", frames: 15 }, { op: "terrain", action: "end" });
  await __j({ op: "seq", steps });
  for (let i = 0; i < 120; i++) {
    await __sleep(100);
    const s = await __j({ op: "seqStatus" });
    if (!s.done) continue;
    if (s.error) { await __j({ op: "terrain", action: "cancel" }); return { error: s.error }; }
    return s.results[s.results.length - 1].terrain;
  }
  return { error: "timeout: read seqStatus, then {op:\"terrain\",action:\"end\"} or cancel" };
};

// Move It > Toolbox > "To Terrain Height" on the object at a world point, through Move It's own panel with real clicks (Move It must be
// in the playset: it is a staging tool, nothing in the mod needs it). The camera must show the point, tilt 50 to 60. It returns the
// object under the cursor and its height before and after. Move It's own Undo button takes the step back.
window.__moveItToTerrain = async (x, y, z) => {
  const icon = () => { let f = null; document.querySelectorAll("img").forEach((i) => { const m = /MoveIt_(Off|Active)\.svg/.exec(i.getAttribute("src") || ""); const r = i.getBoundingClientRect(); if (m && r.width > 0 && r.top > 1950) f = { on: m[1] === "Active", at: [Math.round(r.left + r.width / 2), Math.round(r.top + r.height / 2)] }; }); return f; };
  let b = icon();
  if (!b) return { error: "no Move It button: is Move It in the playset?" };
  if (!b.on) { await __clk(b.at[0], b.at[1], [{ op: "wait", frames: 30 }]); await __sleep(400); }
  if ((await __j({ op: "hover" })).tool !== "MoveItTool") return { error: "the Move It tool did not open" };
  const pick = JSON.parse(await __st({ op: "seq", steps: [{ op: "move", wx: x, wy: y, wz: z }, { op: "wait", frames: 15 }, { op: "hover" }, { op: "click", after: 20 }] }));
  if (pick.error) return { error: pick.error };
  const hit = pick.results[0].hover.hit;
  if (!hit || !hit.owner) return { error: "nothing under the cursor" };
  const height = async () => (await __j({ op: "entity", id: hit.owner, only: ["Game.Objects.Transform"] })).components["Game.Objects.Transform"].m_Position.y;
  const before = await height();
  if (!__find("To Terrain Height").length) { const t = __find("Toolbox")[0]; if (!t) return { error: "no Toolbox row in the Move It panel" }; await __clk(t[0], t[1], [{ op: "wait", frames: 25 }]); await __sleep(400); }
  const tth = __find("To Terrain Height")[0];
  if (!tth) return { error: "no To Terrain Height row" };
  await __clk(tth[0], tth[1], [{ op: "wait", frames: 40 }]);
  await __sleep(500);
  const after = await height();
  await __st({ op: "seq", steps: [{ op: "key", keys: "esc", after: 20 }] });
  return { entity: hit.owner, prefab: hit.ownerPrefab, before, after, terrain: await __h(x, z), tool: (await __j({ op: "hover" })).tool };
};

// Exit from the pause menu (two steps on game 1.6.4f1: "Exit", then "Exit To Desktop"). Nothing is saved.
window.__exitToDesktop = async () => {
  for (let i = 0; i < 3 && !__find("Exit").length; i++) { await __st({ op: "seq", steps: [{ op: "key", keys: "esc", after: 30 }] }); await __sleep(400); }
  const x = __find("Exit")[0];
  if (!x) return "no Exit row";
  await __clk(x[0], x[1], [{ op: "wait", frames: 30 }]);
  await __sleep(500);
  const d = __find("Exit To Desktop")[0];
  if (!d) return "no exit dialog";
  __clk(d[0], d[1]);
  return "exiting";
};
