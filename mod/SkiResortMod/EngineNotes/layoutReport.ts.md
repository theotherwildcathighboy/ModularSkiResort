# layoutReport.ts: the uiLayout dev aid, why the rem is measured with a probe, and when it measures

The `uiLayout` dev aid sends measured panel heights to the mod log, to check the estimated budget in the field (header ~48, footer
~114, content ~616 rem at 1080p / 100 % text; ~7 rows visible with Trees open). Any failure (an API Gameface lacks) is swallowed: a dev aid
must never break the panel.

## Text format

`header=40 content=647 footer=90 list=109 rows=1 scale=1 rem=2.00px status=22 problems=67 tiles=42 view=overview viewH=406 over=0`

Heights are in rem, rounded. `scale` is the root's `--fontScale` (`index.js@801715` `applyTextScale`); `rem` is the measured px of
1rem. If no rem can be measured the numbers are px and the text ends in " px". `status` / `problems` / `tiles` / `view` are our own
blocks of the content (status row, problems band, tile row, view slot; -1 for a missing element). `over` is how far the Scrollable's
content box overflows, in rem = `max(0, round((scrollHeight - offsetHeight) / rem))` of the content element's parent (the Scrollable's
content div; the Providers around our body render no DOM), -1 when there is no such parent. The vanilla Scrollable shows its bar when
`scrollHeight - 1 > offsetHeight` (`index.js@638418`).

## The rem is measured

A probe element of our own, 100rem wide, out of flow and never drawn (`hub.module.scss` `.remProbe`, rendered by `HubPanel` in the
header slot), divided by 100. `parseFloat(getComputedStyle(html).fontSize)` read 38.4 times too large: the game declares the root size in
viewport units (`index.css` `html{font-size:.0925926vh}`, and `.0520833vw` under `@media(min-height:56.25vw)`), Gameface most likely
returned the declared string, and `parseFloat` dropped the unit. A probe also covers Interface Scaling off (the game sets the root to
1px, `index.js@801713` `applyInterfaceScalingEnabled`) and screens taller than 16:9, where "viewport height / 1080" would be wrong.

## When it measures

One animation frame after a view change Gameface has not laid out the new view yet (cause unverified), so `viewH` would read the
PREVIOUS view's height. A measure therefore starts on the SECOND animation frame after the change and repeats once a frame until two
consecutive texts agree (the layout has settled), at most `SETTLE_MAX_FRAMES` frames, then sends the last text. A handful of
`getBoundingClientRect` calls per change, never per frame otherwise.

It is sent after the panel's first layout, whenever the view or the visible row-count BUCKET changes (0, 1-4, 5-9, 10-19, 20+), and
whenever the text scale, the root font size or the viewport changes while the panel stays open: a 1 s timer reads two root style
strings and the viewport size (no layout, no measurement) and measures only when that key changed. A text is sent at most ONCE per UI
session (a module-level set of the texts already sent, at most 64, cleared when full: alternating views would otherwise send a line on
every switch), and C# logs `Hub [layout]` only when the text differs from its last one, at most `Tuning.LayoutLinesMax` lines per load.

## Finding the Panel's boxes

The Panel's own header / content / footer boxes are not ours (`cs2/ui` Panel, `index.js@684600`), so they are found from our own
elements: the lowest common ancestor of our header and footer roots is the panel box; each of our roots' ancestor directly under it is
that part's box.
