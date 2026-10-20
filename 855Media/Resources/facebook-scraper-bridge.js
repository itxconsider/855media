(() => {
  if (window.top !== window.self || window.__fbdScraperInitialized) return;
  window.__fbdScraperInitialized = true;
  const photoSelector = 'a[href*="photo.php"],a[href*="/photo/"],a[href*="fbid="]';
  const state = { running: false, enabled: false, items: new Map(), timer: null, pending: null,
    delayMs: 1400, maxItems: 0, idleRounds: 0, lastProgress: Date.now(), reason: '', pageKey: '' };
  function pageKey() {
    const u = new URL(location.href);
    return u.pathname === '/profile.php' ? u.pathname + '?id=' + u.searchParams.get('id') : u.pathname.split('/').slice(0, 2).join('/');
  }
  function scope() {
    const main = document.querySelector('[role="main"]') || document;
    const heading = Array.from(main.querySelectorAll('h1,h2,h3,[role="heading"]')).find(h => /^photos$/i.test(h.textContent.trim()));
    if (heading) {
      for (let p = heading.parentElement; p && p !== main; p = p.parentElement)
        if (p.querySelector(photoSelector)) return p;
    }
    return main;
  }
  function bestUrl(img) {
    const candidates = (img.getAttribute('srcset') || '').split(',').map(value => {
      const [url, size] = value.trim().split(/\s+/);
      return { url, size: parseFloat(size) || 0 };
    }).filter(x => x.url);
    candidates.sort((a, b) => b.size - a.size);
    return candidates[0]?.url || img.currentSrc || img.getAttribute('data-src') || img.getAttribute('src') || img.getAttribute('href') || img.getAttribute('xlink:href') || '';
  }
  function imageUrlAllowed(value) {
    try {
      const u = new URL(value);
      return u.protocol === 'https:' && (u.hostname.endsWith('.fbcdn.net') || u.hostname === 'fbcdn.net'
        || u.hostname === 'lookaside.fbsbx.com' || u.hostname.endsWith('.fbsbx.com'))
        && !/\/(?:rsrc|emoji|assets|reactions|sprites)/i.test(u.pathname);
    } catch { return false; }
  }
  function photoId(link, url) {
    try {
      const u = new URL(link.href);
      const id = u.searchParams.get('fbid') || u.pathname.match(/\/photo\/(\d+)/)?.[1];
      if (id) return id;
    } catch {}
    return new URL(url).pathname;
  }
  function notify() {
    const count = state.items.size;
    let badge = document.getElementById('__855media_badge');
    if (!badge && document.body) {
      badge = document.createElement('div');
      badge.id = '__855media_badge';
      badge.style.cssText = 'position:fixed;bottom:24px;right:24px;background:#1877f2;color:white;padding:10px 18px;border-radius:20px;font:13px sans-serif;z-index:2147483647;pointer-events:none';
      document.body.appendChild(badge);
    }
    const text = state.running ? `855Media: collecting ${count} photos` : `855Media: ${count} photos collected`;
    if (badge && badge.textContent !== text) badge.textContent = text;
    window.chrome?.webview?.postMessage?.(JSON.stringify({ type: 'MEDIA_DETECTED', count,
      pageUrl: location.href, running: state.running, reason: state.reason }));
  }
  function loginRequired() {
    return /^\/(login|checkpoint)(\/|\.php|$)/i.test(location.pathname)
      || !!document.querySelector('input[name="email"],input[name="pass"]');
  }
  function collect() {
    const key = pageKey();
    if (state.pageKey && key !== state.pageKey) {
      state.items.clear();
      stop('page_changed');
      state.enabled = false;
    }
    state.pageKey = key;
    if (!state.enabled || loginRequired()) return;
    const before = state.items.size;
    scope().querySelectorAll(photoSelector).forEach(link => {
      if (link.closest('[role="dialog"],[role="navigation"]') || /\/(reels?|watch|videos?)\//i.test(link.href)) return;
      const img = link.querySelector('img,image');
      if (!img) return;
      const url = bestUrl(img);
      if (!imageUrlAllowed(url)) return;
      const rect = img.getBoundingClientRect();
      if (rect.width > 0 && rect.height > 0 && (rect.width < 100 || rect.height < 100)) return;
      const id = photoId(link, url);
      const caption = (img.getAttribute('alt') || link.getAttribute('aria-label') || `Facebook photo ${id}`).trim();
      state.items.set(id, { id, url, photoPageUrl: link.href, caption, username: location.pathname.split('/').filter(Boolean)[0] || 'Facebook' });
    });
    if (state.items.size !== before) { state.lastProgress = Date.now(); state.idleRounds = 0; notify(); }
  }
  function scrollTarget() {
    const root = document.scrollingElement || document.documentElement;
    const main = document.querySelector('[role="main"]');
    if (main) {
      for (const element of [main, ...main.querySelectorAll('div,section')]) {
        if (element.scrollHeight > element.clientHeight + 100 && /^(auto|scroll)$/.test(getComputedStyle(element).overflowY)) return element;
      }
    }
    return root;
  }
  function tick() {
    if (loginRequired()) { stop('waiting_for_login'); return; }
    collect();
    if (!state.running) return;
    if (state.maxItems > 0 && state.items.size >= state.maxItems) { stop('item_limit'); return; }
    const target = scrollTarget();
    const bottom = target.scrollTop + target.clientHeight >= target.scrollHeight - 20;
    const loading = !!scope().querySelector('[role="progressbar"],[aria-busy="true"]');
    state.idleRounds = bottom && !loading ? state.idleRounds + 1 : 0;
    if (state.idleRounds >= 12) { stop('no_more_photos'); return; }
    const before = target.scrollTop;
    target.scrollTop += Math.max(600, Math.floor(target.clientHeight * 0.8));
    if (target.scrollTop !== before) state.lastProgress = Date.now();
    if (Date.now() - state.lastProgress > 60000) stop('stalled');
  }
  function start(maxItems = 0, delayMs = 1400) {
    if (state.running) return;
    state.enabled = true;
    const key = pageKey();
    if (state.pageKey && state.pageKey !== key) state.items.clear();
    state.pageKey = key;
    state.reason = '';
    state.idleRounds = 0;
    state.lastProgress = Date.now();
    state.maxItems = Math.max(0, Number(maxItems) || 0);
    state.delayMs = Math.max(800, Number(delayMs) || 1400);
    state.running = true;
    collect();
    tick();
    if (state.running) state.timer = setInterval(tick, state.delayMs);
    notify();
  }
  function stop(reason = 'stopped') {
    state.running = false;
    state.reason = reason;
    if (state.timer) { clearInterval(state.timer); state.timer = null; }
    notify();
  }
  function payload() {
    state.enabled = true;
    collect();
    return JSON.stringify({ type: 'MEDIA_PAYLOAD', pageUrl: location.href, items: Array.from(state.items.values()) });
  }
  function clear() { stop('cleared'); state.items.clear(); state.enabled = false; notify(); }
  window.__fbdStartScraping = start;
  window.__fbdStopScraping = stop;
  window.__fbdClear = clear;
  window.__fbdCollect = collect;
  // Returning a payload does not also post it: one Import action must create one import.
  window.__fbdGetPayload = payload;
  window.__fbdGetCount = () => state.items.size;
  window.chrome?.webview?.addEventListener?.('message', event => {
    let message = event.data;
    if (typeof message === 'string') { try { message = JSON.parse(message); } catch { return; } }
    if (message?.action === 'START_SCRAPING') start(message.maxItems ?? 0, message.delayMs ?? 1400);
    else if (message?.action === 'STOP_SCRAPING') stop();
    else if (message?.action === 'CLEAR') clear();
    else if (message?.action === 'GET_PAYLOAD') window.chrome.webview.postMessage(payload());
  });
  const observer = new MutationObserver(records => {
    if (!state.enabled || state.pending || records.every(r => r.target.id === '__855media_badge' || r.target.parentElement?.id === '__855media_badge')) return;
    state.pending = setTimeout(() => { state.pending = null; collect(); }, 250);
  });
  const observe = () => observer.observe(document.documentElement, { childList: true, subtree: true });
  if (document.documentElement) observe();
  else document.addEventListener('DOMContentLoaded', observe, { once: true });
})();
