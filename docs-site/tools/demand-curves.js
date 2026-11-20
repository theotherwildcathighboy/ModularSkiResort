#!/usr/bin/env node
// Demand-curve graphs and numbers for the "How demand works" page. The curves are computed from the constants and formulas in the C#
// rules (SkiDayRules, SkiMoneyRules, SkiPassRules, SkiCatalog), so a tuning change redraws them.
//
// Writes (default) or checks (--check):
//   docs-site/public/demand/<name>.svg                       static SVG, light and dark (site theme, or the OS theme when opened alone)
//   the generated regions of src/content/docs/guides/how-demand-works.mdx   between {/* DEMAND:begin <name> */} and {/* DEMAND:end <name> */}
// --check exits 1 when a committed SVG, a generated region, or a number in the older passes-and-lessons tables differs from the code,
// or when a C# formula no longer has the shape this script computes (then update the formulas below with the code).
// --self-test plants a changed constant and a changed formula and proves the check catches both.
//
// Usage: node tools/demand-curves.js [--check | --write | --self-test]

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const siteRoot = path.resolve(here, '..');
const repoRoot = path.resolve(siteRoot, '..');
const SRC = {
  day: 'mod/SkiResortMod/Systems/SkiDayRules.cs',
  money: 'mod/SkiResortMod/Systems/SkiMoneyRules.cs',
  pass: 'mod/SkiResortMod/Systems/SkiPassRules.cs',
  catalog: 'mod/SkiResortMod/Systems/SkiCatalog.cs',
};
const PAGE = 'src/content/docs/guides/how-demand-works.mdx';
const OLD_PAGE = 'src/content/docs/guides/passes-and-lessons.mdx';
const OUT_DIR = 'public/demand';

const nl = (s) => s.replace(/\r\n/g, '\n');
const squash = (s) => s.replace(/\s+/g, ' ');

// ---- constants from the C# ----------------------------------------------------------------------------------------------------
function readSources(overrides = {}) {
  const out = {};
  for (const [k, rel] of Object.entries(SRC)) out[k] = nl(overrides[k] ?? fs.readFileSync(path.join(repoRoot, rel), 'utf8'));
  return out;
}

function num(text, name, file) {
  const m = new RegExp(`\\b${name}\\s*=\\s*(-?\\d+(?:\\.\\d+)?)`).exec(text);
  if (!m) throw new Error(`constant ${name} not found in ${file}`);
  return Number(m[1]);
}

function readConstants(src) {
  const c = {
    priceFullAt: num(src.day, 'PriceFullAt', SRC.day),
    priceSpan: num(src.day, 'PriceSpan', SRC.day),
    priceMax: num(src.day, 'PriceMax', SRC.day),
    partyFullAt: num(src.day, 'PartyPriceFullAt', SRC.day),
    ticketDefault: num(src.money, 'TicketPriceDefault', SRC.money),
    ticketMax: num(src.money, 'TicketPriceMax', SRC.money),
    passFullAt: num(src.money, 'PassFullAt', SRC.money),
    passSpan: num(src.money, 'PassSpan', SRC.money),
    passPriceMin: num(src.money, 'PassPriceMin', SRC.money),
    passPriceMax: num(src.money, 'PassPriceMax', SRC.money),
    passPriceDefault: num(src.money, 'PassPriceDefault', SRC.money),
    passShareDefault: num(src.money, 'PassShareDefault', SRC.money),
    lodgeSpendDefault: num(src.catalog, 'Default', SRC.catalog),
  };
  // The lodge rows: name, price tolerance, spend multiplier.
  const lodges = [];
  const re = /new LodgeDef\(\d+, "(\w+)"[\s\S]*?priceTolerance: (\d+)[\s\S]*?spendMultiplier: ([\d.]+)f/g;
  for (let m; (m = re.exec(src.catalog));) lodges.push({ id: m[1], tolerance: Number(m[2]), mult: Number(m[3]) });
  if (lodges.length !== 5) throw new Error(`expected 5 lodge rows in ${SRC.catalog}, found ${lodges.length}`);
  c.lodges = lodges;
  c.tolerances = [...new Set(lodges.map((l) => l.tolerance))].sort((a, b) => a - b);
  c.multipliers = [...new Set(lodges.map((l) => l.mult))].sort((a, b) => a - b);
  return c;
}

// The formulas below mirror the C#. These pins fail when the C# text no longer has the shape computed here.
const PINS = [
  ['day', 'PriceFactor', 'Unit01((PriceFullAt + math.clamp(tolerance, 0, PriceMax) - math.clamp(ticketPrice, 0, PriceMax)) / (double)PriceSpan)'],
  ['day', 'PartyPriceFactor', 'Unit01((PartyPriceFullAt + math.clamp(tolerance, 0, PriceMax) - math.clamp(ticketPrice, 0, PriceMax)) / (double)PriceSpan)'],
  ['money', 'PassFactor', 'long num = (long)(PassFullAt + PassSpan) * ticketPrice - passPrice; long den = (long)PassSpan * ticketPrice; return num >= den ? 1.0 : num <= 0 ? 0.0 : num / (double)den;'],
  ['money', 'LodgeSpend', 'double v = (double)setting * multiplier * math.sqrt((double)ClampTicketPrice(ticketPrice) / TicketPriceDefault); return (int)math.floor(v + 0.5);'],
  ['pass', 'DestFee', 'int quarter = (pass + DestVisits / 2) / DestVisits; return math.min(ticketPrice, math.max(1, quarter));'],
  ['pass', 'DestVisits', 'DestVisits = SkiMoneyRules.PassFullAt'],
  ['pass', 'DestShare', 'double s = share / 100.0 * demand * math.min(1.0, feePriceFactor);'],
];

function checkPins(src) {
  const problems = [];
  for (const [file, name, text] of PINS) {
    if (!squash(src[file]).includes(text)) problems.push(`FORMULA ${name} in ${SRC[file]} no longer reads "${text}". Update the formula in tools/demand-curves.js and regenerate.`);
  }
  return problems;
}

// ---- the curves (same arithmetic as the C#) -----------------------------------------------------------------------------------
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const unit01 = (v) => clamp(v, 0, 1);

function curves(c) {
  return {
    priceFactor: (price, tol) => unit01((c.priceFullAt + clamp(tol, 0, c.priceMax) - clamp(price, 0, c.priceMax)) / c.priceSpan),
    partyFactor: (price, tol) => unit01((c.partyFullAt + clamp(tol, 0, c.priceMax) - clamp(price, 0, c.priceMax)) / c.priceSpan),
    passFactor: (pass, ticket) => {
      if (ticket <= 0 || pass <= 0) return 0;
      const num = (c.passFullAt + c.passSpan) * ticket - pass, den = c.passSpan * ticket;
      return num >= den ? 1 : num <= 0 ? 0 : num / den;
    },
    destFee: (pass, ticket) => {
      if (ticket <= 0) return 0;
      const quarter = Math.floor((pass + Math.floor(c.passFullAt / 2)) / c.passFullAt);
      return Math.min(ticket, Math.max(1, quarter));
    },
    spend: (setting, mult, ticket) => (setting <= 0 || mult <= 0 || ticket <= 0 ? 0
      : Math.floor(setting * mult * Math.sqrt(Math.min(ticket, c.ticketMax) / c.ticketDefault) + 0.5)),
    spendExact: (setting, mult, ticket) => setting * mult * Math.sqrt(ticket / c.ticketDefault),
  };
}

function destShare(c, f, sharePct, pass, ticket, tol) {
  if (sharePct <= 0 || ticket <= 0) return 0;
  const fee = f.destFee(pass, ticket);
  const fp = f.priceFactor(fee, tol);
  if (!(fp > 0)) return 0;
  const demand = f.passFactor(pass, ticket);
  if (!(demand > 0)) return 0;
  return Math.min(1, (sharePct / 100) * demand * Math.min(1, fp));
}

const TIER_NAMES = { 0: 'Hut or A-frame', 20: 'Chalet Lodge', 40: 'Swanky Summit or Grand Lodge' };
const TIER_SHORT = { 0: 'Hut or A-frame', 20: 'Chalet', 40: 'Summit or Grand' };
const SPEND_NAMES = { 1: 'Hut or A-frame', 1.5: 'Chalet Lodge', 3: 'Swanky Summit or Grand Lodge' };
const CLASSES = ['s1', 's2', 's3'];

// ---- SVG ----------------------------------------------------------------------------------------------------------------------
const W = 640, H = 360, ML = 58, MR = 20, MT = 58, MB = 54;
const PW = W - ML - MR, PH = H - MT - MB;
const f1 = (v) => (Math.round(v * 10) / 10).toFixed(1).replace(/\.0$/, '');
const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

const STYLE = `
.dc{--bg:none;--tx:#1d2b44;--mu:#566a8a;--gr:#cbd8e5;--s1:#2e7d32;--s2:#2a63b8;--s3:#0e0e12;--s4:#b35a00;--ref:#8a9bb3}
@media (prefers-color-scheme:dark){.dc{--tx:#f0fbff;--mu:#b9c9d8;--gr:#2a3753;--s1:#6fcf74;--s2:#6aa8ff;--s3:#d5dde6;--s4:#ffa42d;--ref:#56698a}}
:root[data-theme='light'] .dc{--tx:#1d2b44;--mu:#566a8a;--gr:#cbd8e5;--s1:#2e7d32;--s2:#2a63b8;--s3:#0e0e12;--s4:#b35a00;--ref:#8a9bb3}
:root[data-theme='dark'] .dc{--tx:#f0fbff;--mu:#b9c9d8;--gr:#2a3753;--s1:#6fcf74;--s2:#6aa8ff;--s3:#d5dde6;--s4:#ffa42d;--ref:#56698a}
.dc text{fill:var(--tx);font:13px ui-sans-serif,system-ui,'Segoe UI',Roboto,Helvetica,Arial,sans-serif}
.dc .mu{fill:var(--mu)}.dc .gr{stroke:var(--gr);stroke-width:1;fill:none}.dc .ax{stroke:var(--mu);stroke-width:1.5;fill:none}
.dc .ref{stroke:var(--ref);stroke-width:1.5;stroke-dasharray:2 4;fill:none}
.dc .ln{fill:none;stroke-width:3;stroke-linejoin:round;stroke-linecap:round}
.dc .s1{stroke:var(--s1)}.dc .s2{stroke:var(--s2)}.dc .s3{stroke:var(--s3)}.dc .s4{stroke:var(--s4)}
.dc .d2{stroke-dasharray:10 5}.dc .d3{stroke-dasharray:2 5}`.trim();

// chart: { name, title, desc, xlabel, ylabel, xmax, ymax, xticks, yticks, xfmt, yfmt, series:[{label, pts, cls, dash}], refs:[{x,label}] }
function svgChart(ch) {
  const X = (v) => ML + (v / ch.xmax) * PW, Y = (v) => MT + PH - (v / ch.ymax) * PH;
  const o = [];
  o.push(`<svg xmlns="http://www.w3.org/2000/svg" class="dc" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}" role="img" aria-labelledby="dc-${ch.name}-t dc-${ch.name}-d" style="width:100%;height:auto;max-width:${W}px">`);
  o.push(`<title id="dc-${ch.name}-t">${esc(ch.title)}</title><desc id="dc-${ch.name}-d">${esc(ch.desc)}</desc>`);
  o.push(`<style>${STYLE}</style>`);
  for (const t of ch.yticks) o.push(`<path class="gr" d="M${ML} ${f1(Y(t))}H${W - MR}"/><text class="mu" x="${ML - 8}" y="${f1(Y(t) + 4)}" text-anchor="end">${esc(ch.yfmt(t))}</text>`);
  for (const t of ch.xticks) o.push(`<path class="gr" d="M${f1(X(t))} ${MT}V${MT + PH}"/><text class="mu" x="${f1(X(t))}" y="${MT + PH + 18}" text-anchor="middle">${esc(ch.xfmt(t))}</text>`);
  o.push(`<path class="ax" d="M${ML} ${MT}V${MT + PH}H${W - MR}"/>`);
  for (const r of ch.refs || []) o.push(`<path class="ref" d="M${f1(X(r.x))} ${MT}V${MT + PH}"/><text class="mu" x="${f1(X(r.x) + 5)}" y="${MT + PH - 8}">${esc(r.label)}</text>`);
  for (const s of ch.series) {
    const d = s.pts.map(([x, y], i) => `${i ? 'L' : 'M'}${f1(X(x))} ${f1(Y(y))}`).join('');
    o.push(`<path class="ln ${s.cls}${s.dash ? ' ' + s.dash : ''}" d="${d}"/>`);
  }
  o.push(`<text class="mu" x="${f1(ML + PW / 2)}" y="${H - 10}" text-anchor="middle">${esc(ch.xlabel)}</text>`);
  o.push(`<text class="mu" transform="translate(14 ${f1(MT + PH / 2)}) rotate(-90)" text-anchor="middle">${esc(ch.ylabel)}</text>`);
  // legend, one row per series above the plot
  let ly = 14;
  for (const s of ch.series) {
    o.push(`<path class="ln ${s.cls}${s.dash ? ' ' + s.dash : ''}" d="M${ML} ${ly - 4}h34"/><text x="${ML + 42}" y="${ly}">${esc(s.label)}</text>`);
    ly += 16;
  }
  o.push('</svg>');
  return o.join('\n') + '\n';
}

const range = (a, b, step) => { const r = []; for (let v = a; v <= b + 1e-9; v += step) r.push(Math.round(v * 1000) / 1000); return r; };
const pctFmt = (v) => `${Math.round(v * 100)} %`;

function buildCharts(c, f) {
  const charts = {};
  const T = c.tolerances;
  const maxT = c.ticketMax;

  charts['ticket-demand'] = {
    name: 'ticket-demand',
    title: 'Ski-day guests who still come, by lift ticket price',
    desc: `Three falling lines, one per lodge tier. Each is 100 percent up to a ticket price of ${c.priceFullAt - c.priceSpan} plus the tier's tolerance, then falls by ${f1(100 / c.priceSpan)} percentage points per ticket point. At the maximum ticket of ${maxT} the shares are ${T.map((t) => pctFmt(f.priceFactor(maxT, t))).join(', ')}.`,
    xlabel: 'Lift ticket price', ylabel: 'Ski-day guests who come',
    xmax: maxT, ymax: 1, xticks: range(0, maxT, 40), yticks: range(0, 1, 0.25), xfmt: (v) => String(v), yfmt: pctFmt,
    series: T.map((t, i) => ({ label: `${TIER_NAMES[t] ?? 'Lodge tier'} (tolerance ${t})`, cls: CLASSES[i], dash: ['', 'd2', 'd3'][i], pts: range(0, maxT, 5).map((p) => [p, f.priceFactor(p, t)]) })),
    refs: [{ x: c.ticketDefault, label: `default ${c.ticketDefault}` }],
  };

  const passX = 14;
  charts['season-pass'] = {
    name: 'season-pass',
    title: "Households that buy a season pass, by the pass price in lift tickets",
    desc: `One line. All would-be buyers buy up to ${c.passFullAt} times the ticket price, then the share falls to 0 at ${c.passFullAt + c.passSpan} times the ticket price.`,
    xlabel: 'Pass price as a multiple of the lift ticket', ylabel: 'Would-be buyers who buy',
    xmax: passX, ymax: 1, xticks: range(0, passX, 2), yticks: range(0, 1, 0.25), xfmt: (v) => `${v} ×`, yfmt: pctFmt,
    series: [{ label: 'Resident household with the season pass on offer', cls: 's2', pts: range(0, passX, 0.25).map((m) => [m, m === 0 ? 1 : f.passFactor(m * 100, 100)]) }],
    refs: [{ x: c.passFullAt, label: `${c.passFullAt} × ticket` }],
  };

  const tolHi = T[T.length - 1];
  charts['destination-pass'] = {
    name: 'destination-pass',
    title: `Tourist parties on a destination pass, by pass price (ticket ${c.ticketDefault}, pass share ${c.passShareDefault} %)`,
    desc: `Two lines for the lowest and highest lodge tolerance. The share is flat while the pass price is low, then falls as the daily fee of a quarter of the pass price passes the ticket-demand threshold and as the pass price passes ${c.passFullAt} times the ticket. It reaches 0 at ${(c.passFullAt + c.passSpan) * c.ticketDefault}.`,
    xlabel: `Pass price (lift ticket ${c.ticketDefault})`, ylabel: 'Tourist parties on a pass',
    xmax: 1400, ymax: c.passShareDefault / 100, xticks: range(0, 1400, 200),
    yticks: range(0, c.passShareDefault / 100, c.passShareDefault / 100 / 5), xfmt: (v) => String(v), yfmt: pctFmt,
    series: [T[0], tolHi].map((t, i) => ({
      label: `${TIER_NAMES[t] ?? 'Lodge tier'} (tolerance ${t})`, cls: CLASSES[i === 0 ? 0 : 2], dash: i === 0 ? '' : 'd3',
      pts: range(0, 1400, 20).map((p) => [p, p === 0 ? c.passShareDefault / 100 : destShare(c, f, c.passShareDefault, p, c.ticketDefault, t)]),
    })),
    refs: [{ x: c.passPriceDefault, label: `default ${c.passPriceDefault}` }],
  };

  charts['tourist-party'] = {
    name: 'tourist-party',
    title: 'Game-sent tourist parties that pay, against ski-day guests, by lift ticket price',
    desc: `The ski-day curve at tolerance ${T[0]} falls from a ticket of ${c.priceFullAt - c.priceSpan}. The tourist party curve stays at 100 percent up to the default ticket ${c.ticketDefault} and falls at the same slope, to ${pctFmt(f.partyFactor(maxT, T[0]))} at ${maxT}. A lodge tolerance shifts it right.`,
    xlabel: 'Lift ticket price', ylabel: 'Share who pay',
    xmax: maxT, ymax: 1, xticks: range(0, maxT, 40), yticks: range(0, 1, 0.25), xfmt: (v) => String(v), yfmt: pctFmt,
    series: [
      { label: `Ski-day guests (tolerance ${T[0]})`, cls: 's1', dash: 'd2', pts: range(0, maxT, 5).map((p) => [p, f.priceFactor(p, T[0])]) },
      { label: `Game-sent tourist parties (tolerance ${T[0]})`, cls: 's2', dash: '', pts: range(0, maxT, 5).map((p) => [p, f.partyFactor(p, T[0])]) },
      { label: `Game-sent tourist parties (tolerance ${tolHi})`, cls: 's3', dash: 'd3', pts: range(0, maxT, 5).map((p) => [p, f.partyFactor(p, tolHi)]) },
    ],
    refs: [{ x: c.ticketDefault, label: `default ${c.ticketDefault}` }],
  };

  const M = c.multipliers;
  const top = Math.ceil((c.lodgeSpendDefault * M[M.length - 1] * Math.sqrt(maxT / c.ticketDefault)) / 10) * 10;
  charts['lodge-spend'] = {
    name: 'lodge-spend',
    title: `Lodge spend per paying guest, by lift ticket price (setting ${c.lodgeSpendDefault})`,
    desc: `Three rising lines, one per lodge tier, shaped like a square root. At the default ticket ${c.ticketDefault} they read ${M.map((m) => f.spend(c.lodgeSpendDefault, m, c.ticketDefault)).join(', ')}. At ${maxT} they read ${M.map((m) => f.spend(c.lodgeSpendDefault, m, maxT)).join(', ')}.`,
    xlabel: 'Lift ticket price', ylabel: 'Spend per paying guest',
    xmax: maxT, ymax: top, xticks: range(0, maxT, 40), yticks: range(0, top, top / 4), xfmt: (v) => String(v), yfmt: (v) => String(v),
    series: M.map((m, i) => ({ label: `${SPEND_NAMES[m] ?? 'Lodge tier'} (× ${m})`, cls: CLASSES[i], dash: ['', 'd2', 'd3'][i], pts: range(0, maxT, 5).map((p) => [p, f.spendExact(c.lodgeSpendDefault, m, p)]) })),
    refs: [{ x: c.ticketDefault, label: `default ${c.ticketDefault}` }],
  };
  return charts;
}

// ---- generated page text ------------------------------------------------------------------------------------------------------
function tableMd(head, rows) {
  const align = head.map((_, i) => (i === 0 ? '---' : '---:'));
  return [`| ${head.join(' | ')} |`, `|${align.map((a) => ` ${a} `).join('|')}|`, ...rows.map((r) => `| ${r.join(' | ')} |`)].join('\n');
}

function buildBlocks(c, f) {
  const T = c.tolerances, M = c.multipliers, B = {};
  const tn = (t) => TIER_SHORT[t] ?? `Tolerance ${t}`;
  const full0 = c.priceFullAt - c.priceSpan;

  B['ticket-demand'] = [
    `**Equation:** the share of ski-day guests who still come is (${c.priceFullAt} + tolerance - ticket price) / ${c.priceSpan}, held between 0 and 1.`,
    `The tolerance comes from the best lodge in the ski area. It is ${T.map((t) => `**${t}** for ${TIER_NAMES[t] ?? 'a lodge tier'}`).join(', ')}.`,
    '',
    `Up to a ticket of **${full0}** plus the tolerance, everyone comes. Above that, each ticket point costs **${f1(100 / c.priceSpan)} %** of the guests. The price counts up to **${c.priceMax}**.`,
    '',
    tableMd(['Ticket price', ...T.map(tn)], [40, 60, 80, 100, 120, 140, 160, 180, 200].filter((p) => p <= c.priceMax).map((p) => [String(p), ...T.map((t) => pctFmt(f.priceFactor(p, t)))])),
  ].join('\n');

  B['season-pass'] = [
    `**Equation:** the share of would-be buyers who buy is (${c.passFullAt + c.passSpan} × ticket - pass price) / (${c.passSpan} × ticket), held between 0 and 1. Everyone buys up to **${c.passFullAt} ×** the ticket price. Nobody buys from **${c.passFullAt + c.passSpan} ×**. The pass price is judged against the ticket price, so raising the ticket moves the whole curve right.`,
    '',
    tableMd(['Pass price', 'Households that still buy'], range(c.passFullAt, c.passFullAt + c.passSpan, 2).map((m) => [`${m} × the ticket`, pctFmt(f.passFactor(m * 100, 100))])),
  ].join('\n');

  const tolHi = T[T.length - 1];
  const dRows = [200, 400, 600, 800, 1000, 1200].filter((p) => p >= c.passPriceMin && p <= c.passPriceMax).map((p) => {
    const fee = f.destFee(p, c.ticketDefault);
    return [String(p), String(fee), pctFmt(destShare(c, f, c.passShareDefault, p, c.ticketDefault, T[0])), pctFmt(destShare(c, f, c.passShareDefault, p, c.ticketDefault, tolHi))];
  });
  B['destination-pass'] = [
    `**Equations:** the daily fee a pass holder pays is the pass price / ${c.passFullAt}, rounded, never above the lift ticket and never below 1. The share of tourist parties on a pass is *Season pass share* × the season pass demand above × the ski-day curve above. The curve is applied to the **fee**, not to the ticket. The share cannot pass 100 %.`,
    '',
    `The table is for a lift ticket of **${c.ticketDefault}** and the default *Season pass share* of **${c.passShareDefault} %**.`,
    '',
    tableMd(['Pass price', 'Daily fee', `Parties on a pass, ${tn(T[0])}`, `Parties on a pass, ${tn(tolHi)}`], dRows),
  ].join('\n');

  B['tourist-party'] = [
    `**Equation:** the share of game-sent tourist parties that pay at the gate is (${c.partyFullAt} + tolerance - ticket price) / ${c.priceSpan}, held between 0 and 1. It is the ski-day curve moved **${c.partyFullAt - c.priceFullAt}** to the right, with the same slope.`,
    '',
    `Up to a ticket of **${c.partyFullAt - c.priceSpan}** plus the tolerance, every party pays (the default ticket is **${c.ticketDefault}**). At **${c.priceMax}** it is **${pctFmt(f.partyFactor(c.priceMax, T[0]))}**, or **${pctFmt(f.partyFactor(c.priceMax, tolHi))}** with a ${TIER_NAMES[tolHi] ?? 'top-tier lodge'}. A party that does not pay walks away: no ticket, no lesson, no lodge spend. Holders of a destination pass never walk away.`,
    '',
    tableMd(['Ticket price', `Ski-day guests, ${tn(T[0])}`, ...T.map((t) => `Tourist parties, ${tn(t)}`)], [100, 120, 140, 160, 180, 200].filter((p) => p <= c.priceMax).map((p) => [String(p), pctFmt(f.priceFactor(p, T[0])), ...T.map((t) => pctFmt(f.partyFactor(p, t)))])),
  ].join('\n');

  const s = c.lodgeSpendDefault;
  B['lodge-spend'] = [
    `**Equation:** spend per paying guest = *Lodge spend* setting × tier multiplier × square root of (ticket price / ${c.ticketDefault}), rounded to a whole number. The multiplier is ${M.map((m) => `**${m}** for ${SPEND_NAMES[m] ?? 'a lodge tier'}`).join(', ')}. At a ticket of **${c.ticketDefault}** the root is 1, so the table reads as the setting alone.`,
    '',
    `The table is for the default setting of **${s}**. With tickets off there is no gate and no spend.`,
    '',
    tableMd(['Ticket price', ...M.map((m) => SPEND_NAMES[m] ?? `× ${m}`)], [25, 50, 80, 100, 150, 200].filter((p) => p <= c.ticketMax).map((p) => [String(p), ...M.map((m) => String(f.spend(s, m, p)))])),
  ].join('\n');
  return B;
}

// ---- compare / write ----------------------------------------------------------------------------------------------------------
const begin = (n) => `{/* DEMAND:begin ${n} */}`;
const end = (n) => `{/* DEMAND:end ${n} */}`;

function regionOf(page, name) {
  const a = page.indexOf(begin(name)), b = page.indexOf(end(name));
  if (a < 0 || b < a) return null;
  return { a: a + begin(name).length, b, text: page.slice(a + begin(name).length, b) };
}

function oldPageProblems(c, f, oldPage) {
  const problems = [];
  // The older tables of passes-and-lessons: ticket demand by lodge tier, and season pass demand.
  const T = c.tolerances;
  let rows = 0;
  for (const m of oldPage.matchAll(/^\s*\| (\d+) \| (\d+) % \| (\d+) % \| (\d+) % \|\s*$/gm)) {
    rows++;
    const p = Number(m[1]);
    T.forEach((t, i) => {
      const want = Math.round(f.priceFactor(p, t) * 100);
      if (Number(m[2 + i]) !== want) problems.push(`passes-and-lessons: ticket ${p}, tolerance ${t} reads ${m[2 + i]} %, code gives ${want} %`);
    });
  }
  if (rows === 0) problems.push('passes-and-lessons: ticket demand table not found (the check reads rows like "| 100 | 70 % | 80 % | 90 % |")');
  let prow = 0;
  for (const m of oldPage.matchAll(/^\s*\| (?:up to )?(\d+) × the ticket(?: or more)? \| (\d+) % \||^\s*\| (\d+) × the ticket or more \| none \|/gm)) {
    prow++;
    if (m[1] !== undefined) {
      const want = Math.round(f.passFactor(Number(m[1]) * 100, 100) * 100);
      if (Number(m[2]) !== want) problems.push(`passes-and-lessons: pass at ${m[1]} × the ticket reads ${m[2]} %, code gives ${want} %`);
    } else if (f.passFactor(Number(m[3]) * 100, 100) !== 0) problems.push(`passes-and-lessons: "${m[3]} × the ticket or more" reads none, but the code still sells`);
  }
  if (prow === 0) problems.push('passes-and-lessons: season pass demand table not found');
  return problems;
}

function run(mode, overrides = {}, files = null) {
  const src = readSources(overrides);
  const problems = checkPins(src);
  const c = readConstants(src);
  for (const t of c.tolerances) if (!(t in TIER_NAMES)) problems.push(`lodge tolerance ${t} has no name in TIER_NAMES`);
  for (const m of c.multipliers) if (!(m in SPEND_NAMES)) problems.push(`lodge spend multiplier ${m} has no name in SPEND_NAMES`);
  if (problems.length) return problems;
  const f = curves(c);
  const charts = buildCharts(c, f);
  const blocks = buildBlocks(c, f);
  const read = (rel) => (files ? files[rel] : fs.existsSync(path.join(siteRoot, rel)) ? nl(fs.readFileSync(path.join(siteRoot, rel), 'utf8')) : null);

  if (mode === 'write') {
    fs.mkdirSync(path.join(siteRoot, OUT_DIR), { recursive: true });
    for (const [name, ch] of Object.entries(charts)) fs.writeFileSync(path.join(siteRoot, OUT_DIR, `${name}.svg`), svgChart(ch));
    let page = read(PAGE);
    if (page === null) return [`${PAGE} is missing`];
    for (const [name, text] of Object.entries(blocks)) {
      const r = regionOf(page, name);
      if (!r) { problems.push(`${PAGE}: no DEMAND region "${name}"`); continue; }
      page = page.slice(0, r.a) + `\n${text}\n` + page.slice(r.b);
    }
    if (!problems.length) fs.writeFileSync(path.join(siteRoot, PAGE), page);
    return problems;
  }

  for (const [name, ch] of Object.entries(charts)) {
    const want = svgChart(ch), got = read(`${OUT_DIR}/${name}.svg`);
    if (got === null) problems.push(`${OUT_DIR}/${name}.svg is missing (run: node tools/demand-curves.js --write)`);
    else if (got !== want) problems.push(`${OUT_DIR}/${name}.svg differs from the code constants (run: node tools/demand-curves.js --write)`);
  }
  const page = read(PAGE);
  if (page === null) problems.push(`${PAGE} is missing`);
  else {
    for (const [name, text] of Object.entries(blocks)) {
      const r = regionOf(page, name);
      if (!r) problems.push(`${PAGE}: no DEMAND region "${name}"`);
      else if (r.text.trim() !== text.trim()) problems.push(`${PAGE}: region "${name}" differs from the code constants (run: node tools/demand-curves.js --write)`);
      if (!page.includes(`<DemandChart name="${name}"`)) problems.push(`${PAGE}: no <DemandChart name="${name}"> on the page`);
    }
  }
  const old = read(OLD_PAGE);
  if (old === null) problems.push(`${OLD_PAGE} is missing`);
  else problems.push(...oldPageProblems(c, f, old));
  return problems;
}

function selfTest() {
  const real = readSources();
  const files = {};
  for (const rel of [PAGE, OLD_PAGE, ...['ticket-demand', 'season-pass', 'destination-pass', 'tourist-party', 'lodge-spend'].map((n) => `${OUT_DIR}/${n}.svg`)]) {
    const p = path.join(siteRoot, rel);
    files[rel] = fs.existsSync(p) ? nl(fs.readFileSync(p, 'utf8')) : null;
  }
  let bad = 0;
  const expectFail = (label, overrides, needle) => {
    let out;
    try { out = run('check', overrides, files); } catch (e) { out = [String(e.message)]; }
    if (!out.some((p) => p.includes(needle))) { console.error(`SELF-TEST FAIL: ${label} was not caught (got: ${out.slice(0, 2).join(' | ') || 'nothing'})`); bad++; }
  };
  expectFail('constant change (PartyPriceFullAt 300 -> 310)', { day: real.day.replace('PartyPriceFullAt = 300', 'PartyPriceFullAt = 310') }, 'differs from the code constants');
  expectFail('constant change (PassSpan 8 -> 6)', { money: real.money.replace('PassSpan = 8', 'PassSpan = 6') }, 'differs from the code constants');
  expectFail('tolerance change (summit 40 -> 50)', { catalog: real.catalog.replace(/(\"summit\"[\s\S]*?priceTolerance: )40/, '$150') }, 'TIER_NAMES');
  expectFail('formula change (sqrt dropped)', { money: real.money.replace('math.sqrt((double)ClampTicketPrice(ticketPrice) / TicketPriceDefault)', '((double)ClampTicketPrice(ticketPrice) / TicketPriceDefault)') }, 'FORMULA LodgeSpend');
  const clean = run('check', {}, files);
  if (clean.length) { console.error('SELF-TEST FAIL: the committed files do not pass the clean check: ' + clean[0]); bad++; }
  return bad;
}

const arg = process.argv[2] || '--check';
try {
  if (arg === '--self-test') {
    const bad = selfTest();
    console.log(bad ? `demand-curves self-test: FAIL (${bad})` : 'demand-curves self-test: PASS (planted constant, tolerance and formula faults all caught)');
    process.exit(bad ? 1 : 0);
  }
  const problems = run(arg === '--write' ? 'write' : 'check');
  if (problems.length) {
    for (const p of problems) console.error('FAIL ' + p);
    console.log(`demand-curves: FAIL (${problems.length} problem${problems.length === 1 ? '' : 's'})`);
    process.exit(1);
  }
  console.log(arg === '--write' ? 'demand-curves: written (5 SVGs, page regions)' : 'demand-curves: PASS (5 SVGs, 5 page regions and the older tables match the code)');
} catch (e) {
  console.error('FAIL ' + e.message);
  console.log('demand-curves: FAIL (' + e.message + ')');
  process.exit(1);
}
