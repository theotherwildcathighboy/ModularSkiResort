// Gate: the run prop placer's rules a text check can hold (lessons of the 2026-11-01 placer round).
//  R1 consumers read the tag: plume and night light select SkiRunPropMember and never Secondary, because a placed prop has no owner and an old
//     corner prop is gone after the load sweep. Only the sweep may name Secondary.
//  R2 look only: nothing that decides snow or upkeep reads a spacing.
//  R3 the old Options sliders stay gone, also from the locale template.
//  R4 the table's weights equal the prefabs' m_Probability (the mix must not drift from the assets), and the empty marker is not placed.
//  R5 the hub's slider constants equal the rules class.
//  R6 a slider step from the hub is a plain component write: no delete and no structural change in the UI phase.
//  R7 cat tracks: billed at the snowmaking rate, and kept out of injection (the prefab is in no injected set).
//  R8 house style: Game mode only, ARMED line, one-shot catch, [cost] line, and the tag is added in one place.
"use strict";
const fs = require("fs");
const path = require("path");

const repo = path.join(__dirname, "..");
const systems = path.join(repo, "mod", "SkiResortMod", "Systems");
const read = (...p) => fs.readFileSync(path.join(...p), "utf8");
const code = (text) => text.split(/\r?\n/).filter((l) => !l.trim().startsWith("//") && !l.trim().startsWith("///")).join("\n");
const failures = [];
const need = (ok, msg) => { if (!ok) failures.push(msg); };

// R1
for (const f of ["SkiSnowGunPlumeSystem.cs", "SkiNightLightSystem.cs"]) {
  const c = code(read(systems, f));
  need(c.includes("SkiRunPropMember"), `R1 ${f}: does not read the SkiRunPropMember tag`);
  need(!/\bSecondary\b/.test(c), `R1 ${f}: reads Secondary (placed props are free-standing)`);
}
need(code(read(systems, "SkiRunSurfaceSystem.cs")).includes("SkiRunPropMember"), "R1 SkiRunSurfaceSystem.cs: placed props get no snow dress");
const placer = read(systems, "SkiRunPropSystem.Placer.cs");
need(code(placer).split("Game.Objects.Secondary").length === 2, "R1 SkiRunPropSystem.Placer.cs: Secondary belongs to the load sweep only (one use)");
need(!/\bSecondary\b/.test(code(read(systems, "SkiRunPropSystem.cs"))), "R1 SkiRunPropSystem.cs: reads Secondary (the census counts the tag)");

// R2
for (const f of ["SkiRunUpkeepSystem.cs", "SkiSnowInjectionSystem.cs", "RunSnowRules.cs", "SnowGunPlumeRules.cs"]) {
  const c = code(read(systems, f));
  need(!/SkiRunPropSettings|TryGetSpacing|GunSpacing|LightSpacing|RunPropPlacerRules/.test(c), `R2 ${f}: reads a prop spacing (sliders are look only)`);
}

// R3
for (const p of [["mod", "SkiResortMod", "Settings", "SkiResortSettings.cs"], ["mod", "SkiResortMod", "Settings", "LocaleEN.cs"], ["locale", "en-US.json"]]) {
  need(!/SnowGunDensityPct|FloodlightDensityPct/.test(read(repo, ...p)), `R3 ${p.join("/")}: an old density slider is back`);
}

// R4
const rules = read(systems, "RunPropPlacerRules.cs");
const props = [...rules.matchAll(/new PlacerProp\("(\w+)", RunPropDensityRules\.Class\w+, (\d+)\)/g)];
need(props.length === 13, `R4 the table has ${props.length} entries, expected 13`);
for (const [, name, weight] of props) {
  need(name !== "SkiPropNone", "R4 the empty marker SkiPropNone is in the table");
  const file = path.join(repo, "gamedata-mirror", "ImportedData", name, `${name}.Prefab`);
  if (!fs.existsSync(file)) { failures.push(`R4 ${name}: no prefab in gamedata-mirror`); continue; }
  const m = read(file).match(/"m_Probability"\s*:\s*(\d+)/);
  need(m && m[1] === weight, `R4 ${name}: table weight ${weight}, prefab m_Probability ${m ? m[1] : "none"}`);
}

// R5
const ts = read(repo, "mod", "SkiResortUI", "src", "bindings.ts");
const cs = (n) => (rules.match(new RegExp(`const int ${n} = (\\d+);`)) || [])[1];
const tv = (n) => (ts.match(new RegExp(`export const ${n} = (\\d+);`)) || [])[1];
for (const [a, b] of [["SpacingOff", "RUN_PROP_SPACING_OFF"], ["SpacingMinM", "RUN_PROP_SPACING_MIN"], ["SpacingMaxM", "RUN_PROP_SPACING_MAX"], ["SpacingStepM", "RUN_PROP_SPACING_STEP"]]) {
  need(cs(a) !== undefined && cs(a) === tv(b), `R5 ${a} ${cs(a)} differs from bindings.ts ${b} ${tv(b)}`);
}

// R6
const body = (text, signature) => {
  const at = text.indexOf(signature);
  if (at < 0) return null;
  const open = text.indexOf("{", at);
  let depth = 0;
  for (let i = open; i < text.length; i++) {
    if (text[i] === "{") depth++;
    if (text[i] === "}" && --depth === 0) return text.slice(open, i + 1);
  }
  return null;
};
const banned = /AddComponent|RemoveComponent|DestroyEntity|CreateEntity|DeleteLikeBulldozer|DeleteMembersWhere/;
const setSpacing = body(placer, "internal bool SetSpacing(");
need(setSpacing && !banned.test(setSpacing), "R6 SetSpacing: only a component write may run from the hub's phase");
const noteRun = body(placer, "private void NoteRun(");
need(noteRun && !banned.test(noteRun), "R6 NoteRun (called by SetSpacing): only reads and queues");
const hub = read(systems, "SkiResortHubUISystem.cs");
const applySpacing = body(hub, "private void ApplyRunPropSpacing(");
need(applySpacing && !banned.test(applySpacing), "R6 ApplyRunPropSpacing: only stores the value");

// R7
const upkeep = code(read(systems, "SkiRunUpkeepSystem.cs"));
need(/case RunKind\.CatTrack:[\s\S]{0,120}?SnowmakingCostPerHaMonth/.test(body(upkeep, "public static int CostPerHaMonth(") || ""), "R7 CostPerHaMonth(CatTrack) is not the snowmaking rate");
need(!/Names\.RunCatTrack/.test(code(read(systems, "SkiSnowInjectionSystem.cs"))), "R7 SkiSnowInjectionSystem.cs names the cat track prefab (tracks stay out of injection)");

// R8
const load = body(placer, "private void LoadPlacer(") || "";
need(load.indexOf("mode != GameMode.Game") >= 0 && load.indexOf("mode != GameMode.Game") < load.indexOf("m_PlacerLive = true"), "R8 LoadPlacer: Game mode check missing before the placer goes live");
need(placer.includes("placer ARMED"), "R8 no ARMED line");
need(placer.includes("!!!") && placer.includes("m_PlacerErrorLogged"), "R8 no one-shot catch");
need(placer.includes("Props [cost] placer:"), "R8 no [cost] line");
const tagged = fs.readdirSync(systems).filter((f) => f.endsWith(".cs") && code(read(systems, f)).includes("new SkiRunPropMember("));
need(tagged.length === 1 && tagged[0] === "SkiRunPropSystem.Placer.cs", `R8 the tag is added in ${tagged.join(", ") || "no file"}, expected SkiRunPropSystem.Placer.cs alone`);

if (failures.length) {
  console.error("check-run-props: FAIL\n  " + failures.join("\n  "));
  process.exit(1);
}
console.log(`check-run-props: PASS (${props.length} table rows against the prefabs, 8 rules)`);
