(() => {
  const $ = id => document.getElementById(id);
  const ui = {
    modeAll: $('modeAll'), modeCount: $('modeCount'), modeDays: $('modeDays'),
    count: $('count'), days: $('days'), delay: $('delay'), concurrency: $('concurrency'),
    folder: $('folder'), template: $('template'), skipExisting: $('skipExisting'),
    collectStart: $('collectStart'), collectStop: $('collectStop'), enqueue: $('enqueue'),
    downloadStart: $('downloadStart'), downloadStop: $('downloadStop'), clearAll: $('clearAll'),
    exportCsv: $('exportCsv'), copyMediaTag: $('copyMediaTag'),
    stats: $('stats'), thumbs: $('thumbs'), msg: $('msg')
  };

  let mode = 'all';
  const msg = t => ui.msg.textContent = t || '-';

  function setMode(next) {
    mode = next;
    ui.modeAll.classList.toggle('active', mode === 'all');
    ui.modeCount.classList.toggle('active', mode === 'count');
    ui.modeDays.classList.toggle('active', mode === 'days');
  }

  async function activeTabId() {
    const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
    return tabs?.[0]?.id;
  }

  async function toContent(type, config) {
    const id = await activeTabId();
    if (!id) throw new Error('No active tab');
    return await chrome.tabs.sendMessage(id, { type, config });
  }

  async function toBg(type, payload = {}) {
    return await chrome.runtime.sendMessage({ type, ...payload });
  }

  async function getStates() {
    const c = await toContent('FBD_COLLECT_STATE');
    const b = await toBg('FBD_STATE');
    return { collect: c, bg: b?.state || {} };
  }

  function renderThumbs(queue) {
    ui.thumbs.innerHTML = '';
    const last = (queue || []).slice(0, 64);
    for (const it of last) {
      const img = document.createElement('img');
      img.className = `thumb ${it.status || 'pending'}`;
      img.src = it.url;
      img.title = `${it.status || 'pending'} | ${it.caption || ''}`;
      ui.thumbs.appendChild(img);
    }
  }

  function csvEscape(v) {
    const s = String(v || '');
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  }

  async function refresh() {
    try {
      const s = await getStates();
      const q = s.bg.queue || [];
      ui.stats.textContent = [
        `Collecting: ${s.collect?.running ? 'yes' : 'no'}`,
        `Collected: ${s.collect?.items?.length || 0}`,
        `Queue: ${q.length}`,
        `Downloading: ${s.bg.running ? 'yes' : 'no'} | Active: ${s.bg.active || 0}`,
        `Completed: ${s.bg.completed || 0} | Failed: ${s.bg.failed || 0}`,
        `Page: ${s.collect?.pageUrl || '-'}`,
      ].join('\n');
      renderThumbs(q);
    } catch (e) {
      msg(`Error: ${e.message || e}`);
    }
  }

  async function applyConfig() {
    await toBg('FBD_SET_CONFIG', {
      concurrency: Number(ui.concurrency.value || 2),
      folder: ui.folder.value,
      fileTemplate: ui.template.value,
      skipExisting: ui.skipExisting.checked,
    });
  }

  function collectConfig() {
    return {
      maxItems: mode === 'count' ? Number(ui.count.value || 0) : 0,
      maxDays: mode === 'days' ? Number(ui.days.value || 0) : 0,
      delayMs: Math.floor(Number(ui.delay.value || 1.5) * 1000),
    };
  }

  ui.modeAll.addEventListener('click', () => setMode('all'));
  ui.modeCount.addEventListener('click', () => setMode('count'));
  ui.modeDays.addEventListener('click', () => setMode('days'));

  ui.collectStart.addEventListener('click', async () => {
    await toContent('FBD_COLLECT_START', collectConfig());
    msg('Collecting started.');
    await refresh();
  });

  ui.collectStop.addEventListener('click', async () => {
    await toContent('FBD_COLLECT_STOP');
    msg('Collecting stopped.');
    await refresh();
  });

  ui.enqueue.addEventListener('click', async () => {
    await applyConfig();
    const c = await toContent('FBD_COLLECT_STATE');
    const items = c?.items || [];
    const r = await toBg('FBD_ENQUEUE', { items });
    msg(`Added ${r?.added || 0}, rejected ${r?.rejected || 0}.`);
    await refresh();
  });

  ui.downloadStart.addEventListener('click', async () => {
    await applyConfig();
    await toBg('FBD_START');
    msg('Download started.');
    await refresh();
  });

  ui.downloadStop.addEventListener('click', async () => {
    await toBg('FBD_STOP');
    msg('Download stopped.');
    await refresh();
  });

  ui.clearAll.addEventListener('click', async () => {
    await toContent('FBD_COLLECT_CLEAR');
    await toBg('FBD_CLEAR');
    msg('Cleared collected and queue.');
    await refresh();
  });

  ui.exportCsv.addEventListener('click', async () => {
    const c = await toContent('FBD_COLLECT_STATE');
    const items = c?.items || [];
    if (!items.length) return msg('No items.');
    const lines = ['url,caption,date,id,username'];
    for (const it of items) lines.push([it.url, it.caption, it.date, it.id, it.username].map(csvEscape).join(','));
    const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    chrome.downloads.download({ url, filename: 'facebook-items.csv', saveAs: true }, () => setTimeout(() => URL.revokeObjectURL(url), 5000));
    msg(`Exported CSV (${items.length}).`);
  });

  ui.copyMediaTag.addEventListener('click', async () => {
    const c = await toContent('FBD_COLLECT_STATE');
    const items = c?.items || [];
    if (!items.length) return msg('No items.');
    const text = items.map(x => `${x.url} | ${x.caption || ''}`.trim()).join('\n');
    await navigator.clipboard.writeText(text);
    msg(`Copied ${items.length} lines.`);
  });

  setInterval(refresh, 1500);
  refresh();
})();
