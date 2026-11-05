// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import { satteri } from '@astrojs/markdown-satteri';
import { readFileSync } from 'node:fs';

// Heading anchor links copy the section URL on click (src/scripts/anchor-copy.js), inlined so the
// base path never matters.
const anchorCopyScript = readFileSync(new URL('./src/scripts/anchor-copy.js', import.meta.url), 'utf8');

// Deployed by the Pages workflow, which sets DOCS_SITE and DOCS_BASE from the repository
// (https://<owner>.github.io + /<repo>). Locally both fall back to the root.
// A custom domain later: set DOCS_SITE to it, DOCS_BASE to '/', and add public/CNAME.
const site = process.env.DOCS_SITE || 'http://localhost:4321';
const base = process.env.DOCS_BASE || '/';

// Astro never rewrites links written inside Markdown / MDX content: `[x](/guides/lifts/)` stays
// root-relative and 404s under a project base path such as /ModularSkiResort. Astro's docs say to
// build such hrefs from `import.meta.env.BASE_URL` (https://docs.astro.build/en/guides/routing/,
// "base"), which a Markdown link cannot do, and Starlight prefixes only its own sidebar, header and
// hero links. Astro 7's default Markdown processor is Satteri, whose documented extension point is
// an mdast plugin (https://docs.astro.build/en/reference/configuration-reference/#markdownprocessor),
// so this plugin prefixes the configured base onto every root-relative link, image and definition
// in content, and onto `href` / `src` of JSX in MDX (`.mdx` inherits markdown.processor). With
// base '/' it does nothing. Write content links as plain root-relative paths (`/reference/lifts/`);
// never hard-code the repository name. `tools/check-base-links.js` proves it on the built HTML and
// is wired into `npm run build`.
const baseNoSlash = '/' + base.replace(/^\/+|\/+$/g, '');
const prefix = baseNoSlash === '/' ? '' : baseNoSlash;

/** @param {unknown} url */
function withBase(url) {
  if (!prefix || typeof url !== 'string') return url;
  if (!url.startsWith('/') || url.startsWith('//')) return url; // relative, absolute URL, protocol-relative
  if (url === prefix || url.startsWith(prefix + '/') || url.startsWith(prefix + '#')) return url; // already prefixed
  return prefix + url;
}

/** @param {any} node @param {any} ctx */
function prefixJsxAttributes(node, ctx) {
  const attributes = node.attributes ?? [];
  const next = attributes.map((/** @type {any} */ attr) =>
    attr.type === 'mdxJsxAttribute' && (attr.name === 'href' || attr.name === 'src') && typeof attr.value === 'string' && withBase(attr.value) !== attr.value
      ? { ...attr, value: withBase(attr.value) }
      : attr,
  );
  // Only a real change writes: Satteri refuses setProperty on a JSX node's attributes, and a relative src like <Shot src="x.jpg"> needs no prefix.
  if (next.some((/** @type {any} */ attr, /** @type {number} */ i) => attr !== attributes[i])) ctx.setProperty(node, 'attributes', next);
}

/** @param {string} key */
const prefixUrl = (key) => (/** @type {any} */ node, /** @type {any} */ ctx) => {
  const fixed = withBase(node[key]);
  if (fixed !== node[key]) ctx.setProperty(node, key, fixed);
};

const baseLinksPlugin = {
  name: 'prefix-base-on-root-relative-links',
  link: prefixUrl('url'),
  image: prefixUrl('url'),
  definition: prefixUrl('url'),
  mdxJsxFlowElement: prefixJsxAttributes,
  mdxJsxTextElement: prefixJsxAttributes,
};

export default defineConfig({
  site,
  base,
  markdown: { processor: satteri({ mdastPlugins: [baseLinksPlugin] }) },
  integrations: [
    starlight({
      title: 'Modular Ski Resort',
      description:
        'Guides, reference, asset pipeline and timeline for Modular Ski Resort, a ski resort mod for Cities: Skylines II.',
      routeMiddleware: './src/routeMiddleware.ts',
      head: [{ tag: 'script', content: anchorCopyScript }],
      customCss: ['./src/styles/custom.css'],
      logo: {
        light: './src/assets/resort-logo-navy.svg',
        dark: './src/assets/resort-logo-white.svg',
        alt: 'Modular Ski Resort logo, a peak and a snowflake',
      },
      lastUpdated: false,
      sidebar: [
        { label: 'Home', link: '/' },
        { label: 'Resort Quickstart', slug: 'quickstart' },
        {
          label: 'Guides',
          items: [
            { slug: 'guides/lifts' },
            { slug: 'guides/runs-and-snow' },
            { slug: 'guides/lodges-school-patrol' },
            { slug: 'guides/passes-and-lessons' },
            { slug: 'guides/hub-panel' },
            { slug: 'guides/economy-panel' },
            { slug: 'guides/troubleshooting' },
          ],
        },
        {
          label: 'Reference',
          items: [
            { slug: 'reference/buildings' },
            { slug: 'reference/lifts' },
            { slug: 'reference/attraction' },
            { slug: 'reference/settings' },
          ],
        },
        { label: 'Asset Pipeline', slug: 'asset-pipeline' },
        { label: 'Timeline', slug: 'timeline' },
        { label: 'Changelog', slug: 'changelog' },
        { label: 'AI Disclosure', slug: 'ai-disclosure' },
        { label: 'Credits and Licenses', slug: 'credits' },
      ],
    }),
  ],
});
