// Click-to-copy for Starlight's heading anchor links (the link icon shown when a heading is hovered).
// Starlight's icon only jumps to the section; this also copies the section's full URL to the clipboard
// and shows "Copied" next to the icon for a moment. The jump still happens (no preventDefault), so the
// address bar shows the same URL. Without clipboard access (an insecure origin, a denied permission)
// nothing else changes: right-click "Copy link address" still works.
// Inlined into every page's <head> by astro.config.mjs; styles: .sl-anchor-link[data-copied] in custom.css.
document.addEventListener('click', (event) => {
  const target = event.target;
  if (!(target instanceof Element)) return;
  const link = target.closest('a.sl-anchor-link');
  if (!(link instanceof HTMLAnchorElement) || !navigator.clipboard) return;
  if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
  navigator.clipboard.writeText(link.href).then(
    () => {
      link.setAttribute('data-copied', '');
      const live = document.getElementById('sr-anchor-copy-status');
      if (live) live.textContent = 'Link copied';
      clearTimeout(Number(link.dataset.copiedTimer));
      link.dataset.copiedTimer = String(
        setTimeout(() => {
          link.removeAttribute('data-copied');
          if (live) live.textContent = '';
        }, 1500),
      );
    },
    () => {},
  );
});

// A polite live region so screen readers hear the copy too.
document.addEventListener('DOMContentLoaded', () => {
  const live = document.createElement('div');
  live.id = 'sr-anchor-copy-status';
  live.className = 'sr-only';
  live.setAttribute('aria-live', 'polite');
  document.body.append(live);
});
