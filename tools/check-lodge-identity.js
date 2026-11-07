#!/usr/bin/env node
// Gate LODGEID (merge asset P1, EngineNotes/SkiMergeSystem.md "Identity"): a merged building is a lodge that our prefab catalog does not
// know. Every prefab-keyed lodge lookup (IsLodge, LodgeDefs, LodgePrefabSet) is one of two kinds:
//   - A path that must also see merged buildings. It goes through LodgeIdentity.Resolve or the attractiveness system's merged views.
//   - A path that stays prefab-only on purpose.
// The reviewed sites are counted per file below. A new site fails until someone routes it or adds it here with its reason.
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..', 'mod', 'SkiResortMod', 'Systems');
const LOOKUP = /\.IsLodge\(|\.LodgeDefs\b|\.LodgePrefabSet\b/;
const REVIEWED = {
    'LodgeNumberSystem.cs': [2, 'numbering: a merged building keeps its own name'],
    'LodgeRoadSystem.cs': [1, 'road rules: a merged building needs no road'],
    'LodgeSupplySystem.cs': [1, 'supply chain: a merged building roots none'],
    'MountainJobsSystem.cs': [2, 'merged views are read before the catalog (SignJobsOf, SeasonalSignJobsOf)'],
    'OffSeasonStaffSystem.cs': [2, "off season: a merged building's own WorkProvider is never touched"],
    'SkiAreaRingOverlaySystem.cs': [1, 'parking ring while placing: the object tool places our own lodge prefabs; a merged building has no parking row'],
    'SkiGuestCounterSystem.cs': [1, 'guests: a merged building is not a guest destination in v1'],
    'SkiLiftTicketSystem.cs': [1, 'lodge spend: not a guest destination in v1'],
    'SkiLodgeSpotDiag.cs': [1, "diagnostics of our lodges' spots"],
    'SkiResortAttractivenessSystem.cs': [5, 'the catalog walk; merged buildings join through MergedDefOf'],
    'SkiResortAttractivenessSystem.Merged.cs': [1, 'skips our own prefabs'],
    'SkiResortStatsSystem.cs': [3, 'the ladder goes through LodgeIdentity; merged rows are appended from the merged views'],
};

function walk(dir) {
    return fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) =>
        e.isDirectory() ? walk(path.join(dir, e.name)) : e.name.endsWith('.cs') ? [path.join(dir, e.name)] : []);
}

const files = walk(root);
const fails = [];
let sites = 0;
for (const file of files) {
    const rel = path.relative(root, file).replace(/\\/g, '/');
    if (rel === 'SkiPrefabCatalogSystem.cs')
        continue;   // the owner of the lookups
    const lines = [];
    fs.readFileSync(file, 'utf8').split(/\r?\n/).forEach((line, i) => {
        if (LOOKUP.test(line.replace(/\/\/.*$/, '')))
            lines.push(`${rel}:${i + 1}: ${line.trim()}`);
    });
    sites += lines.length;
    const want = REVIEWED[rel] ? REVIEWED[rel][0] : 0;
    if (lines.length > want) {
        fails.push(`${rel}: ${lines.length} prefab-keyed lodge lookups, ${want} reviewed. Route a new one through LodgeIdentity.Resolve ` +
            'or the merged views, or add it to REVIEWED in tools/check-lodge-identity.js with its reason:');
        fails.push(...lines.map((l) => '  ' + l));
    } else if (lines.length < want) {
        fails.push(`${rel}: ${lines.length} prefab-keyed lodge lookups, ${want} reviewed: lower the count in REVIEWED`);
    }
}
for (const f of fails)
    console.log(f);
if (fails.length > 0)
    process.exit(1);
console.log(`${files.length} system files, ${sites} prefab-keyed lodge lookups, all reviewed for merged buildings`);
