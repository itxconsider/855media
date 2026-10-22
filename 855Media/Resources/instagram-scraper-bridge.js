(() => {
  if (!/(^|\.)instagram\.com$/i.test(location.hostname) || window.__ig855) return;
  const state = { items: new Map(), running: false, enabled: false, timer: null,
    key: '', reason: '', idle: 0, progress: Date.now(), pending: null };
  function post(url) {
    try {
      const u = new URL(url, location.href);
      if (!/(^|\.)instagram\.com$/i.test(u.hostname) || u.protocol !== 'https:') return null;
      const m = u.pathname.match(/^\/(?:[\w.]+\/)?(p|reels?|tv)\/([\w-]+)\/?$/);
      return m ? { code: m[2], url: `https://www.instagram.com/${m[1] === 'p' ? 'p' : 'reel'}/${m[2]}/` } : null;
    } catch { return null; }
  }
  function key() { return location.pathname.replace(/\/$/, ''); }
  function login() { return /^\/(accounts\/login|challenge|checkpoint)(\/|$)/.test(location.pathname)
    || !!document.querySelector('input[name="password"]'); }
  function blocked() { return /try again later|please wait a few minutes|we restrict certain activity/i.test(document.querySelector('[role="dialog"]')?.innerText || ''); }
  function notify() { window.chrome?.webview?.postMessage?.(JSON.stringify({type:'IG_COLLECTION_STATE',
    count: state.items.size, running: state.running, reason: state.reason, pageUrl: location.href})); }
  function stop(reason = 'stopped') { state.running = false; state.reason = reason;
    clearInterval(state.timer); state.timer = null; notify(); }
  function collect() {
    if (state.key && state.key !== key()) { state.items.clear(); state.enabled = false; stop('page_changed'); }
    state.key = key();
    if (!state.enabled || login() || blocked()) return;
    const before = state.items.size;
    const current = post(location.href);
    if (current) state.items.set(current.code, current);
    else {
      const scope = document.querySelector('main,[role="main"]');
      scope?.querySelectorAll('a[href]').forEach(link => {
        if (link.closest('nav,[role="navigation"],aside')) return;
        const item = post(link.href);
        if (item) state.items.set(item.code, item);
      });
    }
    if (state.items.size !== before) { state.progress = Date.now(); state.idle = 0; notify(); }
  }
  function tick() {
    if (login()) { stop('waiting_for_login'); return; }
    if (blocked()) { stop('rate_limited'); return; }
    collect();
    if (!state.running) return;
    const root = document.scrollingElement || document.documentElement;
    const loading = !!document.querySelector('main,[role="main"]')?.querySelector('[role="progressbar"],[aria-busy="true"]');
    const bottom = root.scrollTop + root.clientHeight >= root.scrollHeight - 20;
    state.idle = bottom && !loading ? state.idle + 1 : 0;
    if (state.idle >= 10) { stop('loaded_bottom'); return; }
    const before = root.scrollTop;
    root.scrollTop += Math.max(500, root.clientHeight * .8);
    if (root.scrollTop !== before) state.progress = Date.now();
    if (Date.now() - state.progress > 60000) stop('stalled');
  }
  window.__ig855 = {
    start() { if (state.running) return;
      if (state.key && state.key !== key()) state.items.clear();
      state.key = key(); state.enabled = true; state.running = true;
      state.reason = ''; state.idle = 0; state.progress = Date.now();
      collect(); tick(); if (state.running) state.timer = setInterval(tick, 2500); notify(); },
    stop,
    clear() { stop('cleared'); state.items.clear(); state.enabled = false; notify(); },
    payload() { state.enabled = true; collect(); return {pageUrl:location.href, items:[...state.items.values()]}; },
    collect
  };
  new MutationObserver(() => {
    if (!state.enabled || state.pending) return;
    state.pending = setTimeout(() => { state.pending = null; collect(); }, 300);
  }).observe(document.documentElement, {childList:true,subtree:true});
})();
