// Low-voltage feed to a ski lift base station's SkiLiftMarkerLV stub.
// Learned 2026-11-15: start the line ON the marker node, leave sideways (perpendicular to the lift bearing; along the bearing
// the line hits the lift cable "Clearance too low", backwards it overlaps the station), then run to a powered road.
// Where the overhead line is refused all around (a station on a steep shore), prefab "Low-voltage Ground Cable" passes.
// pts: [[x,z], ...] after the marker. Returns the tooltip tail per point and the station's consumer state.
window.__lvFeed = async (marker, pts, stationMatch, prefab = "Low-voltage Line") => {
  const tip = () => { const t = __tips().split(" | "); const err = t.filter(s => /Overlapping|Clearance|In water|steep|Not enough|required|Invalid/i.test(s)); return (err.length ? "ERR " + err.join(", ") + " | " : "") + t.slice(-3).join(" | "); };
  const out = [];
  await __cmd({ op: "tool", prefab, mode: "Straight" });
  await __st({ op: "seq", steps: [{ op: "move", wx: marker[0], wz: marker[1], frames: 3 }, { op: "wait", frames: 12 }, { op: "click", after: 12 }] });
  for (const p of pts) {
    await __st({ op: "seq", steps: [{ op: "move", wx: p[0], wz: p[1], frames: 3 }, { op: "wait", frames: 15 }] });
    out.push(p + ": " + tip());
    await __st({ op: "seq", steps: [{ op: "click", after: 15 }] });
  }
  await __cmd({ op: "tool" });
  await new Promise(r => setTimeout(r, 8000));
  const b = (await __bld()).filter(x => new RegExp(stationMatch).test(x.p) && /Base/.test(x.p))[0];
  const u = b ? JSON.parse(await __cmd({ op: "util", id: b.id })) : null;
  return out.join("\n") + "\n=> " + JSON.stringify(u && u.ElectricityConsumer) + " " + JSON.stringify(u && u.icons);
};
