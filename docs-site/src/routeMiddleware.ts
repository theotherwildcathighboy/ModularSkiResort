// Starlight route middleware (https://starlight.astro.build/guides/route-data/). Starlight adds
// the base path to its sidebar and header links. It does not add the base path to the `link` of a
// page's frontmatter hero actions. So `link: /quickstart/` in index.mdx would 404 under a project
// base such as /ModularSkiResort. This code adds the prefix from BASE_URL. The mdast plugin in
// astro.config.mjs handles links in page content. tools/check-base-links.js checks the result.
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
