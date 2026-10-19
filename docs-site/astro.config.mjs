// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// Deployed by the Pages workflow, which sets DOCS_SITE and DOCS_BASE from the repository
// (https://<owner>.github.io + /<repo>). Locally both fall back to the root.
// A custom domain later: set DOCS_SITE to it, DOCS_BASE to '/', and add public/CNAME.
const site = process.env.DOCS_SITE || 'http://localhost:4321';
const base = process.env.DOCS_BASE || '/';

export default defineConfig({
  site,
  base,
  integrations: [
    starlight({
      title: 'Modular Ski Resort',
      description:
        'Player guides, reference and the making-of story for Modular Ski Resort, a ski resort mod for Cities: Skylines II.',
      customCss: ['./src/styles/custom.css'],
      lastUpdated: false,
      sidebar: [
        { label: 'Home', link: '/' },
        {
          label: 'Getting started',
          items: [{ label: 'Your first resort in 10 minutes', slug: 'getting-started/first-resort' }],
        },
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
        { label: 'The making of', slug: 'making-of' },
        { label: 'Changelog', slug: 'changelog' },
        { label: 'Credits and licences', slug: 'credits' },
      ],
    }),
  ],
});
