(() => {
  const defaultState = {
    queue: [],
    running: false,
    completed: 0,
    failed: 0,
    active: 0,
    concurrency: 2,
    skipExisting: true,
    folder: 'facebook-bulk',
    fileTemplate: '{index}_{caption}',
    downloadsByUrl: {},
  };

  async function loadState() {
    const { fbdState } = await chrome.storage.local.get('fbdState');
    return { ...defaultState, ...(fbdState || {}) };
  }

  async function saveState(state) {
    await chrome.storage.local.set({ fbdState: state });
  }

  function sanitize(input) {
    const s = (input || '').replace(/[\\/:*?"<>|]/g, ' ').replace(/\s+/g, ' ').trim();
    return s.slice(0, 120) || 'untitled';
  }

  function extFromUrl(url) {
    try {
      const u = new URL(url);
      const m = u.pathname.match(/\.([a-zA-Z0-9]{2,5})$/);
      const ext = m ? m[1].toLowerCase() : 'jpg';
      return ['jpg', 'jpeg', 'png', 'webp'].includes(ext) ? ext : 'jpg';
    } catch {
      return 'jpg';
    }
  }

  function isDownloadableImageUrl(url) {
    try {
      const u = new URL(url);
      const hostOk = /(?:scontent|fbcdn\.net|lookaside\.fbsbx\.com)/i.test(u.hostname);
      if (!hostOk) return false;

      const path = u.pathname.toLowerCase();
      const explicitImageExt = /\.(?:jpg|jpeg|png|webp)$/.test(path);
      const facebookPhotoPath = /\/v\/t\d+(?:\.\d+)?-\d+\//i.test(path) || /\/v\/t\d+\//i.test(path);
      const hasPhotoQuery = u.searchParams.has('_nc_cat') || u.searchParams.has('_nc_ohc') || u.searchParams.has('oh');

      return explicitImageExt || (facebookPhotoPath && hasPhotoQuery);
    } catch {
      return false;
    }
  }

  function buildFileName(item, index, template) {
    const map = {
      '{index}': String(index + 1).padStart(4, '0'),
      '{caption}': sanitize(item.caption || ''),
      '{date}': sanitize(item.date || ''),
      '{photo_id}': sanitize(item.id || ''),
      '{username}': sanitize(item.username || ''),
    };

    let name = template || '{index}_{caption}';
    for (const [k, v] of Object.entries(map)) name = name.replaceAll(k, v);
    name = sanitize(name);
    return `${name}.${extFromUrl(item.url)}`;
  }

  async function pumpQueue() {
    const state = await loadState();
    if (!state.running) return;

    while (state.active < state.concurrency) {
      const nextIndex = state.queue.findIndex(i => i.status === 'pending');
      if (nextIndex < 0) break;

      const item = state.queue[nextIndex];
      item.status = 'downloading';
      state.active++;
      await saveState(state);

      const fileName = buildFileName(item, nextIndex, state.fileTemplate);
      const path = `${sanitize(state.folder)}/${fileName}`;

      if (state.skipExisting && state.downloadsByUrl[item.url]) {
        item.status = 'completed';
        state.completed++;
        state.active--;
        await saveState(state);
        continue;
      }

      chrome.downloads.download({ url: item.url, filename: path, saveAs: false }, async (downloadId) => {
        const current = await loadState();
        const idx = current.queue.findIndex(q => q.url === item.url && q.status === 'downloading');
        if (idx >= 0) {
          if (chrome.runtime.lastError || !downloadId) {
            current.queue[idx].status = 'failed';
            current.queue[idx].error = chrome.runtime.lastError?.message || 'download failed';
            current.failed++;
          } else {
            current.queue[idx].status = 'completed';
            current.completed++;
            current.downloadsByUrl[current.queue[idx].url] = true;
          }
          current.active = Math.max(0, current.active - 1);
          await saveState(current);
          pumpQueue();
        }
      });
    }

    await saveState(state);
  }

  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    (async () => {
      const state = await loadState();

      if (message?.type === 'FBD_SET_CONFIG') {
        state.concurrency = Math.max(1, Math.min(8, Number(message.concurrency || 2)));
        state.skipExisting = !!message.skipExisting;
        state.folder = message.folder || 'facebook-bulk';
        state.fileTemplate = message.fileTemplate || '{index}_{caption}';
        await saveState(state);
        sendResponse({ ok: true, state });
        return;
      }

      if (message?.type === 'FBD_ENQUEUE') {
        const incoming = Array.isArray(message.items) ? message.items : [];
        const dedup = new Set(state.queue.map(i => i.url));
        let added = 0;
        let rejected = 0;
        for (const it of incoming) {
          if (!it?.url || dedup.has(it.url)) continue;
          if (!isDownloadableImageUrl(it.url)) {
            rejected++;
            continue;
          }
          dedup.add(it.url);
          state.queue.push({ ...it, status: 'pending' });
          added++;
        }
        await saveState(state);
        sendResponse({ ok: true, added, rejected, state });
        return;
      }

      if (message?.type === 'FBD_START') {
        state.running = true;
        await saveState(state);
        sendResponse({ ok: true });
        pumpQueue();
        return;
      }

      if (message?.type === 'FBD_STOP') {
        state.running = false;
        await saveState(state);
        sendResponse({ ok: true });
        return;
      }

      if (message?.type === 'FBD_CLEAR') {
        const reset = { ...defaultState, downloadsByUrl: state.downloadsByUrl };
        await saveState(reset);
        sendResponse({ ok: true, state: reset });
        return;
      }

      if (message?.type === 'FBD_STATE') {
        sendResponse({ ok: true, state });
        return;
      }

      sendResponse({ ok: false });
    })();

    return true;
  });
})();
