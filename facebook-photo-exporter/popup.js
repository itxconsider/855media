(() => {
  const startBtn = document.getElementById('start');
  const stopBtn = document.getElementById('stop');
  const clearBtn = document.getElementById('clear');
  const refreshBtn = document.getElementById('refresh');
  const refreshBtn2 = document.getElementById('refresh2');
  const exportTxtBtn = document.getElementById('exportTxt');
  const exportJsonBtn = document.getElementById('exportJson');
  const copyAllBtn = document.getElementById('copyAll');
  const exportCsvBtn = document.getElementById('exportCsv');
  const copyWithCaptionBtn = document.getElementById('copyWithCaption');
  const statusEl = document.getElementById('status');
  const countEl = document.getElementById('count');
  const pageUrlEl = document.getElementById('pageUrl');
  const messageEl = document.getElementById('message');

  function setMessage(message) {
    messageEl.textContent = message || '-';
  }

  async function getActiveTabId() {
    const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
    return tabs?.[0]?.id;
  }

  function setState(state) {
    statusEl.textContent = state.running ? 'Running' : 'Idle';
    countEl.textContent = String(state.urls?.length || 0);
    pageUrlEl.textContent = state.pageUrl || '-';
  }

  async function getState(tabId) {
    const response = await chrome.tabs.sendMessage(tabId, { type: 'FPE_GET_STATE' });
    return response || { running: false, urls: [], pageUrl: null };
  }

  async function send(tabId, type) {
    await chrome.tabs.sendMessage(tabId, { type });
  }

  function downloadFile(name, content, mime) {
    const blob = new Blob([content], { type: mime });
    const url = URL.createObjectURL(blob);
    chrome.downloads.download({ url, filename: name, saveAs: true }, () => {
      setTimeout(() => URL.revokeObjectURL(url), 5000);
    });
  }

  function csvEscape(value) {
    const text = String(value || '');
    if (/[",\n]/.test(text)) {
      return `"${text.replace(/"/g, '""')}"`;
    }
    return text;
  }

  async function refresh() {
    const tabId = await getActiveTabId();
    if (!tabId) return;
    try {
      const state = await getState(tabId);
      setState(state);
      setMessage('');
    } catch {
      setState({ running: false, urls: [], pageUrl: 'Not a Facebook tab or content script unavailable.' });
      setMessage('Open a Facebook tab and reload extension.');
    }
  }

  startBtn.addEventListener('click', async () => {
    const tabId = await getActiveTabId();
    if (!tabId) return;
    await send(tabId, 'FPE_START');
    await refresh();
  });

  stopBtn.addEventListener('click', async () => {
    const tabId = await getActiveTabId();
    if (!tabId) return;
    await send(tabId, 'FPE_STOP');
    await refresh();
  });

  clearBtn.addEventListener('click', async () => {
    const tabId = await getActiveTabId();
    if (!tabId) return;
    await send(tabId, 'FPE_CLEAR');
    await refresh();
  });

  refreshBtn.addEventListener('click', refresh);
  refreshBtn2.addEventListener('click', refresh);

  exportTxtBtn.addEventListener('click', async () => {
    const tabId = await getActiveTabId();
    if (!tabId) return;
    const state = await getState(tabId);
    if (!state.urls?.length) {
      setMessage('No URLs to export.');
      return;
    }
    downloadFile('facebook-photo-urls.txt', state.urls.join('\n'), 'text/plain;charset=utf-8');
    setMessage(`Exported TXT (${state.urls.length} URLs).`);
  });

  exportJsonBtn.addEventListener('click', async () => {
    const tabId = await getActiveTabId();
    if (!tabId) return;
    const state = await getState(tabId);
    if (!state.urls?.length) {
      setMessage('No URLs to export.');
      return;
    }
    const payload = {
      exportedAt: new Date().toISOString(),
      pageUrl: state.pageUrl,
      count: state.urls.length,
      urls: state.urls,
      items: state.items || state.urls.map(url => ({ url, caption: '' })),
    };
    downloadFile('facebook-photo-urls.json', JSON.stringify(payload, null, 2), 'application/json;charset=utf-8');
    setMessage(`Exported JSON (${state.urls.length} URLs).`);
  });

  copyAllBtn.addEventListener('click', async () => {
    const tabId = await getActiveTabId();
    if (!tabId) return;
    const state = await getState(tabId);
    if (!state.urls?.length) {
      setMessage('No URLs to copy.');
      return;
    }

    try {
      await navigator.clipboard.writeText(state.urls.join('\n'));
      setMessage(`Copied ${state.urls.length} URLs to clipboard.`);
    } catch {
      setMessage('Clipboard blocked. Export TXT instead.');
    }
  });

  exportCsvBtn.addEventListener('click', async () => {
    const tabId = await getActiveTabId();
    if (!tabId) return;
    const state = await getState(tabId);
    const items = state.items || state.urls?.map(url => ({ url, caption: '' })) || [];
    if (!items.length) {
      setMessage('No URLs to export.');
      return;
    }

    const lines = ['url,caption'];
    for (const item of items) {
      lines.push(`${csvEscape(item.url)},${csvEscape(item.caption || '')}`);
    }
    downloadFile('facebook-photo-urls-captions.csv', lines.join('\n'), 'text/csv;charset=utf-8');
    setMessage(`Exported CSV (${items.length} rows).`);
  });

  copyWithCaptionBtn.addEventListener('click', async () => {
    const tabId = await getActiveTabId();
    if (!tabId) return;
    const state = await getState(tabId);
    const items = state.items || state.urls?.map(url => ({ url, caption: '' })) || [];
    if (!items.length) {
      setMessage('No URLs to copy.');
      return;
    }

    const payload = items
      .map(item => `${item.url}\n${item.caption || ''}`.trimEnd())
      .join('\n\n');

    try {
      await navigator.clipboard.writeText(payload);
      setMessage(`Copied ${items.length} URL+caption blocks.`);
    } catch {
      setMessage('Clipboard blocked. Export CSV instead.');
    }
  });

  refresh();
})();
