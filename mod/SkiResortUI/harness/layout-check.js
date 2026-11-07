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
  async function settle() { await frames(4); await wait(60); await frames(4); await wait(60); await frames(3); }

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
      if (!out.clipped[key]) out.clipped[key] = { cls: name, text, where, kind: past ? "past the panel" : "cut", over: Math.round(e.scrollWidth - e.clientWidth) };
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

  async function applyScenario(name) {
    const sel = document.getElementById("scenario");
    if (!window.SkiScenarios[name]) throw new Error("no scenario " + name);
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

  async function run(opts) {
    const o = opts || {};
    gameRem();
    const out = { viewport: `${window.innerWidth}x${window.innerHeight}`, remPx: 0, cases: [], fails: [], pagers: [], buildings: [], liftsTile: [], band: [], liftTool: [], clipped: {}, fullest: {} };
    out.remPx = Math.round(rem() * 1000) / 1000;
    const names = o.scenarios || Object.keys(window.SkiScenarios).filter((n) => /Ash Flat \(runs|Big resort|Field case/.test(n));
    for (const name of names) for (const fs of o.scales || SCALES) {
      try { await checkOne(name, fs, out); } catch (e) { out.fails.push({ scenario: name, fs, what: "exception: " + (e && e.message || e) }); }
    }
    setScale(1);
    out.ok = out.fails.length === 0;
    out.maxOverRem = out.cases.reduce((m, c) => Math.max(m, c.overRem || 0), 0);
    out.minSlackRem = out.cases.reduce((m, c) => Math.min(m, c.slackRem == null ? m : c.slackRem), 1e9);
    return out;
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

  window.SkiLayout = { run, boot, shot, measure, setScale };
})();
