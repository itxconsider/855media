(() => {
  const state = {
    running: false,
    urls: new Set(),
    captionsByUrl: new Map(),
    pageUrl: location.href,
    intervalId: null,
    observer: null,
  };

  function isLikelyPhotoUrl(url) {
    if (!url || typeof url !== 'string') return false;
    const value = url.trim();
    if (!/^https?:\/\//i.test(value)) return false;
    return /(?:scontent|fbcdn\.net|lookaside\.fbsbx\.com)/i.test(value)
      && (/\.(?:jpe?g|png)(?:\?|$)/i.test(value)
        || /(?:_nc_cat=|_nc_ohc=|stp=|oh=)/i.test(value));
  }

  function normalizeUrl(url) {
    try {
      const u = new URL(url);
      // Strip known resize/transform params to prefer higher-quality source variants.
      u.searchParams.delete('stp');
      u.searchParams.delete('w');
      u.searchParams.delete('h');
      u.searchParams.delete('width');
      u.searchParams.delete('height');
      u.searchParams.delete('tp');
      u.searchParams.delete('cb');
      return u.toString();
    } catch {
      return url;
    }
  }

  function rememberUrl(url, caption) {
    const normalized = normalizeUrl(url);
    state.urls.add(normalized);
    if (caption && !state.captionsByUrl.has(normalized)) {
      state.captionsByUrl.set(normalized, caption.trim());
    }
  }

  function collectFromString(raw, caption) {
    if (!raw) return;
    const parts = String(raw).split(',').map(s => s.trim());
    for (const part of parts) {
      const candidate = part.split(' ')[0];
      if (isLikelyPhotoUrl(candidate)) {
        rememberUrl(candidate, caption);
      }
    }
  }

  function extractCaptionFromElement(el) {
    if (!el) return '';
    const candidates = [];
    const aria = el.getAttribute?.('aria-label');
    if (aria) candidates.push(aria);
    const alt = el.getAttribute?.('alt');
    if (alt) candidates.push(alt);

    const textContainers = [
      el.closest?.('[role="article"]'),
      el.closest?.('[data-pagelet]'),
      el.parentElement,
    ].filter(Boolean);

    for (const c of textContainers) {
      const text = c.innerText || '';
      if (text) {
        const cleaned = text.replace(/\s+/g, ' ').trim();
        if (cleaned.length >= 8) candidates.push(cleaned);
      }
    }

    const best = candidates
      .map(t => t.trim())
      .filter(t =>
        t
        && !/^facebook$/i.test(t)
        && !/^image may contain/i.test(t)
        && !/^see more$/i.test(t)
      )
      .sort((a, b) => a.length - b.length)[0];

    return best || '';
  }

  function collect() {
    state.pageUrl = location.href;

    document.querySelectorAll('img').forEach(img => {
      const caption = extractCaptionFromElement(img);
      collectFromString(img.currentSrc, caption);
      collectFromString(img.src, caption);
      collectFromString(img.getAttribute('data-src'), caption);
      collectFromString(img.getAttribute('srcset'), caption);
    });

    document.querySelectorAll('a[href]').forEach(a => {
      const href = a.getAttribute('href');
      if (href && isLikelyPhotoUrl(href)) {
        rememberUrl(href, extractCaptionFromElement(a));
      }
    });

    const html = document.documentElement?.innerHTML || '';
    const escapedUrlRegex = new RegExp('https:\\\\/\\\\/[^"\'\\\\s<>]+', 'g');
    const matches = html.match(escapedUrlRegex) || [];
    for (const m of matches) {
      const decoded = m.replace(/\\\//g, '/');
      if (isLikelyPhotoUrl(decoded)) {
        rememberUrl(decoded, '');
      }
    }
  }

  function smoothScrollStep() {
    const viewport = Math.max(window.innerHeight, 800);
    window.scrollBy({ top: Math.floor(viewport * 0.85), behavior: 'smooth' });
  }

  function start() {
    if (state.running) return;
    state.running = true;
    collect();

    state.intervalId = setInterval(() => {
      collect();
      smoothScrollStep();
    }, 1400);

    state.observer = new MutationObserver(() => collect());
    state.observer.observe(document.documentElement, { childList: true, subtree: true });
  }

  function stop() {
    state.running = false;
    if (state.intervalId) {
      clearInterval(state.intervalId);
      state.intervalId = null;
    }
    if (state.observer) {
      state.observer.disconnect();
      state.observer = null;
    }
  }

  function clear() {
    state.urls.clear();
    state.captionsByUrl.clear();
  }

  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    try {
      switch (message?.type) {
        case 'FPE_START':
          start();
          sendResponse({ ok: true });
          return;
        case 'FPE_STOP':
          stop();
          sendResponse({ ok: true });
          return;
        case 'FPE_CLEAR':
          clear();
          sendResponse({ ok: true });
          return;
        case 'FPE_GET_STATE':
          collect();
          sendResponse({
            running: state.running,
            pageUrl: state.pageUrl,
            urls: Array.from(state.urls),
            items: Array.from(state.urls).map(url => ({
              url,
              caption: state.captionsByUrl.get(url) || '',
            })),
          });
          return;
        default:
          sendResponse({ ok: false });
      }
    } catch (err) {
      sendResponse({ ok: false, error: String(err) });
    }
  });
})();
