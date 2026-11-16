/*
 * tools/ui-audit-measure.js - T202 audit probe (round 2, 2026-11-16). Page-side source: test-hub-layout.js --measure reads this file
 * and evaluates it in the harness page after a shot's state is set. Nothing here ships. It returns one JSON object per state:
 *   controls: every drawn control in #stage (button, role=button, or a cursor: pointer box that is not inside another one), with its
 *             class names (module hash cut), text, box in rem, padding, font size, radius, background and text color
 *   fonts:    every distinct font size (rem) that draws text, with the classes that use it
 *   colors:   every distinct text color that draws text, with the classes that use it
 * Sizes are in rem (the game's rule: 1 rem = 1 px at 1080p, 2 px at 2160p), so 1080 and 2160 compare 1:1.
 */
(() => {
  const rem = parseFloat(getComputedStyle(document.documentElement).fontSize) || 1;
  const stage = document.getElementById("stage") || document.body;
  const R = (px) => Math.round((parseFloat(px) || 0) / rem * 10) / 10;
  const cls = (e) => String(e.className && e.className.baseVal !== undefined ? e.className.baseVal : e.className || "")
    .split(/\s+/).filter(Boolean).map((c) => c.replace(/^sr-/, "").replace(/_[A-Za-z0-9-]{5}$/, "")).join(".");
  const visible = (e) => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 0 && r.height > 0 && s.visibility !== "hidden" && s.display !== "none"; };
  const isControl = (e) => e.tagName === "BUTTON" || e.getAttribute("role") === "button" || getComputedStyle(e).cursor === "pointer";
  const controls = [];
  for (const e of stage.querySelectorAll("*")) {
    if (!visible(e) || !isControl(e)) continue;
    const p = e.parentElement && e.parentElement.closest("button, [role=button]");
    if (p && stage.contains(p) && p !== e) continue;   // the outer control only
    if (e.parentElement && getComputedStyle(e.parentElement).cursor === "pointer" && e.tagName !== "BUTTON") continue;   // inherited pointer
    const r = e.getBoundingClientRect(), s = getComputedStyle(e);
    controls.push({ cls: cls(e), tag: e.tagName.toLowerCase(), text: (e.textContent || "").trim().replace(/\s+/g, " ").slice(0, 28),
      h: R(r.height), w: R(r.width), x: R(r.left), y: R(r.top), pad: [s.paddingTop, s.paddingRight, s.paddingBottom, s.paddingLeft].map(R).join(" "),
      font: R(s.fontSize), radius: R(s.borderTopLeftRadius), bg: s.backgroundColor, color: s.color,
      border: s.borderTopWidth !== "0px" ? `${R(s.borderTopWidth)} ${s.borderTopColor}` : "" });
  }
  const fonts = {}, colors = {};
  const walker = document.createTreeWalker(stage, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    if (!n.textContent.trim()) continue;
    const e = n.parentElement; if (!e || !visible(e) || (document.getElementById("side") || { contains: () => false }).contains(e)) continue;
    const s = getComputedStyle(e), f = R(s.fontSize), c = cls(e) || e.tagName.toLowerCase();
    (fonts[f] = fonts[f] || new Set()).add(c);
    (colors[s.color] = colors[s.color] || new Set()).add(c);
  }
  const flat = (o) => Object.fromEntries(Object.entries(o).map(([k, v]) => [k, Array.from(v).slice(0, 40)]));
  return { remPx: rem, controls, fonts: flat(fonts), colors: flat(colors) };
})()
