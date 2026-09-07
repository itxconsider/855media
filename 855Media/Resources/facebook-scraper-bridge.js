(() => {
  if (window.__fbdScraperInitialized) {
    if (window.__fbdCollect) window.__fbdCollect();
    return;
  }
  window.__fbdScraperInitialized = true;

  const state = {
    running: false,
    itemsByUrl: new Map(),
    intervalId: null,
    observer: null,
    delayMs: 1200,
    maxItems: 1000,
  };

  const style = document.createElement('style');
  style.innerHTML = `
    @keyframes fbd-pulse {
      0% { filter: brightness(1); }
      50% { filter: brightness(1.3); }
      100% { filter: brightness(1); }
    }
    .fbd-scraped {
      animation: fbd-pulse 0.6s ease-out;
      outline: 4px solid #00C853 !important;
      outline-offset: -4px !important;
      border-radius: 4px !important;
      transition: all 0.3s ease;
    }
  `;
  document.head.appendChild(style);

  function sanitizeTitle(text, id) {
    if (!text) return `Facebook_Photo_${id || Date.now()}`;
    let cleaned = text.trim();

    // Replace invalid filename characters
    cleaned = cleaned.replace(/[\/\\:*?"<>|]/g, ' ').replace(/\s+/g, ' ').trim();

    // If it's too short or generic, fallback
    if (!cleaned || cleaned.length < 3 || /^facebook$/i.test(cleaned)) {
      return `Facebook_Photo_${id || Date.now()}`;
    }

    return cleaned.slice(0, 100);
  }

  function extractId(url) {
    const m = String(url).match(/(?<id>\d{10,})/);
    return m?.groups?.id || '';
  }

  function getBestImageUrl(img) {
    // 1. Try srcset for highest resolution signed CDN URL
    const srcset = img.getAttribute('srcset') || img.srcset;
    if (srcset && typeof srcset === 'string') {
      const candidates = srcset.split(',').map(entry => {
        const parts = entry.trim().split(/\s+/);
        const url = parts[0];
        let width = 0;
        if (parts[1] && parts[1].endsWith('w')) {
          width = parseInt(parts[1], 10) || 0;
        }
        return { url, width };
      }).filter(c => c.url && !c.url.includes('/rsrc.php/') && !c.url.includes('/emoji.php'));

      if (candidates.length > 0) {
        candidates.sort((a, b) => b.width - a.width);
        if (candidates[0].url) {
          return candidates[0].url;
        }
      }
    }

    // 2. Fallback to currentSrc, data-src, or src
    return img.currentSrc || img.getAttribute('data-src') || img.src || '';
  }

  function isUserPhoto(img, url) {
    if (!url || typeof url !== 'string') return false;

    try {
      const u = new URL(url);

      // Exclude Facebook static assets, emojis, stickers, reaction sprites, icons
      const lowerPath = u.pathname.toLowerCase();
      const lowerHost = u.hostname.toLowerCase();

      if (lowerHost.includes('static.') ||
          lowerPath.includes('/rsrc.php/') ||
          lowerPath.includes('/rsrc/') ||
          lowerPath.includes('/emoji.php') ||
          lowerPath.includes('/assets/') ||
          lowerPath.includes('/reactions/') ||
          lowerPath.includes('/sprites/')) {
        return false;
      }

      // Check CDN host
      const hostOk = /(?:scontent|fbcdn\.net|lookaside\.fbsbx\.com)/i.test(lowerHost);
      if (!hostOk) return false;

      // Check element dimensions (exclude icons, emojis, reaction buttons)
      const rect = img.getBoundingClientRect ? img.getBoundingClientRect() : null;
      const width = img.naturalWidth || img.width || (rect ? rect.width : 0);
      const height = img.naturalHeight || img.height || (rect ? rect.height : 0);

      // Icons and emojis are typically 16px to 48px; album photos in grid are >= 120px
      if (width > 0 && height > 0 && (width < 120 || height < 120)) {
        return false;
      }

      // Exclude Reels and Videos explicitly
      if (img.closest('a[href*="/reel/"], a[href*="/reels/"], a[href*="/watch/"], a[href*="/video/"]')) {
        return false;
      }

      // Positive indicators for actual user photos:
      // 1. Inside a Facebook photo link (/photo/, photo.php, fbid=)
      const photoLink = img.closest('a[href*="/photo/"], a[href*="photo.php"], a[href*="fbid="], a[href*="/photos/"]');
      if (photoLink) return true;

      // 2. Lightbox media image
      if (img.getAttribute('data-visualcompletion') === 'media-vc-image') return true;

      // 3. Path matches Facebook photo pattern and has reasonable size
      const isPhotoPath = /\/v\/t\d+(?:\.\d+)?-\d+\//i.test(lowerPath) || /\/v\/t\d+\//i.test(lowerPath);
      if (isPhotoPath && width >= 140 && height >= 140) return true;

      return false;
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
      if (t.length > 5) cands.push(t);
    }
    return (cands.find(x => x && !/^facebook$/i.test(x)) || '').slice(0, 200);
  }

  function toDateFromText(text) {
    if (!text) return '';
    const m = text.match(/\b(\d{4})-(\d{2})-(\d{2})\b/);
    return m ? m[0] : '';
  }

  function updateFloatingBadge(count, running) {
    try {
      let badge = document.getElementById('__855media_badge');
      if (!badge) {
        badge = document.createElement('div');
        badge.id = '__855media_badge';
        badge.style.cssText = 'position:fixed;bottom:24px;right:24px;background:#1877F2;color:#ffffff;padding:10px 18px;border-radius:24px;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,sans-serif;font-size:13px;font-weight:700;z-index:2147483647;box-shadow:0 6px 20px rgba(0,0,0,0.35);display:flex;align-items:center;gap:10px;pointer-events:none;transition:all 0.25s ease;border:2px solid rgba(255,255,255,0.25);';
        document.body?.appendChild(badge);
      }
      badge.style.display = 'flex';
      badge.innerHTML = running
        ? `<span style="display:inline-block;animation:spin 1s linear infinite;">🔄</span> Auto-scrolling... <b>${count}</b> photos`
        : `📸 855Media: <b>${count}</b> photos detected`;
    } catch {}
  }

  function collect() {
    let newItemsFound = 0;

    // 1. First priority: Target photo links in albums & grids
    const photoLinks = document.querySelectorAll('a[href*="/photo/"], a[href*="photo.php"], a[href*="fbid="], a[href*="/photos/"]');
    photoLinks.forEach(link => {
      // Exclude reels and videos explicitly
      const href = link.href ? link.href.toLowerCase() : '';
      if (href.includes('/reel/') || href.includes('/reels/') || href.includes('/watch/') || href.includes('/video/')) {
        return;
      }

      const img = link.querySelector('img');
      if (!img) return;

      const rawUrl = getBestImageUrl(img);
      if (!rawUrl || !isUserPhoto(img, rawUrl)) return;

      const photoId = extractId(link.href) || extractId(rawUrl);
      const rawCaption = getCaption(img);
      const title = sanitizeTitle(rawCaption, photoId);
      const date = toDateFromText(rawCaption) || new Date().toISOString().slice(0, 10);

      if (!state.itemsByUrl.has(rawUrl)) {
        state.itemsByUrl.set(rawUrl, {
          url: rawUrl,
          caption: title,
          id: photoId || GuidFallback(),
          date,
          username: location.pathname.split('/').filter(Boolean)[0] || 'facebook',
        });
        newItemsFound++;
      }
      
      if (!img.classList.contains('fbd-scraped')) {
        img.classList.add('fbd-scraped');
      }
    });

    // 2. Second priority: Full media viewer images & general photos
    document.querySelectorAll('img').forEach(img => {
      const rawUrl = getBestImageUrl(img);
      if (!rawUrl || !isUserPhoto(img, rawUrl)) return;

      const photoId = extractId(rawUrl);
      const rawCaption = getCaption(img);
      const title = sanitizeTitle(rawCaption, photoId);
      const date = toDateFromText(rawCaption) || new Date().toISOString().slice(0, 10);

      if (!state.itemsByUrl.has(rawUrl)) {
        state.itemsByUrl.set(rawUrl, {
          url: rawUrl,
          caption: title,
          id: photoId || GuidFallback(),
          date,
          username: location.pathname.split('/').filter(Boolean)[0] || 'facebook',
        });
        newItemsFound++;
      }

      if (!img.classList.contains('fbd-scraped')) {
        img.classList.add('fbd-scraped');
      }
    });

    updateFloatingBadge(state.itemsByUrl.size, state.running);

    if (newItemsFound > 0) {
      notifyCount();
    }
  }

  function GuidFallback() {
    return Math.random().toString(36).substring(2, 12);
  }

  function notifyCount() {
    updateFloatingBadge(state.itemsByUrl.size, state.running);
    if (window.chrome?.webview?.postMessage) {
      try {
        window.chrome.webview.postMessage(JSON.stringify({
          type: 'MEDIA_DETECTED',
          count: state.itemsByUrl.size,
          pageUrl: location.href,
          running: state.running,
        }));
      } catch {}
    }
  }

  function sendPayload() {
    collect();
    const payload = {
      type: 'MEDIA_PAYLOAD',
      pageUrl: location.href,
      items: Array.from(state.itemsByUrl.values()),
    };
    if (window.chrome?.webview?.postMessage) {
      try {
        window.chrome.webview.postMessage(JSON.stringify(payload));
      } catch {}
    }
    return JSON.stringify(payload);
  }

  function scrollStep() {
    // 1. Scroll window and document scrolling elements
    window.scrollBy({ top: 1200, behavior: 'smooth' });
    if (document.scrollingElement) document.scrollingElement.scrollTop += 1200;
    if (document.body) document.body.scrollTop += 1200;
    if (document.documentElement) document.documentElement.scrollTop += 1200;

    // 2. Find and scroll any scrollable parent divs/main in modern Facebook layout
    try {
      const scrollableElements = document.querySelectorAll('div, main, section, [role="main"]');
      for (let i = 0; i < scrollableElements.length; i++) {
        const el = scrollableElements[i];
        if (el.scrollHeight > el.clientHeight + 40) {
          const overflowY = window.getComputedStyle(el).overflowY;
          if (overflowY === 'auto' || overflowY === 'scroll') {
            el.scrollTop += 1200;
          }
        }
      }
    } catch {}

    // 3. Scroll the last visible photo into view to trigger Facebook IntersectionObserver
    try {
      const imgs = Array.from(document.querySelectorAll('a[href*="/photo/"] img, img[data-visualcompletion="media-vc-image"]'));
      if (imgs.length > 0) {
        imgs[imgs.length - 1].scrollIntoView({ behavior: 'smooth', block: 'end' });
      }
    } catch {}
  }

  function startScraping(maxItems = 1000, delayMs = 1200) {
    if (state.running) return;
    state.running = true;
    state.maxItems = maxItems;
    state.delayMs = Math.max(400, delayMs);

    collect();
    scrollStep();

    state.intervalId = setInterval(() => {
      collect();
      if (state.maxItems > 0 && state.itemsByUrl.size >= state.maxItems) {
        stopScraping();
        return;
      }
      scrollStep();
    }, state.delayMs);

    notifyCount();
  }

  function stopScraping() {
    state.running = false;
    if (state.intervalId) {
      clearInterval(state.intervalId);
      state.intervalId = null;
    }
    notifyCount();
  }

  function clearItems() {
    state.itemsByUrl.clear();
    notifyCount();
  }

  // Setup DOM mutation observer
  try {
    state.observer = new MutationObserver(() => {
      collect();
    });
    state.observer.observe(document.body || document.documentElement, {
      childList: true,
      subtree: true,
    });
  } catch {}

  // Expose global controller functions
  window.__fbdStartScraping = startScraping;
  window.__fbdStopScraping = stopScraping;
  window.__fbdCollect = collect;
  window.__fbdGetPayload = sendPayload;
  window.__fbdClear = clearItems;
  window.__fbdGetCount = () => { collect(); return state.itemsByUrl.size; };

  // Listen for messages from C# CoreWebView2
  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener('message', event => {
      const data = event.data;
      if (!data) return;

      let msg = data;
      if (typeof data === 'string') {
        try { msg = JSON.parse(data); } catch { return; }
      }

      if (msg.action === 'START_SCRAPING') {
        startScraping(msg.maxItems || 1000, msg.delayMs || 1200);
      } else if (msg.action === 'STOP_SCRAPING') {
        stopScraping();
      } else if (msg.action === 'GET_PAYLOAD') {
        sendPayload();
      } else if (msg.action === 'CLEAR') {
        clearItems();
      }
    });
  }

  // Removed initial passive scan to prevent auto-collect on page load
})();
