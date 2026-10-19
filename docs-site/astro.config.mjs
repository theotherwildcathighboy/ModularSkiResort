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
        'Guides, reference, asset pipeline and timeline for Modular Ski Resort, a ski resort mod for Cities: Skylines II.',
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
