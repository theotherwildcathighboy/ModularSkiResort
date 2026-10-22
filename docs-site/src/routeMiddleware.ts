// Starlight route middleware (https://starlight.astro.build/guides/route-data/). Starlight prefixes
// the base path onto its sidebar and header links but not onto the `link` of a page's frontmatter
// hero actions, so `link: /quickstart/` in index.mdx would 404 under a project base such as
// /ModularSkiResort. Prefix it here from BASE_URL. Links in page content are handled by the mdast
// plugin in astro.config.mjs. Checked by tools/check-base-links.js.
import { defineRouteMiddleware } from '@astrojs/starlight/route-data';

const base = import.meta.env.BASE_URL.replace(/\/$/, '');

export const onRequest = defineRouteMiddleware((context) => {
  const hero = context.locals.starlightRoute.entry.data.hero;
  if (!base || !hero?.actions) return;
  for (const action of hero.actions) {
    if (action.link.startsWith('/') && !action.link.startsWith('//') && !action.link.startsWith(base + '/')) {
      action.link = base + action.link;
    }
  }
});
