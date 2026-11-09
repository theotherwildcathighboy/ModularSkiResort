/*
 * harness/layout-check.js - "never a scrollbar" measured in the harness. harness/index.html loads it. Nothing here ships.
 *
 * URL parameters (read once the bundle is mounted):
 *   ?layout=1   run the whole check and write its JSON into <pre id="layout-result"> (tools/test-hub-layout.js reads it
 *               with a headless browser's --dump-dom). You can also call window.SkiLayout.run() from the console.
 *   ?scenario=<name>&view=<0-5>&fs=<text scale>&tab=<lifts|runs|buildings>&fold=<n>&areas=1&problems=1&page=<n>
 *               set up one state for a screenshot (tools/test-hub-layout.js --shots).
 * Scale: the game's rem rule (html font-size from the window height) and its text scale (index.js applyTextScale sets
 * --fontScale = s and --fontScaleChange = s - 1). The font sizes use both.
 *
 * Asserted per scenario x text scale x view x fold state (each fold opened in turn) x build tab:
 *   - over = content scrollHeight - clientHeight <= 1 px (the vanilla Scrollable shows its bar at scrollHeight - 1 >
 *     offsetHeight, index.js@638418). The check also runs with the ski-area list and the +N popover open.
 *   - The header is one row, and the name never pushes the buttons out.
 *   - Runs / Lifts views are the one exception. Their list (class listAll) draws every row with no pager and may
 *     scroll. The check asserts that no pager row is drawn and records their overflow as "scrolls".
 *   - The tab row and the first tile row keep their place on every build tab (2026-11-07).
 *   - Buildings tab: one tile row (family chips Lodges | Services, the Mergeable buildings chip) and no tile label cut at
 *     text scale 1. Lifts tile "built / drawn". The footer opens like the game's toolbar (openTab).
 * The harness panel frame is a stand-in for the game's, so the report also gives each case's slack (rem left).
 * MIN_SLACK_REM is a margin rule for the title bar / padding difference.
 * Every measure follows settle(): a quiet page, at least QUIET_FRAMES drawn frames after the last DOM change (see settle()).
 */
(function () {
  "use strict";
  const MIN_SLACK_REM = 0;   // over must be <= 0. The slack table in the report shows how much room is left
  const SCALES = [1, 1.25];
  const H = () => window.SkiHarness;
  const q = (sel, root) => (root || document).querySelector(sel);
  const qa = (sel, root) => Array.from((root || document).querySelectorAll(sel));
  const cls = (name) => `[class*="sr-${name}_"]`;
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const frames = (n) => new Promise((r) => { let i = 0; const f = () => { if (++i >= n) r(); else requestAnimationFrame(f); }; requestAnimationFrame(f); });

  // settle() = wait until the hub is quiet (2026-11-09; it was 11 frames + 120 ms of fixed waits, about 300 ms a call).
  // Quiet = QUIET_FRAMES drawn frames in a row with no DOM change in the page (the harness side bar excluded). That is more than the
  // two frames the pager (src/hub/fit.tsx markStale) waits before it measures again, so its measure chain has ended. The hub's
  // setInterval polls (the pager's VIEW_PAGER_POLL_MS safety net: the one that re-pages after a text scale change, which no render
  // or mutation announces) run once the change is drawn and again at the quiet point. The settle ends only when that last poll
  // changes nothing. So the measurement waits for the converged state and not for a timer's phase. MAX_FRAMES caps a page that
  // never goes quiet (counted in stats.capped, reported by the test).
  // `SkiLayout.run({ fixedSettle: true })` (test --fixed-settle) restores the old fixed waits, for a cross-check.
  const QUIET_FRAMES = 3, MAX_FRAMES = 60;
  const stats = { settles: 0, frames: 0, capped: 0, lastCapped: "" };
  let fixedSettle = false;
  // The hub's setInterval callbacks, so settle() can run its polls at once. layout-check.js loads before the bundle, so this sees them all.
  const intervals = new Map();
  const nativeSetInterval = window.setInterval.bind(window), nativeClearInterval = window.clearInterval.bind(window);
  window.setInterval = function (fn, ms, ...args) {
    const id = nativeSetInterval(fn, ms, ...args);
    if (typeof fn === "function") intervals.set(id, () => fn(...args));
    return id;
  };
  window.clearInterval = function (id) { intervals.delete(id); return nativeClearInterval(id); };
  function runPolls() { for (const f of Array.from(intervals.values())) { try { f(); } catch (e) { /* a poll's own error is the hub's */ } } }
  // DOM changes anywhere but the harness side bar (its trigger log and status line)
  let changes = 0, lastChange = "";
  const describe = (r) => { const t = r.target.nodeType === 1 ? r.target : r.target.parentElement; return `${r.type}${r.attributeName ? " " + r.attributeName : ""} on ${t ? t.tagName + "." + String(t.className).slice(0, 60) : "?"}`; };
  const outside = (recs) => { const side = document.getElementById("side"); const r = recs.find((x) => !side || !side.contains(x.target)); if (r) lastChange = describe(r); return !!r; };
  const observer = new MutationObserver((recs) => { if (outside(recs)) changes++; });
  let observing = false;
  function observe() {
    if (observing || !document.body) return;
    observer.observe(document.body, { subtree: true, childList: true, attributes: true, characterData: true });
    observer.observe(document.documentElement, { attributes: true });   // the text scale variables
    observing = true;
  }
  function pending() { const recs = observer.takeRecords(); if (outside(recs)) changes++; return changes; }
  async function legacySettle() { await frames(4); await wait(60); await frames(4); await wait(60); await frames(3); }
  async function settle() {
    stats.settles++;
    if (fixedSettle) { await legacySettle(); return; }
    observe();
    let seen = pending(), quiet = 0;
    for (let n = 1; n <= MAX_FRAMES; n++) {
      await frames(1);
      stats.frames++;
      if (n === 1) runPolls();
      const now = pending();
      if (now !== seen) { seen = now; quiet = 0; continue; }
      if (++quiet < QUIET_FRAMES) continue;
      runPolls();   // the last word: a poll that still changes the page means it was not settled
      const after = pending();
      if (after === seen) return;
      seen = after; quiet = 0;
    }
    stats.capped++;
    stats.lastCapped = lastChange;
  }

  function setScale(fs) {
    const r = document.documentElement;
    r.style.setProperty("--fontScale", String(fs));
    r.style.setProperty("--fontScaleChange", String(Math.round((fs - 1) * 1000) / 1000));
  }
  function gameRem() {
    const sel = document.getElementById("scale");
    if (sel && sel.value !== "game") { sel.value = "game"; sel.onchange && sel.onchange(); }
    document.documentElement.style.fontSize = "";
  }
  function rem() { return parseFloat(getComputedStyle(document.documentElement).fontSize) || 1; }
  function panel() { return q(".h-panel"); }
  function content() { const p = panel(); return p ? q(".h-panel-content", p) : null; }
  function measure() {
    const c = content();
    if (!c) return null;
    const u = rem();
    const body = c.firstElementChild;
    const padTop = parseFloat(getComputedStyle(c).paddingTop) || 0, padBottom = parseFloat(getComputedStyle(c).paddingBottom) || 0;
    const header = q(".h-panel-header", panel());
    return {
      overRem: Math.round((c.scrollHeight - c.clientHeight) / u * 10) / 10,
      overPx: c.scrollHeight - c.clientHeight,
      slackRem: body ? Math.round((c.clientHeight - padTop - padBottom - body.getBoundingClientRect().height) / u) : null,
      panelRem: Math.round(panel().getBoundingClientRect().height / u),
      headerRem: header ? Math.round(header.getBoundingClientRect().height / u) : null,
      headerFits: header ? header.scrollWidth <= header.clientWidth + 1 : true,
    };
  }
  /** Text the player cannot read whole: an element whose text is cut (scrollWidth past its box) or whose text ends past the panel's edge. */
  function clippedText(where, out) {
    const p = panel();
    if (!p) return;
    const right = p.getBoundingClientRect().right;
    for (const e of qa("*", p)) {
      const text = (e.textContent || "").trim();
      if (!text || e.children.length > 0) continue;
      const cut = getComputedStyle(e).overflowX !== "visible" && e.scrollWidth > e.clientWidth + 1;
      const past = e.getBoundingClientRect().right > right + 1;
      if (!cut && !past) continue;
      // The nearest classed ancestor clips an unclassed leaf (the <span> that a LocalizedString renders). So that class decides
      // whether the cut is allowed (a player name in a placeholder sentence, 2026-11-02).
      let name = (String(e.className).match(/sr-([A-Za-z0-9]+)_/) || [])[1];
      for (let a = e.parentElement; !name && a && a !== p; a = a.parentElement) name = (String(a.className).match(/sr-([A-Za-z0-9]+)_/) || [])[1];
      name = name || e.tagName;
      const key = `${name}|${text}`;
      if (!out.clipped[key]) out.clipped[key] = { cls: name, text, where, kind: past ? "past the panel" : "cut", over: Math.round(e.scrollWidth - e.clientWidth),
        playerName: scenarioNames.find((n) => text.includes(n)) || null };
    }
  }
  function tiles() { const r = q(cls("tileRow")); return r ? qa("button", r) : []; }
  function buildTabs() { const r = q(cls("tabRow")); return r ? qa("button", r) : []; }
  // The footer opens like the game's toolbar (2026-11-07). A click on a closed footer opens that tab. A click on the open tab closes it.
  function footerOpen() { return !!q(cls("buildRow"), q(cls("buildFooter"))); }
  async function openTab(b) {
    if (!footerOpen() || !/tabButtonOn/.test(b.className)) { b.click(); await settle(); }
    if (!footerOpen()) { b.click(); await settle(); }
  }
  function folds() { const p = panel(); return p ? qa(cls("foldRow"), p) : []; }
  function openFolds() { const p = panel(); return p ? qa(cls("foldRowOpen"), p) : []; }
  async function closeFolds() { for (const f of openFolds()) { f.click(); await settle(); } }

  // The player-chosen names of the current scenario (every string "name" field: lifts, runs, areas, problem subjects). A cut line in a
  // class that holds such a name (test-hub-layout.js NAME_LINES) is allowed only when the line holds one of them (round 624).
  let scenarioNames = [];
  function namesOf(o, out, depth) {
    if (!o || typeof o !== "object" || depth > 4) return out;
    for (const [k, v] of Object.entries(o)) {
      if (k === "name" && typeof v === "string" && v.trim()) out.add(v.trim());
      else if (v && typeof v === "object") namesOf(v, out, depth + 1);
    }
    return out;
  }
  async function applyScenario(name) {
    const sel = document.getElementById("scenario");
    if (!window.SkiScenarios[name]) throw new Error("no scenario " + name);
    scenarioNames = [...namesOf(window.SkiScenarios[name], new Set(), 0)];
    sel.value = name; sel.onchange();
    H().set("hubOpen", true);
    await settle();
  }

  function pagerText() { const p = q(cls("pagerText")); return p ? p.textContent.trim() : ""; }

  /** One scenario at one text scale: every view, every fold, the build tabs, the overlays. */
  async function checkOne(name, fs, out) {
    await applyScenario(name);
    setScale(fs);
    await settle();
    const nViews = tiles().length;
    const fail = (what, m) => out.fails.push(Object.assign({ scenario: name, fs, what }, m || {}));
    const rec = (view, state, m) => {
      // A Runs / Lifts view (its full list carries listAll) may scroll. It must list every row (no pager row).
      const scrolls = !!q(cls("listAll"), panel());
      out.cases.push(Object.assign({ scenario: name, fs, view, state, scrolls }, m));
      clippedText(`${name} x${fs} view ${view} ${state}`, out);
      if (scrolls && q(cls("pagerRow"), panel())) fail(`pager drawn on a full-list view: view ${view} ${state}`, m);
      if (!scrolls && (!m || m.overPx > 1 || m.slackRem < MIN_SLACK_REM)) fail(`overflow: view ${view} ${state}`, m);
      if (m && !m.headerFits) fail(`header overflows: view ${view} ${state}`, m);
      // The planner's plan scrolls in its own box, so a long plan never pushes the tree controls out of view (field test 2026-10-31).
      const plan = q(cls("planList"), panel());
      if (plan && (getComputedStyle(plan).overflowY !== "scroll" || plan.getBoundingClientRect().height > 201 * rem())) fail(`plan list is not its own short scroller: view ${view} ${state}`, m);
    };
    // Every page of the shown state must fit. The function returns the pager text of page 1.
    const allPages = async (v, state) => {
      rec(v, state, measure());
      const pt = pagerText();
      const m = /(\d+)\s*\/\s*(\d+)/.exec(pt);
      const pages = m ? Number(m[2]) : 1;
      for (let p = 1; p < pages; p++) {
        qa("button", q(cls("pagerRow"))).pop().click();
        await settle();
        rec(v, `${state}, page ${p + 1} / ${pages}`, measure());
      }
      return pt;
    };
    for (let v = 0; v < nViews; v++) {
      tiles()[v].click();
      await settle();
      await closeFolds();
      await settle();
      const pt = await allPages(v, "default");
      out.pagers.push({ scenario: name, fs, view: v, pager: pt || "(one page)" });
      const n = folds().length;
      for (let i = 0; i < n; i++) {
        const f = folds()[i];
        const label = f.textContent.trim().slice(0, 24);
        f.click();
        await settle();
        const fp = await allPages(v, `fold ${i} (${label})`);
        if (fp) out.pagers.push({ scenario: name, fs, view: v, pager: `fold ${i}: ${fp}` });
        await closeFolds();
        await settle();
      }
    }
    // Overlays (on the Attraction view): the ski-area list and the +N popover.
    tiles()[0].click(); await settle();
    const nameBtn = q(cls("areaName"));
    if (nameBtn) { nameBtn.click(); await settle(); rec(0, "area list open", measure()); nameBtn.click(); await settle(); }
    const more = q(cls("problemMoreButton"));
    if (more) { more.click(); await settle(); rec(0, "problems popover open", measure()); more.click(); await settle(); }
    // Build tabs: the footer must not change the content's fit, and the Buildings tab draws every building tile.
    // 2026-11-07: the tab row and the first tile row stay in place across tabs (the Buildings tab's two rows moved them 118 rem at 1080).
    const tabTops = [];
    for (const b of buildTabs()) {
      await openTab(b);
      const tabName = b.textContent.trim();
      rec(0, `build tab ${tabName}`, measure());
      const tr = q(cls("tabRow"));
      const firstRow = q(cls("buildRow"), q(cls("buildFooter")));
      if (tr) tabTops.push({ tab: tabName, y: tr.getBoundingClientRect().top, row: firstRow ? firstRow.getBoundingClientRect().top : -1 });
      // The check hovers every tile in turn. Its tooltip and the one status line must not cut.
      for (const cell of qa(cls("buildCell"))) {
        cell.dispatchEvent(new MouseEvent("mouseover", { bubbles: true }));
        await frames(2);
        clippedText(`${name} x${fs} tab ${tabName} hover`, out);
        for (const line of qa(cls("buildStatusLine"))) {
          const range = document.createRange();
          range.selectNodeContents(line);
          const full = Math.round(range.getBoundingClientRect().width / Math.max(1, line.clientWidth) * 100);
          if (!out.fullest[fs] || full > out.fullest[fs].full) out.fullest[fs] = { full, text: line.textContent.trim() };
        }
        cell.dispatchEvent(new MouseEvent("mouseout", { bubbles: true }));
      }
      await frames(2);
    }
    if (tabTops.length > 1) {
      const dy = Math.max(...tabTops.map((x) => x.y)) - Math.min(...tabTops.map((x) => x.y));
      const drow = Math.max(...tabTops.map((x) => x.row)) - Math.min(...tabTops.map((x) => x.row));
      if (dy > 1 || drow > 1) {
        fail(`Build tab row or first tile row moves when the tab changes (${tabTops.map((x) => `${x.tab} ${Math.round(x.y)} / ${Math.round(x.row)}`).join(", ")} px)`, { tabTops });
      }
    }
    const bTab = buildTabs().find((b) => /Buildings/.test(b.textContent));
    if (bTab) {
      await openTab(bTab);
      const tilesN = qa(cls("buildRow")).reduce((n, r) => n + qa("button", r).length, 0);
      const chipEls = qa(cls("pickerChip"), q(cls("pickerRow")));
      const chips = chipEls.length;
      // 2026-11-06: every Buildings tile shows its whole label at text scale 1 (two rows). The game cut a row of 12 to "A-f...".
      const cut = qa(cls("buildTileLabel"), q(cls("buildFooter"))).filter((l) => l.scrollWidth > l.clientWidth + 1).map((l) => l.textContent.trim());
      out.buildings.push({ scenario: name, fs, tiles: tilesN, rows: qa(cls("buildRow")).length, lodgeChips: chips, cut: cut.join(", "),
        caption: chipEls.map((e) => e.textContent.trim()).filter(Boolean).join(" | ") });
      // 2026-11-07: one tile row, the family chips (Lodges | Services, when both exist) and the Mergeable buildings chip.
      if (qa(cls("buildRow")).length !== 1) fail("Buildings tab draws more than one tile row", { rows: qa(cls("buildRow")).length });
      if (fs === 1 && cut.length > 0) fail("Buildings tab tile labels cut at text scale 1", { cut });
    }
    // The Lifts tab carries the lift generator toggle
    const lTab = buildTabs().find((b) => /^Lifts/.test(b.textContent.trim()));
    if (lTab) {
      await openTab(lTab);
      const row = q(cls("pickerRow"));
      const has = !!row && /Lift generator/.test(row.textContent);
      out.liftTool.push({ scenario: name, fs, present: has, rowRem: row ? Math.round(row.getBoundingClientRect().height / rem()) : -1 });
      if (!has) fail("Lifts tab has no Lift generator toggle");
      if (row && row.scrollWidth > row.clientWidth + 1) fail("Lifts family row overflows sideways", { w: row.scrollWidth, cw: row.clientWidth });
    }
    const liftsTile = tiles()[2];
    out.liftsTile.push({ scenario: name, fs, text: liftsTile ? liftsTile.textContent.trim() : "" });
    const band = q(cls("problems"));
    out.band.push({ scenario: name, fs, text: band ? qa(cls("problemText"), band).map((e) => e.textContent.trim()).filter(Boolean) : [] });
  }

  /**
   * Round 624 tooltip pass, at text scale 1, 1.25 and 1.5 (TIP_SCALES): in the Attraction view, every fold opened in turn, and the six
   * tiles, every row with a tooltip is hovered. Each open tooltip must keep (a) every icon key row inside its box (scrollWidth <=
   * clientWidth + 1 and its right edge inside the key; field/2026-11-09/icons2-pass B4 had 730 px in 720) and (b) every number with its
   * unit (no ASCII space between a digit and a UNIT_WORDS entry; hubRules unitNbsp). (b) also runs over the panel text of each state.
   * `units` is hubRules UNIT_WORDS, read from the source by tools/test-hub-layout.js.
   */
  const TIP_SCALES = [1, 1.25, 1.5];
  function unitGapRe(units) {
    const alt = units.map((u) => u.replace(/[/\\^$*+?.()|[\]{}]/g, "\\$&")).join("|");
    return new RegExp(`\\d[ ]+(?:${alt})(?![A-Za-z0-9²])`);
  }
  function unitSplits(root, re) {
    const bad = [];
    const walk = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    // Adjacent text nodes are one run of text in layout ("376" + " m" from two React children), so test their joined text too.
    let n, joined = "", parent = null;
    while ((n = walk.nextNode())) {
      if (n.parentNode !== parent) { if (re.test(joined)) bad.push(joined.trim().slice(0, 80)); joined = ""; parent = n.parentNode; }
      joined += n.nodeValue;
    }
    if (re.test(joined)) bad.push(joined.trim().slice(0, 80));
    return bad;
  }
  async function tipPass(name, fs, out, units) {
    await applyScenario(name);
    setScale(fs);
    await settle();
    const re = unitGapRe(units);
    const fail = (what, m) => out.fails.push(Object.assign({ scenario: name, fs, what }, m || {}));
    const hoverAll = async (state) => {
      for (const bad of unitSplits(panel(), re)) fail(`number split from its unit in the panel (${state}): "${bad}"`);
      const targets = [...qa(cls("infoLine"), panel()), ...tiles()];
      for (const el of targets) {
        el.dispatchEvent(new MouseEvent("mouseover", { bubbles: true }));
        await frames(3);
        const tip = q(".h-tip");
        if (tip) {
          out.tips.n++;
          const label = (el.textContent || "").trim().slice(0, 30);
          for (const bad of unitSplits(tip, re)) fail(`number split from its unit in the tooltip of "${label}" (${state}): "${bad}"`);
          const key = q(cls("iconKey"), tip);
          if (key) {
            out.tips.keys++;
            const kr = key.getBoundingClientRect();
            const rows = qa(":scope > " + cls("iconLine"), key);
            out.tips.keyRows = Math.max(out.tips.keyRows, rows.length);
            for (const row of rows) {
              const rr = row.getBoundingClientRect();
              const last = row.lastElementChild ? row.lastElementChild.getBoundingClientRect().right : rr.right;
              const over = Math.max(row.scrollWidth - row.clientWidth, last - kr.right);
              if (over > 1) fail(`tooltip key row overflows by ${Math.round(over)} px in the tooltip of "${label}" (${state}): "${row.textContent.trim()}"`);
            }
          }
        }
        el.dispatchEvent(new MouseEvent("mouseout", { bubbles: true }));
        await frames(1);
      }
    };
    const t0 = tiles()[0];
    if (t0) { t0.click(); await settle(); }
    await closeFolds();
    await hoverAll("view 0, folds closed");
    const nFolds = folds().length;
    for (let i = 0; i < nFolds; i++) {
      await closeFolds();
      const f = folds()[i];
      if (!f) continue;
      f.click();
      await settle();
      await hoverAll(`view 0, fold ${i}`);
    }
    await closeFolds();
  }

  /** The scenarios the check runs and those the tooltip pass runs, each in report order. */
  function checkScenarios() { return Object.keys(window.SkiScenarios).filter((n) => /Ash Flat \(runs|Big resort|Field case/.test(n)); }
  function tipScenarios() { return Object.keys(window.SkiScenarios).filter((n) => /^Ash Flat \(runs|^Ash Flat, no black run open$|^Ash Flat, no ski school$|^Field case/.test(n)); }

  /**
   * opts: scenarios (names, default checkScenarios()), scales (default [1, 1.25]), units (hubRules UNIT_WORDS: runs the tooltip pass
   * after the check), tipScenarios (default tipScenarios()), tipScales (default TIP_SCALES), keepScale (leave the last text scale set,
   * as a serial run does between its units), fixedSettle (the old fixed waits). tools/test-hub-layout.js runs one scenario x text scale
   * of the check or of the tooltip pass per call, on several pages at once, and merges the results in this order.
   */
  async function run(opts) {
    const o = opts || {};
    fixedSettle = !!o.fixedSettle;
    gameRem();
    const t0 = performance.now(), s0 = Object.assign({}, stats);
    const out = { viewport: `${window.innerWidth}x${window.innerHeight}`, remPx: 0, cases: [], fails: [], pagers: [], buildings: [], liftsTile: [], band: [], liftTool: [], clipped: {}, fullest: {} };
    out.remPx = Math.round(rem() * 1000) / 1000;
    const names = o.scenarios || checkScenarios();
    for (const name of names) for (const fs of o.scales || SCALES) {
      try { await checkOne(name, fs, out); } catch (e) { out.fails.push({ scenario: name, fs, what: "exception: " + (e && e.message || e) }); }
    }
    out.tips = { n: 0, keys: 0, keyRows: 0, scenarios: [] };
    if (o.units && o.units.length) {
      const tipNames = o.tipScenarios || tipScenarios();
      out.tips.scenarios = tipNames;
      for (const name of tipNames) for (const fs of o.tipScales || TIP_SCALES) {
        try { await tipPass(name, fs, out, o.units); } catch (e) { out.fails.push({ scenario: name, fs, what: "tooltip pass exception: " + (e && e.message || e) }); }
      }
    }
    if (!o.keepScale) setScale(1);
    out.ok = out.fails.length === 0;
    out.maxOverRem = out.cases.reduce((m, c) => Math.max(m, c.overRem || 0), 0);
    out.minSlackRem = out.cases.reduce((m, c) => Math.min(m, c.slackRem == null ? m : c.slackRem), 1e9);
    out.timing = { ms: Math.round(performance.now() - t0), settles: stats.settles - s0.settles, frames: stats.frames - s0.frames, capped: stats.capped - s0.capped, lastCapped: stats.capped > s0.capped ? stats.lastCapped : "" };
    return out;
  }

  /**
   * The state a finished checkOne leaves the hub in: the Attraction view, the footer open on the Lifts tab (else the last tab the
   * check opened). In a serial run every scenario but the first starts from it. A sharded run's page that begins mid-list calls this
   * first, so each scenario starts as it would in the serial run.
   */
  async function prime(name, fs) {
    fixedSettle = false;
    gameRem();
    await applyScenario(name);
    if (fs !== undefined) { setScale(fs); await settle(); }
    const t = tiles();
    if (t[0]) { t[0].click(); await settle(); }
    const tabs = buildTabs();
    const last = tabs.find((b) => /^Lifts/.test(b.textContent.trim())) || tabs.find((b) => /Buildings/.test(b.textContent)) || tabs[tabs.length - 1];
    if (last) await openTab(last);
    return true;
  }

  /** One state for a screenshot (see the header). */
  async function shot(p) {
    gameRem();
    if (p.get("scenario")) await applyScenario(p.get("scenario"));
    setScale(Number(p.get("fs") || 1));
    await settle();
    if (p.get("view")) { tiles()[Number(p.get("view"))].click(); await settle(); }
    if (p.get("tab")) { const b = buildTabs().find((x) => x.textContent.trim().toLowerCase() === p.get("tab")); if (b) await openTab(b); }
    if (p.get("fold")) { const f = folds()[Number(p.get("fold"))]; if (f) { f.click(); await settle(); } }
    if (p.get("page")) { for (let i = 1; i < Number(p.get("page")); i++) { const r = q(cls("pagerRow")); if (r) { qa("button", r).pop().click(); await settle(); } } }
    if (p.get("areas")) { const b = q(cls("areaName")); if (b) { b.click(); await settle(); } }
    if (p.get("problems")) { const b = q(cls("problemMoreButton")); if (b) { b.click(); await settle(); } }
    if (p.get("free")) {   // debug aid, never in the check: let the panel grow to show what the Scrollable hid
      const pn = panel(); pn.style.maxHeight = "none"; pn.style.height = "auto";
    }
    // Frame the panel: hide the harness side bar so a screenshot shows only the "game viewport".
    if (p.get("clean")) { const s = document.getElementById("side"); if (s) s.style.display = "none"; document.getElementById("stage").style.left = "0px"; await settle(); }
    const m = measure();
    document.body.setAttribute("data-shot-ready", JSON.stringify(m));
  }

  async function boot() {
    const p = new URLSearchParams(location.search);
    try {
      if (p.get("layout")) {
        const res = await run();
        const pre = document.createElement("pre");
        pre.id = "layout-result";
        pre.textContent = JSON.stringify(res);
        document.body.appendChild(pre);
      } else if (p.get("scenario") || p.get("view") || p.get("fs")) {
        await shot(p);
      }
    } catch (e) {
      const pre = document.createElement("pre");
      pre.id = "layout-result";
      pre.textContent = JSON.stringify({ ok: false, fails: [{ what: "exception: " + (e && e.message || e) }] });
      document.body.appendChild(pre);
    }
  }

  window.SkiLayout = { run, prime, checkScenarios, tipScenarios, TIP_SCALES, boot, shot, measure, setScale, stats };
})();
