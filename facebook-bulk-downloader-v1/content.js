(() => {
  const state = {
    running: false,
    itemsByUrl: new Map(),
    intervalId: null,
    pageUrl: location.href,
    maxItems: 0,
    maxDays: 0,
    delayMs: 1500,
  };

  function normalize(url) {
    try {
      const u = new URL(url);
      u.searchParams.delete('stp');
      u.searchParams.delete('w');
      u.searchParams.delete('h');
      u.searchParams.delete('width');
      u.searchParams.delete('height');
      u.searchParams.delete('tp');
      return u.toString();
    } catch {
      return url;
    }
  }

  function isImg(url) {
    if (!url) return false;
    try {
      const u = new URL(url);
      const hostOk = /(?:scontent|fbcdn\.net|lookaside\.fbsbx\.com)/i.test(u.hostname);
      if (!hostOk) return false;
      const path = u.pathname.toLowerCase();
      const explicitImageExt = /\.(?:jpg|jpeg|png|webp)(?:$)/i.test(path);
      const facebookPhotoPath = /\/v\/t\d+(?:\.\d+)?-\d+\//i.test(path) || /\/v\/t\d+\//i.test(path);
      const hasPhotoQuery = u.searchParams.has('_nc_cat') || u.searchParams.has('_nc_ohc');
      return explicitImageExt || (facebookPhotoPath && hasPhotoQuery);
    } catch {
      return false;
    }
  }

  function getCaption(el) {
    const cands = [];
    const alt = el?.getAttribute?.('alt');
    if (alt) cands.push(alt);
    const aria = el?.getAttribute?.('aria-label');
    if (aria) cands.push(aria);
    const block = el?.closest?.('[role="article"]') || el?.closest?.('[data-pagelet]') || el?.parentElement;
    if (block?.innerText) {
      const t = block.innerText.replace(/\s+/g, ' ').trim();
      if (t.length > 8) cands.push(t);
    }
    return (cands.find(x => x && !/^facebook$/i.test(x)) || '').slice(0, 220);
  }

  function extractId(url) {
    const m = String(url).match(/(?<id>\d{10,})/);
    return m?.groups?.id || '';
  }

  function toDateFromText(text) {
    if (!text) return '';
    const m = text.match(/\b(\d{4})-(\d{2})-(\d{2})\b/);
    return m ? m[0] : '';
  }

  function shouldStopCollecting() {
    if (state.maxItems > 0 && state.itemsByUrl.size >= state.maxItems) return true;
    return false;
  }

  function collect() {
    state.pageUrl = location.href;
    document.querySelectorAll('img').forEach(img => {
      const caption = getCaption(img);
      const date = toDateFromText(caption) || new Date().toISOString().slice(0, 10);
      const sources = [img.currentSrc, img.src, img.getAttribute('data-src'), img.getAttribute('srcset')].filter(Boolean);
      for (const srcAny of sources) {
        const first = String(srcAny).split(',')[0].trim().split(' ')[0];
        if (!isImg(first)) continue;
        const url = normalize(first);
        if (!state.itemsByUrl.has(url)) {
          state.itemsByUrl.set(url, {
            url,
            caption,
            id: extractId(url),
            date,
            username: location.pathname.split('/').filter(Boolean)[0] || 'facebook',
          });
        }
      }
    });
  }

  function scrollStep() {
    window.scrollBy({ top: Math.floor(Math.max(window.innerHeight, 900) * 0.9), behavior: 'smooth' });
  }

  function start(config = {}) {
    if (state.running) return;
    state.maxItems = Number(config.maxItems || 0);
    state.maxDays = Number(config.maxDays || 0);
    state.delayMs = Math.max(200, Number(config.delayMs || 1500));

    state.running = true;
    collect();
    state.intervalId = setInterval(() => {
      collect();
      if (shouldStopCollecting()) {
        stop();
        return;
      }
      scrollStep();
    }, state.delayMs);
  }

  function stop() {
    state.running = false;
    if (state.intervalId) clearInterval(state.intervalId);
    state.intervalId = null;
  }

  function clear() {
    state.itemsByUrl.clear();
  }

  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (message?.type === 'FBD_COLLECT_START') {
      start(message.config || {});
      sendResponse({ ok: true });
      return;
    }
    if (message?.type === 'FBD_COLLECT_STOP') {
      stop();
      sendResponse({ ok: true });
      return;
    }
    if (message?.type === 'FBD_COLLECT_CLEAR') {
      clear();
      sendResponse({ ok: true });
      return;
    }
    if (message?.type === 'FBD_COLLECT_STATE') {
      collect();
      sendResponse({ ok: true, running: state.running, pageUrl: state.pageUrl, items: Array.from(state.itemsByUrl.values()) });
      return;
    }
  });
})();
