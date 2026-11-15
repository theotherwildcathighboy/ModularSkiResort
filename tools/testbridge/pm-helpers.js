// Placemaker (pdx 163638, 1.4.2) helpers for the citybuilder round. Load after helpers.js.
window.__pmState = null;
if (!window.__pmSub) { window.__pmSub = true; engine.on("Placemaker.state.update", s => { try { window.__pmState = JSON.parse(s); } catch (e) { window.__pmState = { raw: s }; } }); engine.trigger("Placemaker.state.subscribe"); }
window.__pmS = () => { const s = window.__pmState || {}; return JSON.stringify({ open: s.open, stage: s.stage, mode: s.areaMode, area: s.area, primary: s.primary, planning: s.planning, pp: s.planningProgress, bp: s.buildProgress }); };
// Click a polygon on terrain: corners [[x,z],...], then the first corner again to close.
window.__pmOutline = async (pts) => { const steps = []; for (const [x, z] of pts) steps.push({ op: "move", wx: x, wz: z, frames: 3 }, { op: "wait", frames: 6 }, { op: "click", after: 8 }); steps.push({ op: "move", wx: pts[0][0], wz: pts[0][1], frames: 3 }, { op: "wait", frames: 6 }, { op: "click", after: 15 }); return __st({ op: "seq", steps }); };
window.__pmWait = (pred, ms = 60000) => new Promise(r => { const t0 = Date.now(); const p = () => { const s = window.__pmState || {}; if (pred(s)) r("ok " + __pmS()); else if (Date.now() - t0 > ms) r("timeout " + __pmS()); else setTimeout(p, 250); }; p(); });
// One Placemaker build: aim the camera at the outline's center (upper half of the screen, clear of the panel), select the recipe and
// preset, click the outline, wait for planning, build, wait until the tool is back at the area stage.
window.__pmBuild = async (recipe, preset, pts, zoom = 1800) => {
  const cx = pts.reduce((a, p) => a + p[0], 0) / pts.length, cz = pts.reduce((a, p) => a + p[1], 0) / pts.length;
  const y = JSON.parse(await __cmd({ op: "heights", x0: cx, z0: cz, x1: cx, z1: cz, step: 10 })).min || 600;
  await __aim(cx, y, cz, zoom, 60, 0, 1920, 620);
  for (const [x, z] of pts) { const j = JSON.parse(await __cmd({ op: "project", wx: x, wz: z })); if (!j.safe || j.client[1] > 1200) return "corner off-safe " + x + "," + z + " " + JSON.stringify(j.client); }
  engine.trigger("Placemaker.selectRecipe", recipe); await new Promise(r => setTimeout(r, 400));
  if (preset) { engine.trigger("Placemaker.selectPreset", recipe === "roads" ? "roads" : "zoning", preset); await new Promise(r => setTimeout(r, 400)); }
  await __pmOutline(pts); await new Promise(r => setTimeout(r, 1500));
  const w1 = await __pmWait(s => !s.planning, 30000);
  const s = window.__pmState || {}; if (!(s.primary && s.primary.enabled && s.primary.action === "build")) return "not ready " + w1;
  engine.trigger("Placemaker.primary"); await new Promise(r => setTimeout(r, 1500));
  return "built " + recipe + "/" + preset + " " + Math.round(s.area.areaM2) + " m2 :: " + (await __pmWait(st => st.stage === "area", 30000)).slice(0, 60);
};
