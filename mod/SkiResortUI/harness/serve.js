/*
 * harness/serve.js - tiny static server for the browser harness (MIT, 2026-10-18).
 * Root = mod/SkiResortUI (so /harness/ and /build/ are both reachable). /game/index.css streams the
 * installed game's UI stylesheet (theme variables) straight from the Steam folder; nothing is copied.
 * Run: node mod/SkiResortUI/harness/serve.js [port]   (default 5173)  ->  http://localhost:5173/harness/
 * Deploys nothing, writes nothing.
 */
"use strict";
const http = require("http");
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const PORT = Number(process.argv[2] || process.env.PORT || 5173);
const GAME_UI = process.env.CS2_UI_DIR ||
  "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Cities Skylines II\\Cities2_Data\\Content\\Game\\UI";
const MIME = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".mjs": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8", ".json": "application/json", ".svg": "image/svg+xml", ".png": "image/png", ".txt": "text/plain; charset=utf-8" };

function send(res, code, body, type) {
  res.writeHead(code, { "Content-Type": type || "text/plain; charset=utf-8", "Cache-Control": "no-store" });
  res.end(body);
}
// In the game, CSS and the hub reference assets by coui:// URLs and by paths relative to the game's
// index.html ("Media/Glyphs/Warning.svg"). The browser knows neither, so served CSS is rewritten and
// the game's Media / Fonts / Static folders are mounted read-only under /game/ and beside /harness/.
function rewriteCss(text) {
  return text
    .replace(/coui:\/\/ui-mods\/SkiResortMod-img\//g, "/build/SkiResortMod-img/")
    .replace(/coui:\/\/GameUI\//g, "/game/")
    .replace(/coui:\/\/gameui\//g, "/game/");
}
function stream(res, file) {
  fs.stat(file, (err, st) => {
    if (err || !st.isFile()) return send(res, 404, "not found: " + file);
    const type = MIME[path.extname(file).toLowerCase()] || "application/octet-stream";
    if (type.startsWith("text/css")) {
      return fs.readFile(file, "utf8", (e2, txt) => e2 ? send(res, 500, String(e2)) : send(res, 200, rewriteCss(txt), type));
    }
    res.writeHead(200, { "Content-Type": type, "Cache-Control": "no-store", "Content-Length": st.size });
    fs.createReadStream(file).pipe(res);
  });
}
const GAME_DIRS = ["Media", "Fonts", "Static"];
function gameFile(rel) {
  const f = path.normalize(path.join(GAME_UI, rel));
  return f.startsWith(path.normalize(GAME_UI)) ? f : null;
}
http.createServer((req, res) => {
  const url = decodeURIComponent((req.url || "/").split("?")[0]);
  if (url === "/" ) { res.writeHead(302, { Location: "/harness/" }); return res.end(); }
  if (url === "/game/index.css") return stream(res, path.join(GAME_UI, "index.css"));
  const m = /^\/(?:game\/|harness\/)?(Media|Fonts|Static)\/(.+)$/.exec(url);
  if (m && GAME_DIRS.includes(m[1])) { const f = gameFile(path.join(m[1], m[2])); return f ? stream(res, f) : send(res, 403, "forbidden"); }
  if (url.startsWith("/game/")) return send(res, 404, "only index.css, Media/, Fonts/ and Static/ are served from the game folder");
  let rel = url.endsWith("/") ? url + "index.html" : url;
  const file = path.normalize(path.join(ROOT, rel));
  if (!file.startsWith(ROOT)) return send(res, 403, "forbidden");
  stream(res, file);
}).listen(PORT, "127.0.0.1", () => {
  console.log(`hub harness: http://localhost:${PORT}/harness/  (root ${ROOT}; game css from ${GAME_UI})`);
});
