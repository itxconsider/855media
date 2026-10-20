const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../../855Media/Resources/facebook-scraper-bridge.js'), 'utf8');
function fixture() {
  const messages = [], intervals = new Map(), timeouts = new Map(), elements = new Map();
  let links = [], login = false, loading = false, now = 0, next = 1, observer;
  const root = { clientHeight: 600, scrollHeight: 600, scrollTop: 0 };
  let scrollTop = 0;
  Object.defineProperty(root, 'scrollTop', { get: () => scrollTop,
    set: value => { scrollTop = Math.max(0, Math.min(value, root.scrollHeight - root.clientHeight)); } });
  const main = {
    querySelectorAll(selector) { return selector.includes('a[href') ? links : []; },
    querySelector(selector) { return selector.includes('progressbar') && loading ? {} : null; }
  };
  const location = { href: 'https://www.facebook.com/example/photos', pathname: '/example/photos' };
  const document = {
    documentElement: root, scrollingElement: root,
    body: { appendChild(el) { elements.set(el.id, el); } },
    getElementById(id) { return elements.get(id); },
    createElement() { return { style: {}, textContent: '' }; },
    querySelector(selector) { return selector.includes('role="main"') ? main : login ? {} : null; },
    addEventListener() {}
  };
  const window = { chrome: { webview: { postMessage(message) { messages.push(JSON.parse(message)); }, addEventListener() {} } } };
  window.self = window; window.top = window;
  const context = { window, document, location, URL, Date: { now: () => now }, getComputedStyle: () => ({ overflowY: 'visible' }),
    MutationObserver: class { constructor(callback) { observer = callback; } observe() {} },
    setInterval(callback) { const id = next++; intervals.set(id, callback); return id; },
    clearInterval(id) { intervals.delete(id); },
    setTimeout(callback) { const id = next++; timeouts.set(id, callback); return id; } };
  vm.runInNewContext(source, context);
  function photo(id, token = 'first', tag = 'img', host = 'scontent.example.fbcdn.net') {
    const src = `https://${host}/v/t39.30808-6/${id}_1_n.jpg?token=${token}`;
    const img = { tagName: tag, currentSrc: tag === 'img' ? src : '',
      getAttribute(name) { return name === 'alt' ? `Photo ${id}` : name === (tag === 'img' ? 'src' : 'href') ? src : null; },
      getBoundingClientRect() { return { width: 185, height: 185 }; } };
    return { href: `https://www.facebook.com/photo.php?fbid=${id}&set=a.999`, closest() { return null; }, querySelector() { return img; }, getAttribute() { return null; } };
  }
  return { window, messages, photo, root, document, intervals, timeouts,
    links(value) { links = value; }, login(value) { login = value; }, loading(value) { loading = value; },
    tick() { now += 1400; for (const callback of [...intervals.values()]) callback(); },
    observer(records) { observer(records); },
    navigate(path) { location.pathname = path; location.href = 'https://www.facebook.com' + path; },
    payload() { return JSON.parse(window.__fbdGetPayload()); } };
}
function check(name, run) { run(); console.log('PASS: ' + name); }
check('no passive collection before a user action', () => { const f = fixture(); f.links([f.photo('101')]); assert.equal(f.window.__fbdGetCount(), 0); });
check('unlimited collection passes the old 500/1000 photo caps', () => { const f = fixture(); f.links(Array.from({length:1005}, (_,i) => f.photo(String(i+10000)))); f.window.__fbdStartScraping(); assert.equal(f.window.__fbdGetCount(),1005); assert.equal(f.intervals.size,1); });
check('virtualized galleries retain photos scrolled out of the DOM', () => { const f=fixture(); f.links([f.photo('101')]); f.window.__fbdStartScraping(); f.links([f.photo('102')]); f.tick(); assert.equal(f.payload().items.length,2); });
check('photo ID deduplicates fresh signed URLs and keeps their latest signature', () => { const f=fixture(); f.links([f.photo('101')]); f.window.__fbdStartScraping(); f.links([f.photo('101','fresh')]); f.tick(); const p=f.payload(); assert.equal(p.items.length,1); assert.ok(p.items[0].url.endsWith('token=fresh')); });
check('SVG image elements and source photo links are preserved', () => { const f=fixture(); f.links([f.photo('101','first','image')]); f.window.__fbdStartScraping(); assert.equal(f.payload().items.length,1); assert.equal(f.payload().items[0].photoPageUrl,'https://www.facebook.com/photo.php?fbid=101&set=a.999'); });
check('non-Facebook image hosts are ignored', () => { const f=fixture(); f.links([f.photo('101','first','img','evil.test')]); f.window.__fbdStartScraping(); assert.equal(f.payload().items.length,0); });
check('returning Import payload does not also send a duplicate MEDIA_PAYLOAD event', () => { const f=fixture(); f.links([f.photo('101')]); assert.equal(f.payload().items.length,1); assert.equal(f.messages.filter(m=>m.type==='MEDIA_PAYLOAD').length,0); });
check('collection stops at the bottom after repeated empty loading cycles', () => { const f=fixture(); f.links([f.photo('101')]); f.window.__fbdStartScraping(); for(let i=0;i<15;i++)f.tick(); assert.equal(f.intervals.size,0); assert.equal(f.messages.at(-1).reason,'no_more_photos'); });
check('loading galleries are not reported as complete', () => { const f=fixture(); f.loading(true); f.window.__fbdStartScraping(); for(let i=0;i<15;i++)f.tick(); assert.equal(f.intervals.size,1); assert.notEqual(f.messages.at(-1).reason,'no_more_photos'); });
check('stalled loading is reported rather than claimed complete', () => { const f=fixture(); f.loading(true); f.window.__fbdStartScraping(); for(let i=0;i<45;i++)f.tick(); assert.equal(f.messages.at(-1).reason,'stalled'); });
check('login stops collection and asks for sign-in', () => { const f=fixture(); f.login(true); f.window.__fbdStartScraping(); assert.equal(f.messages.at(-1).reason,'waiting_for_login'); assert.equal(f.intervals.size,0); });
check('new profiles do not mix with previously collected photos', () => { const f=fixture(); f.links([f.photo('101')]); f.window.__fbdStartScraping(); f.window.__fbdStopScraping(); f.navigate('/other/photos'); f.links([f.photo('202')]); f.window.__fbdStartScraping(); assert.deepEqual(f.payload().items.map(i=>i.id),['202']); });
check('badge mutations do not recursively rescan the entire document', () => { const f=fixture(); f.window.__fbdStartScraping(); f.observer([{target:{id:'__855media_badge'}}]); assert.equal(f.timeouts.size,0); });
check('Clear stops and empties the collection', () => { const f=fixture(); f.links([f.photo('101')]); f.window.__fbdStartScraping(); f.window.__fbdClear(); assert.equal(f.window.__fbdGetCount(),0); assert.equal(f.intervals.size,0); });
console.log('All 14 photo collector checks passed.');
