(expected => {
  const code = location.pathname.match(/\/(?:p|reels?|tv)\/([\w-]+)(?:\/|$)/)?.[1];
  if (code !== expected.code) return {state:'wrong_post'};
  if (document.querySelector('input[name="password"]')) return {state:'login'};
  const dialog = document.querySelector('[role="dialog"]');
  if (/try again later|please wait a few minutes|we restrict certain activity/i.test(dialog?.innerText || '')) return {state:'rate_limited'};
  const allowed = value => { try { const u = new URL(value); return u.protocol === 'https:'
    && /(^|\.)(cdninstagram\.com|fbcdn\.net)$/i.test(u.hostname); } catch { return false; } };
  const biggest = candidates => (candidates || []).filter(c => allowed(c.url || c.src))
    .sort((a,b) => (b.width || b.config_width || 0) * (b.height || b.config_height || 0)
      - (a.width || a.config_width || 0) * (a.height || a.config_height || 0))[0];
  const result = (items, caption, author, complete = true) => ({state:'ready', code, items,
    caption:caption || '', author:author || 'Instagram', complete});
  let budget = 80000;
  function find(value) {
    if (--budget < 0 || !value || typeof value !== 'object') return null;
    if ((value.code === code || value.shortcode === code)
      && (value.carousel_media || value.edge_sidecar_to_children || value.image_versions2 || value.display_resources || value.video_versions || value.video_url)) return value;
    for (const child of Object.values(value)) { const hit = find(child); if (hit) return hit; }
    return null;
  }
  function media(node) {
    const video = node.media_type === 2 || node.is_video === true || !!node.video_versions?.length || !!node.video_url;
    if (video) {
      const rendition = biggest(node.video_versions);
      const url = rendition?.url || node.video_url;
      return allowed(url) ? {url, isVideo:true} : null;
    }
    if (node.media_type !== 1 && node.is_video !== false && !node.display_resources) return null;
    const rendition = biggest(node.image_versions2?.candidates || node.display_resources);
    const url = rendition?.url || rendition?.src;
    return allowed(url) ? {url, isVideo:false} : null;
  }
  for (const script of document.querySelectorAll('script[type="application/json"],script[type="application/ld+json"]')) {
    if (script.textContent.length > 8000000) continue;
    try {
      const node = find(JSON.parse(script.textContent));
      if (!node) continue;
      const children = node.carousel_media || node.edge_sidecar_to_children?.edges?.map(e => e.node) || [node];
      const items = children.map(media);
      if (!items.length || items.some(item => !item)) continue;
      const caption = node.caption?.text || node.edge_media_to_caption?.edges?.[0]?.node?.text || '';
      return result(items, caption, node.user?.username || node.owner?.username);
    } catch {}
  }
  // DOM fallback is restricted to this post's article, never the profile grid or recommendations.
  const scope = dialog || document.querySelector('main,[role="main"]');
  const articles = scope ? [...scope.querySelectorAll('article')] : [];
  if (articles.length !== 1) {
    // Reel viewers can omit article elements. Only accept a single video in the viewport;
    // preloaded neighbors, posters and blob URLs cannot establish a downloadable rendition.
    if (/\/(?:reels?|tv)\//.test(location.pathname)) {
      const visible = [...(scope?.querySelectorAll('video') || [])].filter(video => {
        const r = video.getBoundingClientRect();
        return Math.min(r.right, innerWidth) - Math.max(r.left, 0) >= 250
          && Math.min(r.bottom, innerHeight) - Math.max(r.top, 0) >= 200;
      });
      if (visible.length === 1) {
        const url = visible[0].currentSrc || visible[0].src;
        if (allowed(url)) return result([{url,isVideo:true}], '', 'Instagram');
      }
    }
    return {state:'loading'};
  }
  const article = articles[0];
  const heading = article.querySelector('h1');
  const more = heading && [...heading.querySelectorAll('button,[role="button"]')]
    .find(button => button.getBoundingClientRect().width > 0 && button.getAttribute('aria-expanded') !== 'true'
      && /^(more|see more|…\s*more)$/i.test(button.textContent.trim()));
  if (more) { more.click(); return {state:'loading'}; }
  const caption = heading?.innerText || '';
  const author = article.querySelector('header a[href]')?.innerText || 'Instagram';
  const videos = [...article.querySelectorAll('video')];
  if (videos.length) {
    const urls = videos.map(v => v.currentSrc || v.src).filter(allowed);
    return urls.length === 1 ? result([{url:urls[0],isVideo:true}], caption, author) : {state:'loading'};
  }
  const images = [...article.querySelectorAll('img')].filter(img => {
    const r = img.getBoundingClientRect();
    return r.width >= 250 && r.height >= 200 && !img.closest('header,ul,[role="list"]');
  });
  if (images.length !== 1) return {state:'loading'};
  const image = images[0];
  const choices = (image.srcset || '').split(',').map(s => s.trim().split(/\s+/))
    .filter(p => allowed(p[0])).sort((a,b) => parseFloat(b[1] || 0) - parseFloat(a[1] || 0));
  const url = choices[0]?.[0] || image.currentSrc || image.src;
  if (!allowed(url) || image.naturalWidth < 300) return {state:'loading'};
  const next = [...article.querySelectorAll('button,[role="button"]')].find(button =>
    !button.disabled && button.getAttribute('aria-disabled') !== 'true'
      && (button.getAttribute('aria-label') === 'Next' || button.querySelector('svg[aria-label="Next"]')));
  return {...result([{url,isVideo:false}], caption, author, !next), canAdvance:!!next};
})
