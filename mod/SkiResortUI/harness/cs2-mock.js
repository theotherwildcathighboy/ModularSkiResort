/*
 * harness/cs2-mock.js - browser stand-ins for the game's window modules, so build/SkiResortMod.mjs
 * can mount in a plain browser (no game). Written for this repo (MIT), 2026-10-18.
 *
 * The bundle is an ES module with window externals (webpack.config.js): it reads window.React,
 * window["cs2/api"], window["cs2/ui"], window["cs2/l10n"], window["cs2/input"]. This file defines
 * those names BEFORE the bundle is imported (index.html loads it after the React UMD scripts).
 *
 * What is faithful: bindValue / useValue / bindLocalValue / trigger semantics (cs2/api typings in
 * types/api.d.ts), LocalizedString placeholder substitution, LocalizedNumber unit formats as the
 * hub expects them (src/hub/parts.tsx header), Panel / PanelFoldout / Button / FloatingButton /
 * Tooltip props the hub uses. What is NOT: the game's own component styling (vanilla panel chrome,
 * fonts, sounds, focus / gamepad), its full number localization, and anything C# computes. The
 * harness only answers the triggers a C# HubSystem would answer trivially (toggleHub, setHubOpen,
 * selectResort, setPisteMap, glade*, rename); everything else is logged.
 *
 * Harness API (window.SkiHarness): bindings (name -> LocalValueBinding), set(name, value),
 * get(name), log (trigger log array), onLog(fn), dict (locale id -> text; empty = fallbacks).
 */
(function () {
  "use strict";
  const React = window.React;
  if (!React) throw new Error("cs2-mock.js: window.React must be loaded first");
  const h = React.createElement;

  // ---- cs2/api --------------------------------------------------------------------------------
  class LocalValueBinding {
    constructor(initialValue) {
      this._value = initialValue;
      this.listeners = new Set();
      this.disposed = false;
    }
    get registered() { return true; }
    get value() { return this._value; }
    subscribe(listener) {
      const self = this;
      let current = listener;
      if (current) self.listeners.add(current);
      return {
        get value() { return self._value; },
        setChangeListener(next) {
          if (current) self.listeners.delete(current);
          current = next;
          if (current) self.listeners.add(current);
        },
        dispose() { if (current) self.listeners.delete(current); current = undefined; },
      };
    }
    dispose() { this.listeners.clear(); this.disposed = true; }
    update(newValue) {
      if (Object.is(newValue, this._value)) return;
      this._value = newValue;
      for (const l of Array.from(this.listeners)) { try { l(newValue); } catch (e) { console.error(e); } }
    }
  }

  const bindings = new Map();        // "group.name" -> LocalValueBinding
  const log = [];
  const logListeners = new Set();
  function pushLog(entry) {
    log.push(entry);
    if (log.length > 400) log.shift();
    for (const f of logListeners) { try { f(entry); } catch (e) { console.error(e); } }
  }
  function key(group, name) { return group + "." + name; }
  function bindValue(group, name, fallbackValue) {
    const k = key(group, name);
    let b = bindings.get(k);
    if (!b) { b = new LocalValueBinding(fallbackValue); bindings.set(k, b); }
    return b;
  }
  function bindLocalValue(initialValue) { return new LocalValueBinding(initialValue); }
  function useValue(binding) {
    const [v, setV] = React.useState(binding.value);
    React.useEffect(() => {
      const s = binding.subscribe(setV);
      setV(binding.value);
      return () => s.dispose();
    }, [binding]);
    return v;
  }
  function useValueRef(binding) {
    const ref = React.useRef(binding.value);
    React.useEffect(() => {
      const s = binding.subscribe((v) => { ref.current = v; });
      return () => s.dispose();
    }, [binding]);
    return ref;
  }
  function useValueOnChange(binding, onChange) {
    const v = useValue(binding);
    const prev = React.useRef(v);
    React.useEffect(() => { if (!Object.is(prev.current, v)) { onChange(v, prev.current); prev.current = v; } }, [v, onChange]);
    return v;
  }
  function get(name) { const b = bindings.get(key("skiResort", name)); return b ? b.value : undefined; }
  function set(name, value) { bindValue("skiResort", name, value).update(value); }
  function sameEntity(a, b) { return (a ? a.index : 0) === (b ? b.index : 0) && (a ? a.version : 0) === (b ? b.version : 0); }

  // The C# HubSystem's trivial answers. Everything else is only logged.
  const answers = {
    toggleHub: () => set("hubOpen", !get("hubOpen")),
    setHubOpen: (open) => set("hubOpen", !!open),
    selectResort: (sign) => set("selectedResort", sign || { index: 0, version: 0 }),
    setPisteMap: (on) => set("pisteMap", !!on),
    liftLineToggle: () => { const g = get("liftLine"); if (g) set("liftLine", Object.assign({}, g, { active: !g.active, state: "PickBase" })); },   // contract 12
    gradeToggle: () => { const g = get("grade"); if (g) set("grade", Object.assign({}, g, { active: !g.active, state: g.active ? "" : "Empty" })); },   // contract 14
    gladeToggle: () => { const g = get("glade"); if (g) set("glade", Object.assign({}, g, { active: !g.active })); },
    gladeSetLevel: (level) => { const g = get("glade"); if (g) set("glade", Object.assign({}, g, { level })); },
    gladeSetTreeLine: (treeLine) => { const g = get("glade"); if (g) set("glade", Object.assign({}, g, { treeLine: !!treeLine })); },
    launch: (id) => {
      const items = get("build") || [];
      set("build", items.map((it) => Object.assign({}, it, { active: it.id === id ? !it.active : false })));
    },
    rename: (entity, name) => {
      const nm = String(name || "").trim().slice(0, 64);
      for (const [list, field] of [["resorts", "sign"], ["runs", "entity"], ["lifts", "entity"]]) {
        const rows = get(list) || [];
        if (rows.some((r) => sameEntity(r[field], entity))) {
          set(list, rows.map((r) => sameEntity(r[field], entity)
            ? Object.assign({}, r, { name: nm || (r.defaultName || r.name), customName: !!nm }) : r));
        }
      }
    },
  };
  function trigger(group, name, ...args) {
    pushLog({ t: Date.now(), group, name, args });
    const a = answers[name];
    if (group === "skiResort" && a) { try { a(...args); } catch (e) { console.error("harness answer failed", name, e); } }
  }
  function call(group, name, ...args) { pushLog({ t: Date.now(), group, name: "call:" + name, args }); return Promise.resolve(undefined); }
  function bindMap() { return { getValue: () => undefined, subscribe: () => ({ value: undefined, setChangeListener() {}, dispose() {} }), dispose() {} }; }
  function bindEvent() { return { subscribe: () => ({ dispose() {} }), dispose() {} }; }
  function bindTrigger(group, name) { return () => trigger(group, name); }
  function bindTriggerWithArgs(group, name) { return (...args) => trigger(group, name, ...args); }

  window["cs2/api"] = {
    bindValue, bindLocalValue, bindMap, bindEvent, bindTrigger, bindTriggerWithArgs, trigger, call,
    useValue, useValueRef, useValueOnChange, LocalValueBinding,
    useReducedValue: (binding, reducer, initial) => { const v = useValue(binding); const [acc, setAcc] = React.useState(initial); React.useEffect(() => setAcc((c) => reducer(c, v)), [v]); return acc; },
    useMapValue: () => undefined, useMapValues: () => [], useMapValueOnChange: () => undefined,
  };

  // ---- cs2/l10n -------------------------------------------------------------------------------
  const Unit = {
    Integer: "integer", IntegerRounded: "integerRounded", IntegerPerMonth: "integerPerMonth", IntegerPerHour: "integerPerHour",
    FloatSingleFraction: "floatSingleFraction", FloatTwoFractions: "floatTwoFractions", FloatThreeFractions: "floatThreeFractions",
    Percentage: "percentage", PercentageSingleFraction: "percentageSingleFraction", PercentagePrecise: "percentagePrecise",
    Angle: "angle", Length: "length", Area: "area", Volume: "volume", VolumePerMonth: "volumePerMonth", Weight: "weight",
    WeightPerCell: "weightPerCell", WeightPerMonth: "weightPerMonth", Power: "power", Energy: "energy", DataRate: "dataRate",
    DataBytes: "dataBytes", DataMegabytes: "dataMegabytes", Money: "money", MoneyPerCell: "moneyPerCell",
    MoneyPerMonth: "moneyPerMonth", MoneyPerHour: "moneyPerHour", MoneyPerDistance: "moneyPerDistance",
    MoneyPerDistancePerMonth: "moneyPerDistancePerMonth", BodiesPerMonth: "bodiesPerMonth", XP: "xp", Temperature: "temperature",
    NetElevation: "netElevation", ScreenFrequency: "screenFrequency", Height: "height", Custom: "custom",
  };
  const dict = {};
  const thousands = (n) => Math.round(n).toLocaleString("en-US");
  const dec1 = (n) => { const s = (Math.round(n * 10) / 10).toFixed(1); return s.endsWith(".0") ? s.slice(0, -2) : s; };
  const nbsp = (s) => s.replace(/(\d) (\D)/, "$1 $2");
  function renderNumber(loc, props) {
    const v = Number(props.value);
    const sign = props.signed && v > 0 ? "+" : "";
    switch (props.unit) {
      case Unit.Length: return nbsp(sign + (Math.abs(v) < 1000 ? thousands(v) + " m" : dec1(v / 1000) + " km"));
      case Unit.Height: return nbsp(sign + thousands(v) + " m");
      case Unit.Area: return nbsp(sign + (Math.abs(v) < 100000 ? thousands(v) + " m²" : dec1(v / 1e6) + " km²"));
      case Unit.Percentage: return sign + thousands(v) + "%";
      case Unit.PercentageSingleFraction: return sign + dec1(v) + "%";
      case Unit.Money: return sign + "¢" + thousands(v);
      case Unit.MoneyPerMonth: return sign + "¢" + thousands(v) + "/month";
      case Unit.MoneyPerHour: return sign + "¢" + thousands(v) + "/h";
      case Unit.IntegerPerMonth: return sign + thousands(v) + "/month";
      case Unit.IntegerPerHour: return sign + thousands(v) + "/h";
      case Unit.FloatSingleFraction: return sign + dec1(v);
      case Unit.FloatTwoFractions: return sign + v.toFixed(2);
      case Unit.Temperature: return sign + thousands(v) + "°C";
      case Unit.Integer: case Unit.IntegerRounded: default: return sign + thousands(v);
    }
  }
  const localization = {
    translate: (id, fallback) => (id != null && Object.prototype.hasOwnProperty.call(dict, id)) ? dict[id] : (fallback === undefined ? null : fallback),
    unitSettings: { timeFormat: 0, temperatureUnit: 0, unitSystem: 0 },
  };
  function useLocalization() { return localization; }
  function locComponent(renderString) {
    const C = React.memo(function Loc(props) { return h("span", null, renderString(localization, props)); });
    C.renderString = renderString;
    return C;
  }
  const LocalizedNumber = locComponent(renderNumber);
  const LocalizedFraction = locComponent((loc, p) => renderNumber(loc, { value: p.value, unit: p.unit }) + " / " + renderNumber(loc, { value: p.total, unit: p.unit }));
  const LocalizedBounds = locComponent((loc, p) => renderNumber(loc, { value: p.min, unit: p.unit }) + " – " + renderNumber(loc, { value: p.max, unit: p.unit }));
  function renderArg(arg) {
    if (arg == null) return "";
    if (typeof arg === "string" || typeof arg === "number") return String(arg);
    if (React.isValidElement(arg) && arg.type && typeof arg.type.renderString === "function") return arg.type.renderString(localization, arg.props);
    return arg; // a React element: interleaved below
  }
  const LocalizedString = React.memo(function LocalizedString(props) {
    const text = localization.translate(props.id, props.fallback == null ? (props.showIdOnFail ? props.id : "") : props.fallback) || "";
    const args = props.args || {};
    const parts = text.split(/(\{[A-Za-z0-9_]+(?::[A-Za-z]+)?\})/g);
    const out = parts.map((p, i) => {
      const m = /^\{([A-Za-z0-9_]+)(?::[A-Za-z]+)?\}$/.exec(p);
      if (!m) return p;
      const a = renderArg(args[m[1]]);
      return typeof a === "string" ? a : h(React.Fragment, { key: i }, a);
    });
    return h("span", null, ...out);
  });
  LocalizedString.renderString = (loc, props) => {
    const text = loc.translate(props.id, props.fallback) || "";
    return text.replace(/\{([A-Za-z0-9_]+)(?::[A-Za-z]+)?\}/g, (_, k) => { const a = renderArg((props.args || {})[k]); return typeof a === "string" ? a : "?"; });
  };
  window["cs2/l10n"] = {
    Unit, UnitSystem: { Metric: 0, Freedom: 1 }, useLocalization, useCachedLocalization: useLocalization,
    LocalizedNumber, LocalizedString, LocalizedFraction, LocalizedBounds,
    LocalizedPercentage: locComponent((loc, p) => thousands(100 * p.value / (p.max || 1)) + "%"),
    LocalizedEntityName: locComponent((loc, p) => String(p.value && p.value.name || "")),
    LocalizedNumber$1: LocalizedNumber, LocalizedString$1: LocalizedString, LocalizedFraction$1: LocalizedFraction, LocalizedBounds$1: LocalizedBounds,
  };

  // ---- cs2/input / cs2/utils / cs2/bindings / cs2/modding ------------------------------------
  window["cs2/input"] = { FOCUS_DISABLED: "FOCUS_DISABLED", FOCUS_AUTO: "FOCUS_AUTO", useUniqueFocusKey: () => "focus" };
  window["cs2/utils"] = { entityEquals: sameEntity, Entity: { Null: { index: 0, version: 0 } } };
  window["cs2/bindings"] = {};
  window["cs2/modding"] = {};

  // ---- cs2/ui ---------------------------------------------------------------------------------
  const cx = (...xs) => xs.filter(Boolean).join(" ");
  const imgSrc = (src) => typeof src === "string" ? src.replace(/^coui:\/\/ui-mods\/SkiResortMod-img\//, "../build/SkiResortMod-img/") : src;
  const DROP = new Set(["focusKey", "debugName", "sounds", "selectAction", "selectSound", "disableHint", "hintAction", "actionContext", "theme", "selected", "onSelect", "as", "tooltipLabel", "tinted", "draggable", "initialPosition", "transition", "transitionSounds", "allowFocusExit", "hintClassName", "showCloseHint", "unfocusedHintAction", "footerHintAsTooltip", "backActionOverride", "contentClassName", "header", "footer", "onClose", "initialExpanded", "onToggleExpanded", "disableFocus", "expandFromContent", "tooltip", "delayTime", "forceVisible", "hideOnInteraction", "direction", "alignment", "balloonUiTarget", "anchorElRef"]);
  const rest = (props) => { const o = {}; for (const k in props) if (!DROP.has(k) && k !== "children" && k !== "className") o[k] = props[k]; return o; };

  function Button(props) {
    const Tag = props.as === "div" ? "div" : "button";
    const theme = props.theme || {};
    return h(Tag, Object.assign(rest(props), {
      // the game REPLACES the button theme when theme.button is set: no stand-in padding on themed buttons (tiles, chips)
      className: cx(theme.button ? null : "h-btn", theme.button, props.selected && "h-selected", props.className),
      onClick: (e) => { if (props.onSelect) props.onSelect(); if (props.onClick) props.onClick(e); },
      title: typeof props.tooltipLabel === "string" ? props.tooltipLabel : undefined,
    }), props.children);
  }
  function FloatingButton(props) {
    return h("button", Object.assign(rest(props), {
      className: cx("h-floating", props.selected && "h-selected", props.className),
      onClick: () => { if (props.onSelect) props.onSelect(); },
    }), props.src ? h("img", { src: imgSrc(props.src), alt: "", className: "h-floating-img" }) : props.children);
  }
  // T80 (2026-10-18): like the game's Tooltip (it attaches its listeners to its ONE child, no wrapper element), the listeners go on the
  // child itself and the tip renders in a portal on <body>, fixed under the child, wrapping at 420 rem. The old inline
  // `span.h-tipwrap` shrink-wrapped every row with a tooltip (ragged value columns) and collapsed tipped flex bars.
  function Tooltip(props) {
    const [at, setAt] = React.useState(null);
    const tip = props.tooltip;
    const child = React.Children.only(props.children);
    const cp = child.props || {};
    const enter = (e) => { const r = e.currentTarget.getBoundingClientRect(); setAt({ left: r.left, top: r.bottom }); if (cp.onMouseEnter) cp.onMouseEnter(e); };
    const leave = (e) => { setAt(null); if (cp.onMouseLeave) cp.onMouseLeave(e); };
    const shown = at && tip != null && !props.disabled;
    return h(React.Fragment, null,
      React.cloneElement(child, { onMouseEnter: enter, onMouseLeave: leave }),
      shown ? ReactDOM.createPortal(h("span", {
        className: cx("h-tip", props.direction && "h-tip-" + props.direction),
        style: { position: "fixed", left: at.left + "px", top: at.top + "px", maxWidth: "420rem", whiteSpace: "normal", zIndex: 10000 },
      }, tip), document.body) : null);
  }
  function Panel(props) {
    const theme = props.theme || {};
    return h("div", Object.assign(rest(props), { className: cx("h-panel", theme.panel, props.className) }),
      props.header != null || props.onClose ? h("div", { className: cx("h-panel-header", theme.header) },
        h("div", { className: "h-panel-title" }, props.header),
        props.onClose ? h("button", { className: "h-panel-close", onClick: props.onClose, title: "Close" }, "×") : null) : null,
      h("div", { className: cx("h-panel-content", theme.content, props.contentClassName) }, props.children),
      props.footer != null ? h("div", { className: cx("h-panel-footer", theme.footer) }, props.footer) : null);
  }
  function PanelFoldout(props) {
    const [open, setOpen] = React.useState(props.initialExpanded !== false);
    return h("div", Object.assign(rest(props), { className: cx("h-foldout", open && "h-open", props.className) }),
      h("div", { className: "h-foldout-header", onClick: () => { const v = !open; setOpen(v); if (props.onToggleExpanded) props.onToggleExpanded(v); } },
        h("span", { className: "h-foldout-arrow" }, open ? "▾" : "▸"), props.header),
      open ? h("div", { className: "h-foldout-content" }, props.children) : null);
  }
  const simple = (tag, cls) => (props) => h(tag, Object.assign(rest(props), { className: cx(cls, props.className) }), props.children);
  // T97 P1 (2026-10-19): Dropdown / DropdownToggle / DropdownItem follow the game's dropdown.tsx (installed 1.6.2f1 index.js@716237,
  // @717950, @719062): a context with visible / toggle / hide; the children (the toggle) render in place; while open the list renders in a
  // PORTAL on <body> under the toggle (at least its width, AnchoredPopup minWidth: true, @713812) above a full-screen barrier whose click
  // closes it (PointerBarrier, @621027). Portals bubble React events along the React tree, exactly as in game, so the hub's mousedown guard
  // around the Dropdown is exercised here too. An item fires onChange only when it is not selected, then closes the list. Not faithful:
  // the vanilla chrome (here plain dark boxes), flipping up, focus / gamepad navigation, sounds.
  const DropdownContext = React.createContext({ visible: false, toggle: () => {}, hide: () => {} });
  function Dropdown(props) {
    const [open, setOpen] = React.useState(false);
    const [at, setAt] = React.useState(null);
    const anchor = React.useRef(null);
    const set = (v) => {
      if (v && anchor.current) {
        const el = anchor.current.firstElementChild || anchor.current;
        const r = el.getBoundingClientRect();
        setAt({ left: r.left, top: r.bottom, width: r.width });
      }
      setOpen(v);
      if (props.onToggle) props.onToggle(v);
    };
    const ctx = { visible: open, toggle: () => set(!open), hide: () => set(false) };
    return h(DropdownContext.Provider, { value: ctx },
      h("span", { ref: anchor, className: "h-ddanchor" }, props.children),
      open ? ReactDOM.createPortal(h("div", { className: "h-ddbarrier", onClick: () => set(false) }), document.body) : null,
      open && at ? ReactDOM.createPortal(h("div", { className: "h-ddpopup", style: { left: at.left + "px", top: at.top + "px", minWidth: at.width + "px" },
        onClick: (e) => e.stopPropagation() }, props.content), document.body) : null);
  }
  function DropdownToggle(props) {
    const ctx = React.useContext(DropdownContext);
    return h("button", Object.assign(rest(props), { className: cx("h-ddtoggle", ctx.visible && "active", props.className), onClick: (e) => { e.stopPropagation(); ctx.toggle(); } }),
      h("div", { className: "h-ddlabel" }, props.children), h("span", { className: "h-ddindicator" }, ctx.visible ? "▴" : "▾"));
  }
  function DropdownItem(props) {
    const ctx = React.useContext(DropdownContext);
    return h("div", { className: cx("h-dditem", props.selected && "h-selected", props.className), onClick: () => {
      if (props.onChange && !props.selected) props.onChange(props.value); else if (props.onToggleSelected && props.selected) props.onToggleSelected(props.value);
      if (props.closeOnSelect !== false) ctx.hide();
    } }, props.children);
  }
  (function addDropdownCss() {
    const css = ".h-ddanchor{display:flex;flex:1 1 0;min-width:0}" +
      ".h-ddtoggle{display:flex;align-items:center;flex:1 1 0;min-width:0;background:rgba(255,255,255,0.08);color:inherit;border:1px solid rgba(255,255,255,0.18);border-radius:4rem;padding:2rem 8rem;cursor:pointer;font:inherit}" +
      ".h-ddtoggle:hover,.h-ddtoggle.active{background:var(--hoverColorNormal)}" +
      ".h-ddlabel{flex:1 1 0;min-width:0;overflow:hidden;white-space:nowrap;text-overflow:ellipsis;text-align:left}.h-ddindicator{margin-left:6rem;opacity:0.7}" +
      ".h-ddbarrier{position:fixed;left:0;top:0;right:0;bottom:0;z-index:9998}" +
      ".h-ddpopup{position:fixed;z-index:9999;background:rgba(20,26,38,0.98);border:1px solid rgba(255,255,255,0.15);border-radius:4rem;padding:4rem 0;color:var(--normalTextColor);box-shadow:0 6rem 18rem rgba(0,0,0,0.5)}" +
      ".h-ddpopup .h-dditem{display:flex;padding:6rem 12rem}.h-ddpopup .h-dditem:hover{background:var(--hoverColorNormal)}";
    const el = document.createElement("style"); el.textContent = css; document.head.appendChild(el);
  })();
  window["cs2/ui"] = {
    Button, FloatingButton, Tooltip, Panel, PanelFoldout, DropdownItem, Dropdown, DropdownToggle,
    Icon: (props) => h("img", { src: imgSrc(props.src), className: cx("h-icon", props.className), alt: "" }),
    MenuButton: Button, PanelSection: simple("div", "h-section"), PanelSectionRow: simple("div", "h-row"),
    Scrollable: simple("div", "h-scrollable"), Portal: (props) => props.children,
    FormattedText: simple("span", ""), FormattedParagraphs: simple("div", ""), MarkdownRenderer: simple("div", ""), MarkupRenderer: simple("div", ""),
    ConfirmationDialog: simple("div", "h-panel"), DialogContext: React.createContext(null), DialogRenderer: () => null, DialogStack: simple("div", ""),
  };

  // ---- harness API ----------------------------------------------------------------------------
  window.SkiHarness = { bindings, set, get, log, onLog: (f) => { logListeners.add(f); return () => logListeners.delete(f); }, dict, sameEntity };
})();
