// bodycompare.js <old LiftLoopCache.cs> <new LiftLoopCache.cs>
// Round 3 (skeptic MUST 2): the Loop geometry, Sample and closure bodies of LiftLoopCache moved verbatim.
// Extracts members by signature (brace matching from the signature line) and compares the text after an explicit,
// listed token map (the chain type moved to LiftLoopTopology). Exit code = number of mismatches.
const fs = require('fs');
const [oldPath, newPath] = process.argv.slice(2);
const oldText = fs.readFileSync(oldPath, 'utf8'), newText = fs.readFileSync(newPath, 'utf8');

function member(text, signatureStart, nth = 0) {
  let at = -1;
  for (let k = 0; k <= nth; k++) {
    at = text.indexOf(signatureStart, at + 1);
    if (at < 0) return null;
  }
  const lineStart = text.lastIndexOf('\n', at) + 1;
  const open = text.indexOf('{', at);
  let depth = 0, i = open;
  for (; i < text.length; i++) {
    if (text[i] === '{') depth++;
    else if (text[i] === '}') { depth--; if (depth === 0) break; }
  }
  return text.slice(lineStart, i + 1);
}
function from(text, marker) { const i = text.indexOf(marker); return i < 0 ? null : text.slice(i); }

const RENAMES = [
  [/\bChainLink link = chain\.m_Links\[i\];/g, 'LiftLoopTopology.Link link = chain.Links[i];'],
  [/chain\.m_Links\.Count/g, 'chain.Links.Count'],
  [/lanes\[link\.m_LaneIndex\]/g, 'lanes[link.Lane]'],
  [/link\.m_Reversed/g, 'link.Reversed'],
  [/List<CableLane> lanes, Chain chain, Loop loop/g, 'List<CableLane> lanes, LiftLoopTopology.Chain chain, Loop loop'],
];
const rename = s => RENAMES.reduce((acc, [re, to]) => acc.replace(re, to), s);

const checks = [
  ['struct CableLane', 'internal struct CableLane', 'internal struct CableLane', s => s],
  ['struct Segment', 'internal struct Segment', 'internal struct Segment', s => s],
  ['class Loop', 'internal sealed class Loop', 'internal sealed class Loop', s => s],
  ['struct ChainDiag', 'internal struct ChainDiag', 'internal struct ChainDiag', s => s],
  ['AppendTerminalTurn', 'private static bool AppendTerminalTurn(', 'private static bool AppendTerminalTurn(', s => s],
  ['Fmt', 'private static string Fmt(', 'private static string Fmt(', s => s],
  ['AppendLine (Loop)', 'private static void AppendLine(float3 a, float3 b, Loop loop', 'private static void AppendLine(float3 a, float3 b, Loop loop', s => s],
  ['AppendArc', 'private static bool AppendArc(', 'private static bool AppendArc(', s => s],
  ['ProjectOnOwner (3 args)', 'internal static double ProjectOnOwner(Loop loop, Entity owner, float3 position)\n', 'internal static double ProjectOnOwner(Loop loop, Entity owner, float3 position)\n', s => s],
  ['ProjectOnOwner (distance)', 'internal static double ProjectOnOwner(Loop loop, Entity owner, float3 position, out float distance)', 'internal static double ProjectOnOwner(Loop loop, Entity owner, float3 position, out float distance)', s => s],
  ['AppendChain (Loop) after renames', 'private void AppendChain(List<CableLane> lanes, Chain chain, Loop loop', 'private void AppendChain(List<CableLane> lanes, LiftLoopTopology.Chain chain, Loop loop', rename],
];
let bad = 0;
for (const [name, oldSig, newSig, norm] of checks) {
  const a = member(oldText, oldSig), b = member(newText, newSig);
  const ok = a != null && b != null && norm(a) === b;
  if (!ok) bad++;
  console.log((ok ? 'SAME   ' : 'DIFFER ') + name);
}
// Sample(Loop)'s body → SampleSegments: identical from the binary-search comment on, minus `var segments = loop.m_Segments;`.
{
  const a = member(oldText, 'public static void Sample(Loop loop'), b = member(newText, 'private static void SampleSegments(');
  const body = s => s.slice(s.indexOf('// Binary search')).replace(/\n\s*var segments = loop\.m_Segments;/, '');
  const ok = a && b && body(a) === body(b);
  if (!ok) bad++;
  console.log((ok ? 'SAME   ' : 'DIFFER ') + 'Sample body → SampleSegments (minus the segments local)');
}
// TryCloseLoop's geometry: from "double cum = 0.0;" to the end of the method.
{
  const a = member(oldText, 'private bool TryCloseLoop('), b = member(newText, 'private bool TryCloseLoop(');
  const geo = s => s.slice(s.indexOf('double cum = 0.0;'));
  const ok = a && b && geo(a) === geo(b);
  if (!ok) bad++;
  console.log((ok ? 'SAME   ' : 'DIFFER ') + 'TryCloseLoop geometry (:432-453: chain A, top turn, chain B, base turn, notes, length)');
}
// Build's loop tail (from the loop identity lines to the "loop closed" note): identical text.
{
  const tail = s => { const i = s.indexOf('loop.m_BaseStation = stations[baseStation];'); const j = s.indexOf('uphill, {(decided ? swap', i); return i < 0 || j < 0 ? null : s.slice(i, s.indexOf('\n', j)); };
  const ok = tail(oldText) != null && tail(oldText).replace('CommonType(lanes, first, second)', 'pair.TypeIndex') === tail(newText);
  if (!ok) bad++;
  console.log((ok ? 'SAME   ' : 'DIFFER ') + 'Build loop tail (identity, type, mixes note, loop closed note; CommonType(...) → pair.TypeIndex)');
}
// Note texts that moved to the pair loop: present verbatim in the new file.
for (const lit of ['chain(s), need exactly 2', 'closure failed — {why}', 'terminal gap out of range (top {', 'want 1–10)', 'mixes lift types — not moving', 'top turn: {topNote}; base turn: {baseNote}', 'top turn degenerate: ', 'base turn degenerate: ', 'loop too short (']) {
  const ok = oldText.includes(lit) && newText.includes(lit);
  if (!ok) bad++;
  console.log((ok ? 'SAME   ' : 'DIFFER ') + 'note literal "' + lit + '"');
}
console.log(bad === 0 ? 'ALL SAME' : bad + ' DIFFER');
process.exitCode = bad;
