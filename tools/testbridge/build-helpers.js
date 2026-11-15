// Generic build helpers for player-like city building through the bridge `tool` op + real clicks (citybuilder round).
// __net(prefab, pts): a straight net (road, pipe, power line) through the points, one click per point, Esc at the end.
window.__net = async (prefab, pts, mode = "Straight") => {
  const t = JSON.parse(await __cmd({ op: "tool", prefab, mode })); if (!t.ok) return "tool " + JSON.stringify(t);
  const steps = []; for (const p of pts) steps.push({ op: "move", wx: p[0], wz: p[1], frames: 3 }, { op: "wait", frames: 10 }, { op: "click", after: 12 });
  steps.push({ op: "key", keys: "esc", after: 10 });
  const r = JSON.parse(await __st({ op: "seq", steps })); await __cmd({ op: "tool" });
  return prefab + " " + (r.error || "ok");
};
// __put(prefab, x, z): hover, optional rotation by mouse wheel-free keys is not used; returns what the tool hit + the result.
window.__put = async (prefab, x, z) => {
  const t = JSON.parse(await __cmd({ op: "tool", prefab })); if (!t.ok) return "tool " + JSON.stringify(t);
  const r = JSON.parse(await __st({ op: "seq", steps: [{ op: "move", wx: x, wz: z, frames: 3 }, { op: "wait", frames: 20 }, { op: "hover" }, { op: "click", after: 20 }, { op: "key", keys: "esc", after: 10 }] }));
  await __cmd({ op: "tool" });
  const h = (r.results[0] || {}).hover || {};
  return prefab + " " + (r.error || "ok") + " hit " + JSON.stringify(h.hit || h).slice(0, 160);
};
window.__count = async (comp, match) => { const r = JSON.parse(await __cmd({ op: "dump", all: [comp], limit: 500 })); return (r.rows || []).filter(x => !match || new RegExp(match).test(x.prefab)).map(x => x.prefab + "@" + (x.entity || x.id || "")).join(", ") + " (" + r.count + ")"; };
// Bulldoze the object at (x, z) with the vanilla bulldozer (B), confirming the tooltip's "Yes" when it asks.
window.__doze = async (x, z) => {
  await __st({ op: "seq", steps: [{ op: "key", keys: "b", after: 10 }, { op: "move", wx: x, wz: z, frames: 3 }, { op: "wait", frames: 15 }, { op: "click", after: 30 }] });
  await new Promise(r => setTimeout(r, 400));
  const y = [...document.querySelectorAll("button")].filter(e => /^Yes$/i.test((e.textContent || "").trim()) && e.getBoundingClientRect().width > 0);
  let res = "no confirm";
  if (y.length) { const r = y[0].getBoundingClientRect(); await __clk(Math.round(r.x + r.width / 2), Math.round(r.y + r.height / 2)); res = "confirmed"; }
  await new Promise(r => setTimeout(r, 400)); await __st({ op: "seq", steps: [{ op: "key", keys: "esc", after: 10 }] }); await __cmd({ op: "tool" });
  return res;
};
// Hub lift station tile opener for __placeYaw: Lifts tab, the lift type chip, then the tile (Base station / Top station).
window.__stTile = (type, tile) => async () => { const lt = __tile("Lifts", 1000); if (lt) { await __clk(lt[0], lt[1]); await new Promise(r => setTimeout(r, 300)); } const ty = __tile(type); if (ty) { await __clk(ty[0], ty[1]); await new Promise(r => setTimeout(r, 300)); } const t = __tile(tile); if (t) { await __clk(t[0], t[1]); await new Promise(r => setTimeout(r, 500)); } };
window.__bearing = (a, b) => Math.atan2(b[0] - a[0], b[1] - a[1]) * 180 / Math.PI;
// Hub Buildings tab: open it only if the chip row is not showing, pick the chip (Lodges / Services), then place the tile at (x, z).
window.__hubB = async (chip, tile, x, z) => {
  if (!__tile(chip)) { const b = __tile("Buildings", 1000); if (b) { await __clk(b[0], b[1]); await new Promise(r => setTimeout(r, 500)); } }
  const c = __tile(chip); if (!c) return "nf chip " + chip; await __clk(c[0], c[1]); await new Promise(r => setTimeout(r, 400));
  const t = __tile(tile); if (!t) return "nf tile " + tile + " :: " + __all(2800, 1540).slice(0, 300); await __clk(t[0], t[1]); await new Promise(r => setTimeout(r, 400));
  const r = JSON.parse(await __st({ op: "seq", steps: [{ op: "move", wx: x, wz: z, frames: 3 }, { op: "wait", frames: 20 }, { op: "hover" }] }));
  const h = (r.results[0] || {}).hover || {}; const tip = __tips().split(" | ").filter(s => !/Anarchy|Snapping|Megalopolis|Topography/.test(s)).slice(-6).join(" | ");
  if (h.mouseOverUI) return tile + " UI at target";
  const r2 = JSON.parse(await __st({ op: "seq", steps: [{ op: "click", after: 20 }, { op: "key", keys: "esc", after: 10 }] }));
  return tile + " " + h.prefab + " " + (r2.error || "ok") + " :: " + tip;
};
// Cat track (hub Runs tab, "Cat track" tile): a fresh tool, click the points, read the tool's verdict. enter=true builds it.
window.__track = async (pts, enter = false) => {
  for (let i = 0; i < 4; i++) await __st({ op: "seq", steps: [{ op: "key", keys: "esc", after: 6 }] }); await __cmd({ op: "tool" });
  const r0 = __tile("Runs", 1000); if (r0 && !__tile("Cat track")) { await __clk(r0[0], r0[1]); await new Promise(r => setTimeout(r, 900)); }
  const t = __tile("Cat track"); if (!t) return "nf Cat track"; await __clk(t[0], t[1]); await new Promise(r => setTimeout(r, 400));
  const steps = []; for (const [x, z] of pts) steps.push({ op: "move", wx: x, wz: z, frames: 3 }, { op: "wait", frames: 10 }, { op: "click", after: 10 });
  steps.push({ op: "wait", frames: 10 }); await __st({ op: "seq", steps });
  const tip = __tips().split(" | ").filter(x => !/Anarchy|Snapping|Megalopolis|Topography|Click to add/.test(x)).slice(-4).join(" | ");
  if (!enter) return tip;
  await __st({ op: "seq", steps: [{ op: "key", keys: "enter", after: 30 }, { op: "key", keys: "esc", after: 10 }] }); await __cmd({ op: "tool" });
  return "entered :: " + tip;
};
