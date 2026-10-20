(expected) => {
  // A gallery thumbnail is a different signed rendition; never strip its crop/size parameters.
  const page = new URL(location.href);
  const id = page.searchParams.get('fbid') || page.pathname.match(/\/photo\/(\d+)/)?.[1];
  if (id !== expected.id) return null;
  const file = new URL(expected.url).pathname.split('/').pop();
  const candidates = [];
  for (const img of document.querySelectorAll('[role="main"] img,[role="dialog"] img')) {
    if (!img.complete || !img.naturalWidth || !img.naturalHeight) continue;
    let url;
    try { url = new URL(img.currentSrc || img.src); } catch { continue; }
    if (url.protocol !== 'https:' || !url.hostname.endsWith('.fbcdn.net')) continue;
    // Match the asset, not the largest unrelated cover/avatar/recommended image.
    if (url.pathname.split('/').pop() !== file) continue;
    if (/(?:^|_)c\d+[.,]/i.test(url.searchParams.get('stp') || '')) continue;
    if (img.closest('a[href*="photo.php"],a[href*="/photo/"],[role="navigation"]')) continue;
    const size = url.searchParams.get('ctp') || '';
    if (/^s\d+x\d+$/.test(size) && Math.max(img.naturalWidth, img.naturalHeight) <= 320) continue;
    candidates.push({ url: url.href, width: img.naturalWidth, height: img.naturalHeight });
  }
  candidates.sort((a, b) => b.width * b.height - a.width * a.height);
  return candidates[0] || null;
}
