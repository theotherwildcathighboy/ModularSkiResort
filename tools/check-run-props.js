// Gate: the run prop placer's rules a text check can hold (lessons of the 2026-11-01 placer round).
//  R1 consumers read the tag: plume and night light select SkiRunPropMember and never Secondary, because a placed prop has no owner and an old
//     corner prop is gone after the load sweep. Only the sweep may name Secondary.
//  R2 features only: the sliders are the Snowmaking and Lights features (DECISIONS 577), so nothing that decides snow or upkeep reads a
//     spacing itself: runs read features through SkiRunPropSystem.FeaturesOf (the one place a spacing becomes a feature), and a cat
//     track's switch through CatTrackGunsOn: Off bills nothing (DECISIONS 563) and makes no new snow (568; made snow stays until it melts,
//     572). Every TrackBilledHa call passes CatTrackGunsOn, and the injection reads it twice, for a cat track only: in GetRunSnow, and where
//     BuildMask records a track for measurement (its made-snow model).
//  R3 the old Options sliders stay gone, also from the locale template.
//  R4 the table's weights equal the prefabs' m_Probability (the mix must not drift from the assets), and the empty marker is not placed.
//  R5 the hub's slider constants equal the rules class.
//  R6 a slider step from the hub is a plain component write: no delete and no structural change in the UI phase.
//  R7 cat tracks: billed at the snowmaking rate, kept out of injection (the prefab is named only in the measure-only set), and estimated
//     by the run rule: one RunSnowRules.EstimatedRunShare call for an unmeasured run and one for GetRunSnow, both with the pass's gun
//     season once known (DECISIONS 571); a track's made snow uses only TrackGunShare and TrackColdShare, once each, in StepTrack.
//  R8 house style: Game mode only, ARMED line, one-shot catch, [cost] line, and the tag is added in one place.
//  R9 run types: the count of run kind names per mod file only goes down (systems move to RunFeatures in R2). One exception: the shim
//     file EntityRef.cs rose in R2 for SnowKind, the features-to-kind bridge of the kind-keyed snow code (102 -> 95 names overall; R3
//     billed upkeep by feature: 89).
//  R10 cat track lights bill (DECISIONS 579): every place that bills a cat track (the charge, its per-area split, the hub's rows) adds
//     SkiRunUpkeepRates.TrackLitHa at the lights rate, with the same season gate its guns use (CatTrackGunsCanRun), and the charge's
//     monthly rate includes it.
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
{
  // Every piste surface that can carry a feature slider is walked, its kind from the catalog: a groomed run re-emitted by the Groomed toggle
  // keeps its guns and lights (in game 2026-11-02, the placer had no kind for SkiRunGroomedA and stood nothing on it).
  const ps = code(read(systems, "SkiRunPropSystem.cs"));
  const names = (ps.match(/RunSurfaceNames\s*=\s*\{([^}]*)\}/) || [, ""])[1];
  for (const n of ["RunGroomed", "RunSnowmaking", "RunNight", "RunCatTrack"])
    need(names.includes(`SkiCatalog.Names.${n}`), `R2 SkiRunPropSystem.cs: RunSurfaceNames lacks SkiCatalog.Names.${n}`);
  need(/m_RunKindOf\[prefabEntity\]\s*=\s*\(int\)SkiCatalog\.RunByPrefab\(name\)\.Kind/.test(ps),
    "R2 SkiRunPropSystem.cs: the placer's kinds come from SkiCatalog.RunByPrefab for every run surface");
}

// R2
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
for (const f of ["SkiRunUpkeepSystem.cs", "SkiSnowInjectionSystem.cs", "RunSnowRules.cs", "SnowGunPlumeRules.cs"]) {
  const c = code(read(systems, f));
  need(!/SkiRunPropSettings|TryGetSpacing|GunSpacing|LightSpacing|RunPropPlacerRules/.test(c), `R2 ${f}: reads a prop spacing (read features: SkiRunPropSystem.FeaturesOf)`);
}
const featuresBody = body(placer, "internal RunFeatures FeaturesOf(") || "";
need(featuresBody.includes("SkiRunPropSettings") && featuresBody.split("RunFeatures.FromSpacings(").length === 3,
     "R2 FeaturesOf: must read SkiRunPropSettings and turn the spacings into features with RunFeatures.FromSpacings (with the kind defaults)");
// The one exception: every TrackBilledHa call passes the cat track gun switch (upkeep: CatTrackGunsOn in the call; the hub's cost row:
// the call sits behind `trackGunsOn &&`), and CatTrackGunsOn reads the gun value only.
for (const f of ["SkiRunUpkeepSystem.cs", "SkiResortStatsSystem.cs"]) {
  const c = code(read(systems, f));
  const parts = c.split("TrackBilledHa(").slice(1);
  const gated = parts.filter((p) => p.slice(0, p.indexOf(";")).includes("CatTrackGunsOn(") || p.startsWith("row.AreaM2, true, true)")).length;
  need(parts.length > 0 && parts.length === gated, `R2 ${f}: ${parts.length - gated} of ${parts.length} TrackBilledHa call(s) do not pass the cat track gun switch (CatTrackGunsOn)`);
}
const stats = code(read(systems, "SkiResortStatsSystem.cs"));
need(stats.includes("trackGunsOn = catTrack && m_RunProps.CatTrackGunsOn(row.Entity)") &&
     stats.includes("trackInSeason = m_Injection.CatTrackGunsCanRun(") && stats.includes("if (trackGunsOn && trackInSeason)"),
     "R2 SkiResortStatsSystem.cs: the hub's track cost row is not gated by CatTrackGunsOn");
// The second exception: the injection asks the switch twice, for a cat track only: inside GetRunSnow (passed to the threshold) and where
// BuildMask records a track for measurement (passed to the made-snow model).
const injection = code(read(systems, "SkiSnowInjectionSystem.cs"));
const getRunSnow = body(injection, "internal RunSnow GetRunSnow(") || "";
need(injection.split("CatTrackGunsOn(").length === 3 && getRunSnow.includes("kind != RunKind.CatTrack || m_RunProps.CatTrackGunsOn(run)") &&
     injection.includes("GunsOn = !track || m_RunProps.CatTrackGunsOn(ne),"),
     "R2 SkiSnowInjectionSystem.cs: the cat track gun switch must be read twice, in GetRunSnow and the track's measure record, for a cat track only (DECISIONS 568)");
// R10: cat track lights bill wherever a cat track bills.
const upkeepLit = code(read(systems, "SkiRunUpkeepSystem.cs"));
const litCalls = (c) => c.split("SkiRunUpkeepRates.TrackLitHa(row.Features, row.AreaM2, inSeason)").length - 1;
need(litCalls(upkeepLit) === 2 && upkeepLit.split("inSeason = m_SnowInjectionSystem.CatTrackGunsCanRun(").length - 1 === 2,
     "R10 SkiRunUpkeepSystem.cs: CatTrackHa and the per-area split must each add TrackLitHa with the track's season gate (DECISIONS 579)");
need(upkeepLit.includes("trackLitHa * SkiRunUpkeepRates.LightsStepPerHaMonth"), "R10 SkiRunUpkeepSystem.cs: the monthly rate must include the cat track lights");
need(stats.includes("SkiRunUpkeepRates.TrackLitHa(row.Features, row.AreaM2, trackInSeason)") && stats.includes("work.CostLights += litHa * SkiRunUpkeepRates.LightsStepPerHaMonth"),
     "R10 SkiResortStatsSystem.cs: the hub's Lights row must include the cat track lights (TrackLitHa)");
const onBody = body(placer, "internal bool CatTrackGunsOn(") || "";
need(onBody.includes("m_GunSpacingM") && !onBody.includes("m_LightSpacingM"), "R2 CatTrackGunsOn: must read the gun value only");

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
// The rate is the feature step sum (a cat track with its guns on = groomed + snowmaking = the snowmaking rate; ResortBatch features/steps
// pins every kind's number).
need(upkeep.includes("public static int CostPerHaMonth(RunKind kind) => SkiRunUpkeepRates.PerHaMonth(RunFeatures.FromKind((int)kind));"),
     "R7 CostPerHaMonth must be SkiRunUpkeepRates.PerHaMonth(RunFeatures.FromKind(kind)) (a cat track at the snowmaking rate)");
need(!/Names\.RunCatTrack/.test(injection), "R7 SkiSnowInjectionSystem.cs names the cat track prefab through the catalog (tracks stay out of injection)");
// The track's name lives only in the measure-only array, which is resolved only into the measure-only set.
{
  const arrays = [...injection.matchAll(/string\[\]\s+(\w+)\s*=\s*\{([^}]*)\}/g)].filter((m) => m[2].includes("SkiRunCatTrackA")).map((m) => m[1]);
  need(arrays.length === 1 && arrays[0] === "CatTrackMeasureNames" && injection.split("CatTrackMeasureNames").length === 3 &&
       injection.includes("ResolvePrefabSet(CatTrackMeasureNames, m_TrackMeasurePrefabs);"),
       `R7 SkiSnowInjectionSystem.cs: the cat track name must be in CatTrackMeasureNames alone, resolved into m_TrackMeasurePrefabs (found in ${arrays.join(", ") || "none"})`);
}
// One estimate rule for runs and cat tracks (DECISIONS 571): the table's unmeasured run and GetRunSnow's estimate (every cat track) both
// call RunSnowRules.EstimatedRunShare with this pass's gun season; no other estimate in the injection.
const estimates = injection.split("RunSnowRules.EstimatedRunShare(").slice(1).map((p) => p.slice(0, p.indexOf(";")));
need(estimates.length === 2 && estimates.every((e) => e.includes("m_GunSeasonKnown && m_PassGunSeason")),
     `R7 SkiSnowInjectionSystem.cs: ${estimates.length} RunSnowRules.EstimatedRunShare call(s), expected 2, each with m_GunSeasonKnown && m_PassGunSeason (one estimate rule for runs and cat tracks; no guns credited before the season is known)`);
const stepTrack = body(injection, "private float StepTrack(") || "";
need(injection.split("RunSnowRules.TrackGunShare(").length === 2 && injection.split("RunSnowRules.TrackColdShare(").length === 2 &&
     stepTrack.includes("RunSnowRules.TrackGunShare(m_GunSeasonKnown && m_PassGunSeason, input.GunsOn,") &&
     stepTrack.includes("RunSnowRules.TrackColdShare(") && stepTrack.includes("RunSnowRules.StepTrackMadeSnow("),
     "R7 SkiSnowInjectionSystem.cs: a cat track's made snow must come from TrackGunShare (with the known season and its switch) and TrackColdShare, once each, in StepTrack");
need(!/RunSnowRules\.(EstimatedShare|ThresholdC|EstimateThresholdC)\(/.test(injection),
     "R7 SkiSnowInjectionSystem.cs: an estimate bypasses RunSnowRules.EstimatedRunShare (runs and cat tracks must share one rule)");
need((body(injection, "private void RefreshRunSnow(") || "").includes("RunSnowRules.EstimatedRunShare(") &&
     (body(injection, "private float EstimateShare(") || "").includes("RunSnowRules.EstimatedRunShare("),
     "R7 the run table (RefreshRunSnow) and GetRunSnow's estimate (EstimateShare) must both call RunSnowRules.EstimatedRunShare");

// R8
const load = body(placer, "private void LoadPlacer(") || "";
need(load.indexOf("mode != GameMode.Game") >= 0 && load.indexOf("mode != GameMode.Game") < load.indexOf("m_PlacerLive = true"), "R8 LoadPlacer: Game mode check missing before the placer goes live");
need(placer.includes("placer ARMED"), "R8 no ARMED line");
need(placer.includes("!!!") && placer.includes("m_PlacerErrorLogged"), "R8 no one-shot catch");
need(placer.includes("Props [cost] placer:"), "R8 no [cost] line");
const tagged = fs.readdirSync(systems).filter((f) => f.endsWith(".cs") && code(read(systems, f)).includes("new SkiRunPropMember("));
need(tagged.length === 1 && tagged[0] === "SkiRunPropSystem.Placer.cs", `R8 the tag is added in ${tagged.join(", ") || "no file"}, expected SkiRunPropSystem.Placer.cs alone`);

// R9 run types (DECISIONS 576): the per-kind rules are wrappers over RunFeatures; a system that still names a kind (RunKind.X or a KindX
// constant) moves to features in R2, where its behavior changes. Until then no file may name kinds more often than KIND_READS says, and a
// file not listed may not name one at all. Lower a count when a file drops reads (the gate prints it); never raise one.
// KIND_READS_PRINT=1 prints the current counts.
const KIND_READS = {
  "Systems/CatTrackGradeToolSystem.cs": 1,
  "Systems/EntityRef.cs": 10,   // RunFeatures.FromKind (the shim) and SnowKind (features -> the kind the snow code takes, R2, DECISIONS 577)
  "Systems/PisteMapRules.cs": 1,
  "Systems/QuickRunsToolSystem.cs": 5,
  "Systems/QuickRunsToolSystem.Scan.cs": 1,
  "Systems/RunPropPlacerRules.cs": 6,
  "Systems/RunSnowRules.cs": 3,
  "Systems/SkiCatalog.cs": 5,
  "Systems/SkiResortAttractivenessSystem.cs": 3,
  "Systems/SkiResortScore.cs": 5,
  "Systems/SkiResortStatsSystem.cs": 2,
  "Systems/SkiRunAutoPlanner.Select.cs": 8,
  "Systems/SkiRunBindingRules.cs": 5,
  "Systems/SkiRunCatalogSystem.cs": 1,
  "Systems/SkiRunOwnershipSystem.cs": 2,
  "Systems/SkiRunPropSystem.Placer.cs": 4,
  "Systems/SkiRunUpkeepSystem.cs": 1,
  "Systems/SkiSnowInjectionSystem.cs": 26,
};
const kindRe = /\bRunKind\.(Groomed|Snowmaking|Night|Natural|CatTrack)\b|\bKind(Groomed|Snowmaking|Night|Natural|CatTrack)\b/g;
const modRoot = path.join(repo, "mod", "SkiResortMod");
const kindReads = {};
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (e.name !== "bin" && e.name !== "obj")
        walk(p);
    } else if (e.name.endsWith(".cs")) {
      const n = (code(fs.readFileSync(p, "utf8")).replace(/\/\/.*$/gm, "").match(kindRe) || []).length;
      if (n)
        kindReads[path.relative(modRoot, p).split(path.sep).join("/")] = n;
    }
  }
})(modRoot);
if (process.env.KIND_READS_PRINT)
  console.log(JSON.stringify(kindReads, null, 2));
const kindLower = [];
for (const [f, n] of Object.entries(kindReads)) {
  const max = KIND_READS[f] || 0;
  need(n <= max, `R9 ${f}: names a run kind ${n} time(s), at most ${max} (use RunFeatures and the feature rules, DECISIONS 576)`);
  if (n < max)
    kindLower.push(`${f} ${max} -> ${n}`);
}
for (const f of Object.keys(KIND_READS))
  if (!kindReads[f])
    kindLower.push(`${f} ${KIND_READS[f]} -> 0`);

if (failures.length) {
  console.error("check-run-props: FAIL\n  " + failures.join("\n  "));
  process.exit(1);
}
if (kindLower.length)
  console.log("check-run-props: lower KIND_READS (R9): " + kindLower.join(", "));
console.log(`check-run-props: PASS (${props.length} table rows against the prefabs, 10 rules; cat track Off bills nothing and makes no snow; runs and cat tracks share one estimate rule; ${Object.values(kindReads).reduce((a, b) => a + b, 0)} kind reads in ${Object.keys(kindReads).length} files, none grew)`);
