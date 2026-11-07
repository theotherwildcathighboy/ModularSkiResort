# layoutReport.ts: the uiLayout dev aid, why the rem is measured with a probe, and when it measures

The `uiLayout` dev aid sends measured panel heights to the mod log. This checks the estimated budget in the field. The budget is:
header ~48, footer ~114, content ~616 rem at 1080p / 100 % text, and ~7 rows visible with Trees open. The aid swallows any failure
(an API Gameface lacks). A dev aid must never break the panel.

## Text format

`header=40 content=647 footer=90 list=109 rows=1 scale=1 rem=2.00px status=22 problems=67 tiles=42 view=overview viewH=406 over=0`

Heights are in rem, rounded. The fields:

- `scale` is the root's `--fontScale` (`index.js@801715` `applyTextScale`).
- `rem` is the measured px of 1rem. If the code cannot measure the rem, the numbers are px and the text ends in " px".
- `status` / `problems` / `tiles` / `view` are our own blocks of the content (status row, problems band, tile row, view slot).
  The value is -1 for a missing element.
- `over` is how far the Scrollable's content box overflows, in rem. It equals `max(0, round((scrollHeight - offsetHeight) / rem))` of
  the content element's parent. That parent is the Scrollable's content div (the Providers around our body render no DOM).
  The value is -1 when there is no such parent.

The vanilla Scrollable shows its bar when `scrollHeight - 1 > offsetHeight` (`index.js@638418`).

## The rem is measured

The code measures the rem with a probe element of our own. The probe is 100rem wide, out of flow and never drawn (`hub.module.scss`
`.remProbe`, rendered by `HubPanel` in the header slot). The code divides its width by 100. `parseFloat(getComputedStyle(html).fontSize)`
read 38.4 times too large. The game declares the root size in viewport units (`index.css` `html{font-size:.0925926vh}`, and `.0520833vw`
under `@media(min-height:56.25vw)`). Gameface most likely returned the declared string, and `parseFloat` dropped the unit. A probe also
covers Interface Scaling off (the game sets the root to 1px, `index.js@801713` `applyInterfaceScalingEnabled`) and screens taller than
16:9, where "viewport height / 1080" would be wrong.

## When it measures

One animation frame after a view change, Gameface has not laid out the new view yet (cause unverified). So `viewH` would read the
PREVIOUS view's height. A measure therefore starts on the SECOND animation frame after the change. It repeats once a frame until two
consecutive texts agree (the layout has settled), for at most `SETTLE_MAX_FRAMES` frames. Then it sends the last text. The code makes a
handful of `getBoundingClientRect` calls per change, and never makes them per frame otherwise.

The code sends the text in these cases:

- After the panel's first layout.
- Whenever the view or the visible row-count BUCKET changes (0, 1-4, 5-9, 10-19, 20+).
- Whenever the text scale, the root font size or the viewport changes while the panel stays open. A 1 s timer reads two root style
  strings and the viewport size (no layout, no measurement). It measures only when that key changed.

The UI sends a text at most ONCE per UI session. It keeps a module-level set of the texts already sent, at most 64, and clears the set
when full. Without this set, alternating views would send a line on every switch. C# logs `Hub [layout]` only when the text differs from
its last one, and at most `Tuning.LayoutLinesMax` lines per load.

## Finding the Panel's boxes

The Panel's own header / content / footer boxes are not ours (`cs2/ui` Panel, `index.js@684600`). So the code finds them from our own
elements. The lowest common ancestor of our header and footer roots is the panel box. For each of our roots, the ancestor directly under
the panel box is that part's box.
