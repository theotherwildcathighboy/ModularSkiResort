/*
 * tools/check-us-english.js - US English in every player-facing text (T130, 2026-10-20; field test note 18, "remove ALL British
 * English, here and everywhere", he is American; DECISIONS 350). Written for this repo (MIT). Reads only; writes nothing.
 *
 * Scans the text a player can read:
 *   - mod/SkiResortMod/LocaleEN.cs and mod/SkiResortMod/Settings/LocaleEN.cs: every string literal outside a // comment;
 *   - mod/SkiResortUI/src/**: the English of every [key, english] locale tuple (hubText.ts and the views);
 *   - mod/SkiResortMod/**.cs (not obj/, bin/): the string literals of every Mod.Log / log Info / Warn / Error call (the mod log is
 *     something a player opens and posts);
 *   - docs-site/src/content/**.md(x): the prose (code fences skipped; LOCK regions are scanned too: a hit there is reported for
 *     the owner, never edited by an agent, CLAUDE.md docs-site rule).
 * T130 part 2 (2026-10-20) adds the text a developer reads:
 *   - the comments and docstrings of every .cs .ts .tsx .js .scss .py .ps1 .sh file under mod/, tests/, tools/, assets/blender, docs/;
 *   - the prose of every .md under docs/ (not docs/archive/), README / release / .claude / assets notes, code fences included.
 *   In comments and prose a token is NOT checked when it is an identifier or a path: it holds _ or a digit or an inner capital
 *   (colour_set, GladeOutlineColour), sits inside `backticks`, a URL, a file name or a path, or is followed by = (a parameter), or sits inside a quotation (a
 *   double-quoted span of 3+ words: the owner's words, a wiki page, a field-log line: never edited).
 *   A prose line that discusses spellings (it says "British", "US English", "T130", "spelling", or holds a British word AND its
 *   US form) is skipped. NOT scanned: string literals outside the player-facing places above, identifiers, docs/archive/, JSON data,
 *   tools/loop-identity/baseline-* (a frozen copy of an old source file), and the PENDING files below.
 * FAILS (exit 1) on a word of BRITISH below, matched case-insensitively at a word start; ALLOW holds the proper names that contain one
 * (another mod's or a vanilla name). Prints "file:line: word -> us" per hit.
 *   T130 part 3 (2026-10-24): the string literals of every .cs under mod/ tests/ tools/ (log lines, DUMP rows, warnings, assert messages),
 *   not only the ones inside a Log call; CS_LITERAL_ALLOW lists the few intentional ones. --cs-counts prints the count per file.
 *   T130 part 4 (2026-10-24): the string literals of every .py under assets/blender (print / log / assert / raise messages, report and manifest
 *   text; section 8; tests/data is skipped, docstrings are section 5's). A token with _ or a digit or an inner capital (colour_set), a path,
 *   an attribute (Mark.centre) or a call written with empty parentheses (storey()) is a code name, not prose. A literal that is a data key
 *   spelled the British way (the "centre" key of the frame records) goes in PY_LITERAL_ALLOW, with its reason. --py-counts prints the count per file.
 * Usage: node tools/check-us-english.js [--verbose] [--cs-counts] [--py-counts] [--self-test]
 */
"use strict";
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");

// British form (a word start; "*" = any ending) -> the US form. Keep the list to spellings, not vocabulary.
const BRITISH = [
  ["colour*", "color"], ["grey*", "gray"], ["centre*", "center"], ["metre*", "meter"], ["kilometre*", "kilometer"],
  ["centimetre*", "centimeter"], ["litre*", "liter"], ["modelled", "modeled"], ["modelling", "modeling"], ["cancelled", "canceled"],
  ["cancelling", "canceling"], ["travelled", "traveled"], ["travelling", "traveling"], ["traveller*", "traveler"], ["labelled", "labeled"],
  ["labelling", "labeling"], ["levelled", "leveled"], ["levelling", "leveling"], ["signalled", "signaled"], ["signalling", "signaling"],
  ["fuelled", "fueled"], ["totalled", "totaled"], ["marvellous", "marvelous"], ["behaviour*", "behavior"], ["favour*", "favor"],
  ["honour*", "honor"], ["neighbour*", "neighbor"], ["harbour*", "harbor"], ["labour*", "labor"], ["flavour*", "flavor"],
  ["organis*", "organiz-"], ["realise*", "realize"], ["realised", "realized"], ["realising", "realizing"], ["recognis*", "recogniz-"],
  ["optimis*", "optimiz-"], ["minimis*", "minimiz-"], ["maximis*", "maximiz-"], ["prioritis*", "prioritiz-"], ["normalis*", "normaliz-"],
  ["initialis*", "initializ-"], ["customis*", "customiz-"], ["visualis*", "visualiz-"], ["utilis*", "utiliz-"], ["summaris*", "summariz-"],
  ["categoris*", "categoriz-"], ["finalis*", "finaliz-"], ["stabilis*", "stabiliz-"], ["synchronis*", "synchroniz-"], ["apologis*", "apologiz-"],
  ["analyse*", "analyze"], ["analysed", "analyzed"], ["analysing", "analyzing"], ["catalogue*", "catalog"], ["dialogue*", "dialog"],
  ["licence*", "license"], ["defence*", "defense"], ["offence*", "offense"], ["programme*", "program"], ["tyre*", "tire"], ["kerb*", "curb"],
  ["aluminium", "aluminum"], ["storey*", "story"], ["whilst", "while"], ["amongst", "among"], ["plough*", "plow"], ["mould*", "mold"],
  ["manoeuvr*", "maneuver"], ["artefact*", "artifact"], ["judgement*", "judgment"], ["ageing", "aging"], ["sceptic*", "skeptic"],
  ["fulfil", "fulfill"], ["enrol", "enroll"], ["cheque*", "check"], ["jewellery", "jewelry"], ["aeroplane*", "airplane"],
  // T130 part 2 additions: more -our / -re words, -ise verbs with their endings spelled out (a bare "stem*" would hit organism, optimism,
  // finalist), doubled-l forms, and the odd spellings found in docs and comments. "re:" = a raw regex (a word start is added).
  ["recolour*", "recolor"], ["armour*", "armor"], ["humour*", "humor"], ["rumour*", "rumor"], ["vigour*", "vigor"], ["odour*", "odor"], ["vapour*", "vapor"],
  ["theatre*", "theater"], ["fibre*", "fiber"], ["calibre*", "caliber"], ["millimetre*", "millimeter"], ["decimetre*", "decimeter"],
  ["sulphur*", "sulfur"], ["analogue*", "analog"], ["maths", "math"], ["per cent", "percent"], ["practise*", "practice"],
  ["channelled", "channeled"], ["tunnelled", "tunneled"], ["equalled", "equaled"], ["dialled", "dialed"], ["shovelled", "shoveled"],
  ["greyscale", "grayscale"], ["centring", "centering"], ["acknowledgement*", "acknowledgment"], ["fulfilment", "fulfillment"],
  ["learnt", "learned"], ["spelt", "spelled"], ["burnt", "burned"], ["dreamt", "dreamed"], ["pretence", "pretense"], ["skilful", "skillful"],
  ["re:(?:raster|equal|quant|general|random|local|parameter|vector|serial|author|subsid|material|sanit|capital|neutral|memor|special|standard|character|digit|token|central|formal|legal|moder|harmon|scrutin|sympath|theor|fertil|industrial|hospital|monopol)is(?:e|es|ed|ing|er|ers|ation|ations|able|ability)", "-ize forms"],
  ["re:(?:emphas|parameter|paralys|catalys)(?:ise|ised|ises|ising|yse|ysed|yses|ysing)", "-ize / -yze forms"],
];
// Proper names that contain a British form (another mod's name, a vanilla prefab or locale key), and valid US words the word list's
// prefixes would catch (analyses = the plural noun; programmer; organism; optimism). Matched before the word list.
const ALLOW = [
  /Realistic Workplaces/g, /realisticWorkplaces/g,
  /\banalyses\b/gi, /\bprogrammers?\b/gi, /\borganisms?\b/gi, /\boptimis(?:m|t|ts|tic|tically)\b/gi, /\bfinalists?\b/gi,
];

const RE = BRITISH.map(([w, us]) => ({
  re: w.startsWith("re:") ? new RegExp("\\b" + w.slice(3), "gi")
    : new RegExp("\\b" + w.replace("*", "[a-z]*") + (w.endsWith("*") ? "" : "\\b"), "gi"),
  us,
}));

function hitsIn(text) {
  let t = text;
  for (const a of ALLOW) t = t.replace(a, (m) => " ".repeat(m.length));
  const out = [];
  for (const { re, us } of RE) {
    re.lastIndex = 0;
    let m;
    while ((m = re.exec(t))) out.push({ word: m[0], us });
  }
  return out;
}

/** C# string literals of one line (regular "..." and $"...", escapes skipped; verbatim @"" rare here and read the same way). */
function csLiterals(line) {
  const out = [];
  const re = /"((?:[^"\\]|\\.)*)"/g;
  let m;
  while ((m = re.exec(line))) out.push(m[1]);
  return out;
}
/** Every C# string literal of `text` as {text, line}: regular, $"" (the {holes} blanked: they are code), verbatim @"", raw """ ; comments and 'c' literals skipped (T130 part 3). */
function csStrings(text){
  const out=[],n=text.length;let i=0,line=1;
  const nl=(a,b)=>{for(let k=a;k<b;k++)if(text[k]==="\n")line++;};
  while(i<n){
    const c=text[i],d=text[i+1];
    if(c==="\n"){line++;i++;continue;}
    if(c==="/"&&d==="/"){const j=text.indexOf("\n",i);i=j<0?n:j;continue;}
    if(c==="/"&&d==="*"){let j=text.indexOf("*/",i+2);if(j<0)j=n;nl(i,j);i=j+2;continue;}
    if(c==="'"){i++;if(text[i]==="\\"){i++;} i++; while(i<n&&text[i]!=="'"&&text[i]!=="\n")i++; i++;continue;}
    if(c==="$"||c==="@"||c==='"'){
      const mm=/^(\$*)(@?)(\$*)(""")?/.exec(text.slice(i,i+10));
      const dollars=(mm[1]||"").length+(mm[3]||"").length, verb=mm[2]==="@";
      let p=i+mm[1].length+mm[2].length+mm[3].length;
      if(text[p]!=='"'){i++;continue;}
      let raw=!verb&&text.startsWith('"""',p);
      const startLine=line;let buf="";
      if(raw){let q=0;while(text[p+q]==='"')q++;const close='"'.repeat(q);p+=q;const j=text.indexOf(close,p);const body=text.slice(p,j<0?n:j);
        out.push({text:body,line:startLine,kind:"raw"});nl(p,j);i=j+q;continue;}
      p++;
      while(p<n){
        const ch=text[p];
        if(ch==="\n"&&!verb){break;}
        if(!verb&&ch==="\\"){buf+=text.slice(p,p+2);p+=2;continue;}
        if(verb&&ch==='"'){if(text[p+1]==='"'){buf+='"';p+=2;continue;}break;}
        if(!verb&&ch==='"')break;
        if(dollars&&ch==="{"){
          if(text[p+1]==="{"){buf+="{";p+=2;continue;}
          // hole: skip to matching }, strings inside
          let depth=1;p++;buf+=" ";
          while(p<n&&depth>0){const h=text[p];
            if(h==='"'){p++;while(p<n&&text[p]!=='"'){if(text[p]==="\\")p++;p++;}p++;continue;}
            if(h==="'"){p++;if(text[p]==="\\")p++;p+=2;continue;}
            if(h==="{")depth++;else if(h==="}")depth--;
            if(h==="\n")line++;
            p++;buf+=" ";}
          continue;
        }
        if(dollars&&ch==="}"&&text[p+1]==="}"){buf+="}";p+=2;continue;}
        if(ch==="\n")line++;
        buf+=ch;p++;
      }
      out.push({text:buf,line:startLine,kind:verb?"verbatim":"regular"});
      i=p+1;continue;
    }
    i++;
  }
  return out;
}
/** Every Python string literal of `text` as {text, line, doc, map}: ' " ''' """, r / b / u / f prefixes, escapes turned into spaces (a "\ncolour" must still start a word),
 * f-string {holes} blanked (they are code), # comments skipped. doc = a bare string statement (a docstring: section 5 reads those). map[k] = the source offset of text[k]. */
function pyStrings(text){
  const out=[],n=text.length;let i=0,line=1,depth=0,stmtStart=true;
  while(i<n){
    const c=text[i];
    if(c==="#"){const j=text.indexOf("\n",i);i=j<0?n:j;continue;}
    if(c==="\n"){line++;if(depth===0)stmtStart=true;i++;continue;}
    if(c===" "||c==="\t"||c==="\r"){i++;continue;}
    if(c==="\\"&&(text[i+1]==="\n"||text[i+1]==="\r")){if(text[i+1]==="\n")line++;i+=2;continue;}
    const m=/^([rRbBuUfF]{0,3})("""|'''|"|')/.exec(text.slice(i,i+6));
    if(m&&(m[1]===""||!/[A-Za-z0-9_]/.test(text[i-1]||" "))){
      const q=m[2],isRaw=/[rR]/.test(m[1]),isF=/[fF]/.test(m[1]),startLine=line;let p=i+m[0].length,buf="";const map=[];
      const put=(s,at)=>{buf+=s;for(let k=0;k<s.length;k++)map.push(at);};
      for(;;){
        if(p>=n)break;
        const ch=text[p];
        if(text.startsWith(q,p))break;
        if(q.length===1&&ch==="\n")break;
        if(ch==="\\"){
          const e=text[p+1];
          if(e==="\n")line++;
          if(isRaw)put(ch+(e==="\n"?"\n":e||""),p);
          else put((e==='"'||e==="'"||e==="\\")?e:(e==="\n"?"\n":" "),p);
          p+=2;continue;
        }
        if(isF&&ch==="{"){
          if(text[p+1]==="{"){put("{",p);p+=2;continue;}
          let d=1;put(" ",p);p++;
          while(p<n&&d>0){const h=text[p];
            if(h==='"'||h==="'"){const t3=text.startsWith(h.repeat(3),p)?h.repeat(3):h;put(" ",p);p+=t3.length;
              while(p<n&&!text.startsWith(t3,p)){if(text[p]==="\\")p++;if(text[p]==="\n")line++;p++;}
              p+=t3.length;continue;}
            if(h==="{")d++;else if(h==="}")d--;
            if(h==="\n"){line++;put("\n",p);}else put(" ",p);
            p++;}
          continue;
        }
        if(isF&&ch==="}"&&text[p+1]==="}"){put("}",p);p+=2;continue;}
        if(ch==="\n")line++;
        put(ch,p);p++;
      }
      const after=p+q.length;
      let doc=false;
      if(stmtStart&&depth===0){let k=after;while(text[k]===" "||text[k]==="\t")k++;doc=k>=n||"\n\r#;".includes(text[k]);}
      out.push({text:buf,line:startLine,doc,map});
      stmtStart=false;i=after;continue;
    }
    stmtStart=false;
    if("([{".includes(c))depth++;else if(")]}".includes(c))depth=Math.max(0,depth-1);else if(c===";"&&depth===0)stmtStart=true;
    i++;
  }
  return out;
}
function stripLineComment(line) {
  // a // outside a string literal starts a comment
  let inStr = false;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (c === "\\" && inStr) { i++; continue; }
    if (c === '"') inStr = !inStr;
    else if (!inStr && c === "/" && line[i + 1] === "/") return line.slice(0, i);
  }
  return line;
}
function walk(dir, pred, acc) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === "node_modules" || e.name === "obj" || e.name === "bin" || e.name.startsWith(".")) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, pred, acc);
    else if (pred(p)) acc.push(p);
  }
  return acc;
}

// ---- comments and prose (T130 part 2) -------------------------------------------------------------------------------------------
/** Offsets {s, e} of every comment (and, for Python, every bare-string docstring) in `text`. cs = C# string forms, rx = JS regex literals. */
function lexC(text, cs, rx) {
  const R = [], n = text.length; let i = 0;
  const canRegex = () => { let k = i - 1; while (k >= 0 && /[ \t]/.test(text[k])) k--; return k < 0 || "(,=:[!&|?{};+-*%<>~^".includes(text[k]) || /(?:return|typeof|case|in|of)$/.test(text.slice(Math.max(0, k - 6), k + 1)); };
  while (i < n) {
    const c = text[i], d = text[i + 1];
    if (c === "/" && d === "/") { let j = text.indexOf("\n", i); if (j < 0) j = n; R.push({ s: i + 2, e: j }); i = j; continue; }
    if (c === "/" && d === "*") { let j = text.indexOf("*/", i + 2); if (j < 0) j = n; R.push({ s: i + 2, e: j }); i = j + 2; continue; }
    if (cs && (c === "@" && d === '"' || c === "$" && d === "@" && text[i + 2] === '"' || c === "@" && d === "$" && text[i + 2] === '"')) {
      i = text.indexOf('"', i) + 1;
      for (;;) { const j = text.indexOf('"', i); if (j < 0) { i = n; break; } if (text[j + 1] === '"') { i = j + 2; continue; } i = j + 1; break; }
      continue;
    }
    if (cs && c === '"' && d === '"' && text[i + 2] === '"') { let q = 3; while (text[i + q] === '"') q++; const j = text.indexOf('"'.repeat(q), i + q); i = j < 0 ? n : j + q; continue; }
    if (c === '"' || (cs && c === "$" && d === '"')) { if (c === "$") i++; i++; while (i < n && text[i] !== '"' && text[i] !== "\n") { if (text[i] === "\\") i++; i++; } i++; continue; }
    if (c === "'") {
      if (cs) { i++; if (text[i] === "\\") i++; i += 2; continue; }
      i++; while (i < n && text[i] !== "'" && text[i] !== "\n") { if (text[i] === "\\") i++; i++; } i++; continue;
    }
    if (c === "`" && !cs) {
      i++; let depth = 0;
      while (i < n) { if (text[i] === "\\") { i += 2; continue; } if (text[i] === "`" && depth === 0) { i++; break; } if (text[i] === "$" && text[i + 1] === "{") { depth++; i += 2; continue; } if (text[i] === "}" && depth > 0) { depth--; i++; continue; } i++; }
      continue;
    }
    if (c === "/" && rx && canRegex()) {
      let j = i + 1, cls = false;
      while (j < n && text[j] !== "\n") { if (text[j] === "\\") { j += 2; continue; } if (text[j] === "[") cls = true; else if (text[j] === "]") cls = false; else if (text[j] === "/" && !cls) break; j++; }
      if (text[j] === "/") { i = j + 1; continue; }
    }
    i++;
  }
  return R;
}
function lexPy(text) {
  const R = [], n = text.length; let i = 0, depth = 0, stmtStart = true;
  while (i < n) {
    const c = text[i];
    if (c === "#") { let j = text.indexOf("\n", i); if (j < 0) j = n; R.push({ s: i + 1, e: j }); i = j; continue; }
    if (c === "\n") { if (depth === 0) stmtStart = true; i++; continue; }
    if (c === " " || c === "\t" || c === "\r") { i++; continue; }
    if (c === "\\" && (text[i + 1] === "\n" || text[i + 1] === "\r")) { i += 2; continue; }
    const m = /^([rRbBuUfF]{0,3})("""|'''|"|')/.exec(text.slice(i, i + 6));
    if (m) {
      const q = m[2], start = i + m[0].length; let j = start;
      for (;;) { if (j >= n) break; if (text[j] === "\\") { j += 2; continue; } if (text.startsWith(q, j)) break; if (q.length === 1 && text[j] === "\n") break; j++; }
      const after = j + q.length;
      if (stmtStart && depth === 0) { let k = after; while (text[k] === " " || text[k] === "\t") k++; if (k >= n || "\n\r#;".includes(text[k])) R.push({ s: start, e: j }); }
      stmtStart = false; i = after; continue;
    }
    stmtStart = false;
    if ("([{".includes(c)) depth++; else if (")]}".includes(c)) depth = Math.max(0, depth - 1); else if (c === ";" && depth === 0) stmtStart = true;
    i++;
  }
  return R;
}
function lexPs(text) {
  const R = [], n = text.length; let i = 0;
  while (i < n) {
    const c = text[i], d = text[i + 1];
    if (c === "<" && d === "#") { let j = text.indexOf("#>", i + 2); if (j < 0) j = n; R.push({ s: i + 2, e: j }); i = j + 2; continue; }
    if (c === "#") { let j = text.indexOf("\n", i); if (j < 0) j = n; R.push({ s: i + 1, e: j }); i = j; continue; }
    if (c === "@" && (d === "'" || d === '"') && /[\r\n]/.test(text[i + 2] || "")) { const j = text.indexOf("\n" + d + "@", i); i = j < 0 ? n : j + 3; continue; }
    if (c === "'") { i++; while (i < n) { if (text[i] === "'") { if (text[i + 1] === "'") { i += 2; continue; } break; } i++; } i++; continue; }
    if (c === '"') { i++; while (i < n) { if (text[i] === "`") { i += 2; continue; } if (text[i] === '"') { if (text[i + 1] === '"') { i += 2; continue; } break; } i++; } i++; continue; }
    i++;
  }
  return R;
}
function lexSh(text) {
  const R = [], n = text.length; let i = 0;
  while (i < n) {
    const c = text[i];
    if (c === "#" && (i === 0 || /[\s;]/.test(text[i - 1]))) { let j = text.indexOf("\n", i); if (j < 0) j = n; R.push({ s: i + 1, e: j }); i = j; continue; }
    if (c === "'") { const j = text.indexOf("'", i + 1); i = j < 0 ? n : j + 1; continue; }
    if (c === '"') { i++; while (i < n && text[i] !== '"') { if (text[i] === "\\") i++; i++; } i++; continue; }
    i++;
  }
  return R;
}
/** A whitespace-delimited chunk that is a parameter (a=b), a path or a file name: its spelling is data. */
function pathLike(chunk) {
  return /[=\\]/.test(chunk) || /^[./~]/.test(chunk) || /\w\/\w+\/\w/.test(chunk) || (/\.[A-Za-z]{1,5}$/.test(chunk) && chunk.includes("/")) ||
    (/\w\.\w/.test(chunk.replace(/[A-Za-z]\.$/, "")) && /\.[a-z]{2,4}\b/i.test(chunk));
}
/** Hits in the text of one string literal (a Python message, report or note). Like proseHits, an identifier (colour_set, GladeOutlineColour) or a path is
 * data and not checked; unlike proseHits there is no backtick, quotation or "spelling" exemption: the words are the program's own output. */
function literalHits(literal) {
  let text = literal;
  for (const a of ALLOW) text = text.replace(a, (m) => " ".repeat(m.length));
  const out = [];
  for (const m of text.matchAll(/[A-Za-z][A-Za-z0-9_]*/g)) {
    const tok = m[0];
    if (/[_0-9]/.test(tok) || /[a-z][A-Z]/.test(tok)) continue;
    const hs = hitsIn(tok);
    if (hs.length === 0) continue;
    let a = m.index; while (a > 0 && !/[\s|]/.test(text[a - 1])) a--;
    let b = m.index + tok.length; while (b < text.length && !/[\s|]/.test(text[b])) b++;
    const chunk = text.slice(a, b).replace(/^[("'*[{<]+|[)"'*\]}>,.;:!?]+$/g, "");
    if (pathLike(chunk)) continue;
    // a code name inside a message: an attribute (Mark.centre) or a call written with empty parentheses (storey())
    if (/[A-Za-z_]\.$/.test(text.slice(0, m.index)) || text.startsWith("()", m.index + tok.length)) continue;
    out.push(...hs.map((h) => ({ ...h, idx: m.index })));
  }
  if (/\bper cent\b/i.test(text)) out.push({ word: "per cent", us: "percent", idx: text.search(/\bper cent\b/i) });
  return out;
}
/** The British spellings in the string literals of one Python source text, as {word, us, line}. A bare-string statement (docstring) is section 5's; `allow` = exact literal texts that are data. */
function pyLiteralHits(text, allow) {
  const out = [];
  for (const lit of pyStrings(text)) {
    if (lit.doc || allow.includes(lit.text)) continue;
    for (const h of literalHits(lit.text)) out.push({ word: h.word, us: h.us, line: lit.line + (lit.text.slice(0, h.idx).match(/\n/g) || []).length });
  }
  return out;
}
const SPELLING_TALK = /British|US English|T130|check-us-english|spelling/i;
/** Hits in one line of comment / prose. Identifiers, paths, `code` and lines that discuss spellings are not checked. */
function proseHits(line) {
  if (SPELLING_TALK.test(line)) return [];
  let text = line.replace(/`[^`]*`/g, (m) => " ".repeat(m.length));
  // a quotation (a double-quoted span of 3+ words: the owner's words, a wiki, a log line) keeps its spelling; an odd quote count = no span
  const qi = []; for (let i = 0; i < text.length; i++) if (text[i] === '"' && text[i - 1] !== "\\" && text[i + 1] !== '"' && text[i - 1] !== '"') qi.push(i);
  if (qi.length % 2 === 0) {
    for (let k = 0; k < qi.length; k += 2) {
      const inner = text.slice(qi[k] + 1, qi[k + 1]);
      if (inner.length >= 12 && inner.trim().split(/[ \t]+/).length >= 3) text = text.slice(0, qi[k]) + " ".repeat(qi[k + 1] - qi[k] + 1) + text.slice(qi[k + 1] + 1);
    }
  }
  const out = [];
  for (const m of text.matchAll(/[A-Za-z][A-Za-z0-9_]*/g)) {
    const tok = m[0];
    if (/[_0-9]/.test(tok) || /[a-z][A-Z]/.test(tok)) continue;
    const hs = hitsIn(tok);
    if (hs.length === 0) continue;
    let a = m.index; while (a > 0 && !/[\s|]/.test(text[a - 1])) a--;
    let b = m.index + tok.length; while (b < text.length && !/[\s|]/.test(text[b])) b++;
    const chunk = text.slice(a, b).replace(/^[("'*[{<]+|[)"'*\]}>,.;:!?]+$/g, "");
    if (pathLike(chunk)) continue;
    out.push(...hs);
  }
  if (/\bper cent\b/i.test(text)) out.push({ word: "per cent", us: "percent" });
  // a line holding a British word AND its US form is discussing spellings
  return out.filter((h) => h.us.includes("-") || !new RegExp("\\b" + h.us + "\\b", "i").test(text));
}
const SRC_SKIP = new Set(["node_modules", "obj", "bin", "build", "dist", "types", "archive", "__pycache__", "decompile"]);
function walkSrc(dir, pred, acc) {
  if (!fs.existsSync(dir)) return acc;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    // assets/blender/tests/data: byte-exact copies of old generator modules (sha1-pinned by their tests), never respelled
    if (SRC_SKIP.has(e.name) || e.name.startsWith("baseline-") || (e.name === "data" && path.basename(dir) === "tests" && rel(dir).startsWith("assets/blender")) || (e.name.startsWith(".") && e.name !== ".claude")) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walkSrc(p, pred, acc); else if (pred(p)) acc.push(p);
  }
  return acc;
}
// Files another session still owns (the tower-number tiles awaited the verdict when T130 part 2 ran): skipped, and counted in the
// last line. Delete an entry once its comments are converted.
// Documents about the spellings themselves (they list British words as their subject): not scanned.
const SPELLING_DOCS = ["docs/COMMIT-HASH-MAP-2026-10-22.md", "docs/COMMIT-HASH-MAP-2026-10-24.md", "docs/research/us-english-sweep-2026-10-20.md", "docs/research/release-prep/python-us-english-2026-10-24.md"];
const PENDING = [];

// --self-test: the gate's own fixtures (each British sample must hit, each US / allowed sample must not).
if (process.argv.includes("--self-test")) {
  const bad = ["difficulty colours", "Too flat: grey.", "at its centre", "15 metres", "Some trees cancelled", "an aluminium tube", "modelled into"];
  const good = ["difficulty colors", "gray", "center", "meters", "canceled", "aluminum", "modeled", "Detected: Realistic Workplaces.", "greyhound"];
  const missed = bad.filter((s) => hitsIn(s).length === 0);
  const wrong = good.filter((s) => s !== "greyhound" && hitsIn(s).length > 0);
  // "greyhound" is a known false positive of the grey* prefix rule: it must hit, so a looser rule is noticed.
  if (hitsIn("greyhound").length === 0) wrong.push("greyhound (prefix rule changed)");
  // comment / prose fixtures (proseHits)
  const cBad = ["the centre of the tile", "kit colour variants", "rasterise the quad", "a two-storey lodge", "equalised areas", "texel centres, metres", "grey-white"];
  const cGood = ["the center of the tile", "colour_set keeps its name", "the `centre` argument", "GladeOutlineColour is a constant", "see kit-colour-variants.md",
    "docs/research/colour/x", "pass centre=True", "three analyses", "a programmer", "an organism", "optimism", "the British colour spelling", "colour / color", "colour->color",
    'The rule: "a colour that makes sense for that building" is kept'];
  for (const s of cBad) if (proseHits(s).length === 0) missed.push("comment: " + s);
  for (const s of cGood) if (proseHits(s).length > 0) wrong.push("comment: " + s);
  // C# string literal fixtures (csStrings): British text inside a literal hits; the same word in a comment, an identifier, a {hole} or a 'c' does not
  const csBad = ['Log.Info("cancelled spots");', 'x = $"{n} metres, {m:F1}";', 'y = @"the ""grey"" tile";', 'z = """a colour"""', 'a = "x" + "the centre";'];
  const csGood = ['// the centre of the tile', 'int centre = 0; /* colour */', 'q = $"{centre} m, {(ok ? "yes" : "no")}";', "c = 'x'; d = \"center\";"];
  for (const t of csBad) if (!csStrings(t).some((l) => hitsIn(l.text).length)) missed.push("cs literal: " + t);
  for (const t of csGood) if (csStrings(t).some((l) => hitsIn(l.text).length)) wrong.push("cs literal: " + t);
  // Python string literal fixtures (pyLiteralHits): ' " ''' """, r / b / f prefixes, escapes, {holes}, comments, docstrings, code names, the allow list, line numbers
  const pyBad = ['print("the colour of it")', "x = 'a centre'", "s = '''line\nthe modelled one'''", 'raise ValueError(f"{n} metres, {m:.1f}")',
    'raise ValueError(f"cannot be recoloured")', String.raw`s = "line\ncolour"`, 'a = r"raw grey"', 'a = b"neighbour"', 'a = rf"{x} centre"', 't = ("a "\n "the centre")',
    'a = "don\'t centre"', "# it's here\nx = 'grey'", 'y = ["one", "two", "a metre"]', "print(f\"{ {'a': 1}['a'] } centre\")", 'x = f"{{centre}} braces"',
    'x = """\nthe centre\n"""', "x = 'a' if y else 'tyre'"];
  const pyGood = ['# the centre of the tile', 'def f():\n    """the colour here"""\n    return 1', 'd["colour_set"] = 1', "x = f\"{centre} m {(a if b else 'colour')}\"",
    'x = "Mark.centre"', 'x = "storey() failed"', 'x = "docs/research/colour/x.md"', 'print("center")', 'x = "it\'s a # not a comment"', 'x = "GladeOutlineColour"',
    "x = 'colour_set'  # the centre", "x = f'{a}' 'center' \"gray\""];
  for (const t of pyBad) if (pyLiteralHits(t, []).length === 0) missed.push("py literal: " + JSON.stringify(t));
  for (const t of pyGood) if (pyLiteralHits(t, []).length > 0) wrong.push("py literal: " + JSON.stringify(t));
  if (pyLiteralHits('d = {"centre": 1}', ["centre"]).length !== 0) wrong.push("py literal: PY_LITERAL_ALLOW entry ignored");
  if (pyLiteralHits('d = {"centre": 1}', []).length !== 1) missed.push("py literal: a bare \"centre\" without an allow entry");
  const ln = (t) => pyLiteralHits(t, []).map((h) => h.line).join(",");
  if (ln("a = 1\nb = '''x\ny'''\nc = 'grey'") !== "4") wrong.push("py literal line number (single)");
  if (ln("b = '''x\ny\nthe centre'''\nc = 1") !== "3") wrong.push("py literal line number (inside a triple-quoted literal)");
  const pyN = pyBad.length + 1;
  console.log(`check-us-english --self-test: ${bad.length + cBad.length + csBad.length + pyN - missed.length} / ${bad.length + cBad.length + csBad.length + pyN} British samples caught, ${wrong.length} wrong`);
  for (const s of missed) console.log(`  MISSED ${s}`);
  for (const s of wrong) console.log(`  WRONG  ${s}`);
  process.exit(missed.length + wrong.length === 0 ? 0 : 1);
}

const hits = [];
const add =(file, line, list) => { for (const h of list) hits.push({ file: path.relative(ROOT, file), line, word: h.word, us: h.us }); };
let scanned = 0;

// 1. locale sources
for (const rel of ["mod/SkiResortMod/LocaleEN.cs", "mod/SkiResortMod/Settings/LocaleEN.cs"]) {
  const f = path.join(ROOT, rel);
  fs.readFileSync(f, "utf8").split(/\r?\n/).forEach((l, i) => {
    if (/^\s*\/\//.test(l)) return;
    for (const s of csLiterals(stripLineComment(l))) { scanned++; add(f, i + 1, hitsIn(s)); }
  });
}
// 2. hub tuples
for (const f of walk(path.join(ROOT, "mod/SkiResortUI/src"), (p) => /\.tsx?$/.test(p), [])) {
  fs.readFileSync(f, "utf8").split(/\r?\n/).forEach((l, i) => {
    const re = /\[\s*"[A-Za-z][\w.]*(?:\[[^\]"]*\])?(?::\d+)?"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\]/g;
    let m;
    while ((m = re.exec(l))) { scanned++; add(f, i + 1, hitsIn(m[1])); }
  });
}
// 3. C# log calls (a call may span lines: from Log.<Level>( to the closing ");")
for (const f of walk(path.join(ROOT, "mod/SkiResortMod"), (p) => p.endsWith(".cs"), [])) {
  const lines = fs.readFileSync(f, "utf8").split(/\r?\n/);
  let inCall = false;
  lines.forEach((raw, i) => {
    const l = stripLineComment(raw);
    if (!inCall && /\b[Ll]og\.(Info|Warn|Error|Debug|Verbose|Critical|Fatal|Trace)(Format)?\s*\(/.test(l)) inCall = true;
    if (!inCall) return;
    for (const s of csLiterals(l)) { scanned++; add(f, i + 1, hitsIn(s)); }
    if (/\)\s*;\s*$/.test(l.trim())) inCall = false;
  });
}
// 4. docs-site prose
const docsDir = path.join(ROOT, "docs-site/src/content");
if (fs.existsSync(docsDir)) {
  for (const f of walk(docsDir, (p) => /\.mdx?$/.test(p), [])) {
    let fence = false;
    fs.readFileSync(f, "utf8").split(/\r?\n/).forEach((l, i) => {
      if (/^\s*```/.test(l)) { fence = !fence; return; }
      if (fence) return;
      scanned++;
      add(f, i + 1, hitsIn(l));
    });
  }
}

// 5. comments and docstrings of the source (T130 part 2)
const pending = new Set(PENDING);
let pendingSeen = 0;
const rel = (f) => path.relative(ROOT, f).split(path.sep).join("/");
const SRC_EXT = /\.(cs|tsx?|jsx?|mjs|cjs|scss|css|py|ps1|sh)$/i;
const srcFiles = [];
for (const d of ["mod", "tests", "tools", "assets/blender", "docs", "docs-site/src", "docs-site/tools"]) walkSrc(path.join(ROOT, d), (p) => SRC_EXT.test(p), srcFiles);
for (const f of srcFiles) {
  const r = rel(f);
  if (r === "tools/check-us-english.js") continue;
  if (pending.has(r)) { pendingSeen++; continue; }
  const text = fs.readFileSync(f, "utf8");
  let ranges;
  if (/\.cs$/i.test(f)) ranges = lexC(text, true, false);
  else if (/\.(tsx?|jsx?|mjs|cjs)$/i.test(f)) ranges = lexC(text, false, true);
  else if (/\.(scss|css)$/i.test(f)) ranges = lexC(text, false, false);
  else if (/\.py$/i.test(f)) ranges = lexPy(text);
  else if (/\.ps1$/i.test(f)) ranges = lexPs(text);
  else ranges = lexSh(text);
  for (const rg of ranges) {
    const seg = text.slice(rg.s, rg.e);
    const base = text.slice(0, rg.s).split("\n").length;
    seg.split("\n").forEach((l, i) => { scanned++; add(f, base + i, proseHits(l)); });
  }
}
// 6. prose of the markdown notes: docs/ (not docs/archive/), READMEs, release/, .claude/, assets/ and tests/ notes (code fences too)
const mdFiles = [];
for (const d of ["docs", "release", ".claude", "assets/blender", "assets/import", "assets/we", "assets/external", "mod", "tools", "tests"]) walkSrc(path.join(ROOT, d), (p) => /\.md$/i.test(p), mdFiles);
for (const f of ["README.md", "CLAUDE.md", "docs-site/README.md"]) if (fs.existsSync(path.join(ROOT, f))) mdFiles.push(path.join(ROOT, f));
for (const f of mdFiles) {
  if (SPELLING_DOCS.includes(rel(f))) continue;
  fs.readFileSync(f, "utf8").split(/\r?\n/).forEach((l, i) => { scanned++; add(f, i + 1, proseHits(l)); });
}

// 7. every C# string literal of the mod and the test hosts: log lines, DUMP text, warnings, assert messages (T130 part 3, 2026-10-24).
// CS_LITERAL_ALLOW: literals that are British ON PURPOSE (a test's own fixture list of the words it forbids; a JSON key the data file
// spells that way): file -> exact literal texts. Add an entry only with a reason beside it.
const CS_LITERAL_ALLOW = {
  "tests/LiftBatch/LiftToolInputTests.cs": ["colour", "metre", "centre", "grey", "behaviour", "cancelled"], // TI-4 forbidden-word list
  "tests/GradeBatch/Program.cs": ["colour", "metre", "centre", "grey"], // GR-15 forbidden-word list
  "tests/LiftBatch/TowerNumberTests.cs": ["centre"], // JSON key of tests/LiftBatch/data/tower-number-plates.json
};
const csCounts = {};
for (const f of walkSrc(ROOT, (p) => /\.cs$/i.test(p), []).filter((p) => /^(mod|tests|tools)\//.test(rel(p)))) {
  const r = rel(f);
  if (r === "tools/check-us-english.js") continue;
  const allow = CS_LITERAL_ALLOW[r] || [];
  for (const lit of csStrings(fs.readFileSync(f, "utf8"))) {
    scanned++;
    if (allow.includes(lit.text)) continue;
    const hs = hitsIn(lit.text);
    if (hs.length) { add(f, lit.line, hs); csCounts[r] = (csCounts[r] || 0) + hs.length; }
  }
}
if (process.argv.includes("--cs-counts")) for (const [k, v] of Object.entries(csCounts)) console.log(`cs-literals ${v}  ${k}`);

// 8. every Python string literal of the Blender generators and their tests: print / log / assert messages, report and manifest text
// (T130 part 4, 2026-10-24). Docstrings are bare-string statements: section 5 already reads those. tests/data is skipped (frozen copies).
// PY_LITERAL_ALLOW: literals that are British ON PURPOSE, file -> exact literal texts; add an entry only with a reason beside it.
const PY_LITERAL_ALLOW = {
  "*": ["centre"], // dict key of the frames / plate / wheel / door records that manifests, tests and tower-number-plates.json read
  "assets/blender/dress_kit.py": ["colour"], // PROPS registry key spec["colour"], also written as the manifest "colour" block
  "assets/blender/tests/test_dress_kit.py": ["colour"], // reads that same registry / manifest key
  "assets/blender/cs2_liftdress.py": ["grey"], // INK palette key
  "assets/blender/lift_terminal_detach.py": ["tyre"], // object-name prefix of the tyre_* / tyrebank_* meshes (hidden / LOD1 / bounds tables)
  "assets/blender/tests/test_bldg_kit.py": ["storey"], // builder name: K.storey, the keys of the builder tables
};
const pyCounts = {};
for (const f of walkSrc(path.join(ROOT, "assets/blender"), (p) => /\.py$/i.test(p), [])) {
  const r = rel(f);
  const text = fs.readFileSync(f, "utf8");
  scanned += pyStrings(text).length;
  for (const h of pyLiteralHits(text, PY_LITERAL_ALLOW["*"].concat(PY_LITERAL_ALLOW[r] || []))) { add(f, h.line, [h]); pyCounts[r] = (pyCounts[r] || 0) + 1; }
}
if (process.argv.includes("--py-counts")) for (const [k, v] of Object.entries(pyCounts)) console.log(`py-literals ${v}  ${k}`);

for (const h of hits) console.log(`${h.file}:${h.line}: "${h.word}" -> ${h.us}`);
if (pendingSeen) console.log(`check-us-english: ${pendingSeen} PENDING file(s) not scanned (see PENDING in this file)`);
console.log(`check-us-english: ${scanned} text(s) scanned, ${hits.length} British spelling(s) ${hits.length === 0 ? "- PASS" : "- FAIL"}`);
process.exit(hits.length === 0 ? 0 : 1);
