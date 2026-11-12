// Links that leave the docs site open in a new tab. Inlined into every page by astro.config.mjs.
// A client script, not a Markdown plugin, so it also covers MDX components and Starlight's own links.
(() => {
  const mark = () => {
    for (const a of document.querySelectorAll('a[href]')) {
      let url;
      try {
        url = new URL(a.getAttribute('href'), location.href);
      } catch {
        continue;
      }
      if ((url.protocol === 'http:' || url.protocol === 'https:') && url.host !== location.host) {
        a.target = '_blank';
        a.rel = 'noopener noreferrer';
      }
    }
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mark);
  else mark();
})();
