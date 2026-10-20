(options = {}) => {
  const image = options.imageUrl && Array.from(document.querySelectorAll('img')).find(i => (i.currentSrc || i.src) === options.imageUrl);
  const dialog = image?.closest('[role="dialog"]') || document.querySelector('[role="dialog"][aria-label="Photo Viewer"]');
  let root = dialog || image?.closest('[role="main"]') || document.querySelector('[role="main"]') || document;
  // Standalone pages put the image in main and the caption in an adjacent panel.
  if (!dialog && root !== document && !root.querySelector('h2 a,[role="heading"][aria-level="2"] a')) {
    for (let parent = root.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
      if (parent.querySelector('h2 a,[role="heading"][aria-level="2"] a')) { root = parent; break; }
    }
  }
  let block = Array.from(root.querySelectorAll('[data-ad-preview="message"],[data-ad-comet-preview="message"],[data-testid="post_message"]'))
    .find(e => !e.closest('[aria-label^="Comment by"],[contenteditable="true"]'));
  let headerFound = false;
  if (!block) {
    // Photo pages also use a plain caption sibling after the author/actions header.
    // Never read the whole side panel: that includes timestamps, reactions and comments.
    for (const heading of root.querySelectorAll('h2,[role="heading"][aria-level="2"]')) {
      if (!heading.querySelector('a[href]')) continue;
      for (let p = heading.parentElement, depth = 0; p && p !== root && depth < 9; p = p.parentElement, depth++) {
        if (!p.querySelector('[role="button"][aria-haspopup="menu"]')) continue;
        headerFound = true;
        const sibling = p.nextElementSibling;
        if (sibling && !sibling.querySelector('h2,h3,[role="heading"],[role="article"],[contenteditable="true"],a[href*="comment_id="]')
            && (sibling.matches('[dir="auto"]') || sibling.querySelector('[dir="auto"]')))
          block = sibling;
        break;
      }
      if (headerFound) break;
    }
  }
  if (!block) return { caption: '', captionReady: headerFound };
  const controls = Array.from(block.querySelectorAll('[role="button"],button'));
  const more = controls.find(e => /^(see more|show more|មើលច្រើនទៀត|ดูเพิ่มเติม)$/i.test((e.innerText || e.textContent || '').trim()));
  if (more) {
    if (options.expand) more.click();
    return { caption: '', captionReady: false };
  }
  // A reaction/action row is not a caption slot on captionless posts.
  if (controls.some(e => !/^(see less|show less)$/i.test((e.innerText || e.textContent || '').trim())))
    return { caption: '', captionReady: true };
  function text(node) {
    if (node.nodeType === 3) return node.textContent;
    if (node.nodeType !== 1 || node.matches('[role="button"],button,[aria-hidden="true"],script,style')) return '';
    if (node.tagName === 'BR') return '\n';
    if (node.tagName === 'IMG') return node.getAttribute('alt') || '';
    const value = Array.from(node.childNodes).map(text).join('');
    return /^(DIV|P|LI)$/.test(node.tagName) ? value + '\n' : value;
  }
  return { caption: text(block).replace(/\u00a0/g, ' ').replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim(), captionReady: true };
}
