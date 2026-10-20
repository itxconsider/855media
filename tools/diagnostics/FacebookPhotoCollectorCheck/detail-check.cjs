const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../../855Media/Resources/facebook-photo-detail.js'), 'utf8');
const thumbnail = 'https://scontent.example.fbcdn.net/v/t39/861234978_2238776080014473_1655746097447648014_n.jpg?stp=c0.169.1536.1536a_dst-jpg_tt6&ctp=s206x206&oh=thumbnail-signature';
const full = thumbnail.split('?')[0] + '?stp=dst-jpg_tt6&cstp=mx1536x2048&ctp=s1536x2048&oh=full-signature';
const expected = { id: '2238773100014771', url: thumbnail };
function image(url, width, height, gallery = false) {
  return { currentSrc: url, naturalWidth: width, naturalHeight: height, complete: true,
    closest() { return gallery ? {} : null; } };
}
function read(images, href = 'https://www.facebook.com/photo.php?fbid=' + expected.id) {
  const context = { URL, location: { href }, document: { querySelectorAll() { return images; } } };
  return vm.runInNewContext('(' + source + ')', context)(expected);
}
let count = 0;
function check(name, run) { run(); count++; console.log('PASS: ' + name); }
check('206px cropped grid image resolves to the actual 1536x2048 signed rendition', () => {
  const result = read([image(thumbnail, 206, 206, true), image(full, 1536, 2048)]);
  assert.equal(result.url, full); assert.equal(result.width, 1536); assert.equal(result.height, 2048);
});
check('large unrelated covers and avatars cannot replace the requested photo', () => {
  const other = full.replace('861234978_', '999999999_');
  assert.equal(read([image(other, 4096, 4096), image(full, 1536, 2048)]).url, full);
});
check('wrong photo navigation cannot return a stale image', () => {
  assert.equal(read([image(full, 1536, 2048)], 'https://www.facebook.com/photo.php?fbid=other'), null);
});
check('login redirects cannot return a photo', () => {
  assert.equal(read([image(full, 1536, 2048)], 'https://www.facebook.com/login/'), null);
});
check('a cropped gallery image is never used as a fallback', () => {
  assert.equal(read([image(thumbnail, 206, 206)]), null);
});
check('uncropped small gallery renditions are not called full size', () => {
  const small = full.replace('s1536x2048', 's206x206');
  assert.equal(read([image(small, 206, 206)]), null);
});
check('matching grid links still cannot be mistaken for a viewer image', () => {
  assert.equal(read([image(full, 1536, 2048, true)]), null);
});
check('unfinished image loads wait rather than return zero-size data', () => {
  const loading = image(full, 1536, 2048); loading.complete = false;
  assert.equal(read([loading]), null);
});
check('highest available matching rendition is selected', () => {
  const medium = full.replace('s1536x2048', 's768x1024');
  assert.equal(read([image(medium, 768, 1024), image(full, 1536, 2048)]).url, full);
});
check('signed CDN parameters are preserved byte for byte', () => {
  const signed = full + '&token=a%2Fb%2Bz&expiry=2026';
  assert.equal(read([image(signed, 1536, 2048)]).url, signed);
});
check('host spoofing cannot supply the full-size image', () => {
  assert.equal(read([image(full.replace('.fbcdn.net', '.fbcdn.net.evil.test'), 1536, 2048)]), null);
});
check('alternate numeric photo URL is supported', () => {
  assert.equal(read([image(full, 1536, 2048)], 'https://www.facebook.com/photo/' + expected.id + '/').url, full);
});
console.log(`All ${count} photo detail checks passed.`);
