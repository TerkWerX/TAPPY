import assert from 'node:assert/strict';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const root = path.dirname(fileURLToPath(import.meta.url));
const dist = path.join(root, 'dist');
const html = await readFile(path.join(dist, 'index.html'), 'utf8');
const css = await readFile(path.join(dist, 'site.css'), 'utf8');
const script = await readFile(path.join(dist, 'site.js'), 'utf8');
const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map((match) => match[1]);
assert.equal(new Set(ids).size, ids.length, 'Every HTML ID must be unique');
assert.equal((html.match(/<h1\b/g) || []).length, 1, 'One primary heading');
assert.match(html, /<html lang="en">/);

let references = 0;
for (const [, attribute, value] of html.matchAll(/\b(href|src|aria-controls|aria-labelledby)="([^"]+)"/g)) {
  if (attribute.startsWith('aria-')) {
    for (const id of value.split(' ')) assert.ok(ids.includes(id), `Missing ARIA target: ${id}`);
  } else if (value.startsWith('#')) {
    assert.ok(ids.includes(value.slice(1)), `Missing anchor: ${value}`);
  } else if (!/^https?:\/\//.test(value)) {
    assert.ok((await stat(path.join(dist, value))).isFile(), `Missing file: ${value}`);
    references++;
  }
}
for (const [tag] of html.matchAll(/<img\b[^>]*>/g)) {
  assert.match(tag, /\balt="[^"]*"/, 'Images need alt attributes');
  assert.match(tag, /\bwidth="\d+"/, 'Images need intrinsic width');
  assert.match(tag, /\bheight="\d+"/, 'Images need intrinsic height');
  const source = tag.match(/\bsrc="([^"]+)"/)[1];
  if (source.endsWith('.png') && !/^https?:/.test(source)) {
    const png = await readFile(path.join(dist, source));
    assert.equal(png.readUInt32BE(16), Number(tag.match(/\bwidth="(\d+)"/)[1]), `Incorrect intrinsic width: ${source}`);
    assert.equal(png.readUInt32BE(20), Number(tag.match(/\bheight="(\d+)"/)[1]), `Incorrect intrinsic height: ${source}`);
  }
}
for (const [tag] of html.matchAll(/<button\b[^>]*>/g)) assert.match(tag, /type="button"/);

const voidTags = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr']);
const stack = [];
for (const [, closing, name] of html.matchAll(/<(\/?)([a-z][a-z0-9-]*)\b[^>]*>/gi)) {
  if (voidTags.has(name)) continue;
  if (closing) assert.equal(stack.pop(), name, `Incorrect closing tag: ${name}`);
  else stack.push(name);
}
assert.deepEqual(stack, [], 'All HTML tags must close');
assert.equal((css.match(/\{/g) || []).length, (css.match(/\}/g) || []).length, 'Balanced CSS blocks');
assert.match(css, /prefers-reduced-motion/);
assert.match(css, /focus-visible/);
assert.match(css, /forced-colors/);
new vm.Script(script, { filename: 'site.js' });
console.log(`PASS: markup, accessibility references, ${references} local asset references, and script syntax`);

// Lightweight DOM stub: validates interaction state without starting a browser.
class Element {
  constructor(dataset = {}) { this.dataset = dataset; this.attributes = {}; this.style = {}; this.textContent = ''; this.children = []; this.listeners = {}; this.selectors = {}; }
  setAttribute(name, value) { this.attributes[name] = value; }
  getAttribute(name) { return this.attributes[name]; }
  addEventListener(name, callback) { this.listeners[name] = callback; }
  querySelector(selector) { return this.selectors[selector]; }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  click() { this.listeners.click?.(); }
}
const keys = Array.from({ length: 12 }, (_, index) => {
  const button = new Element({ key: String(index) });
  button.selectors.strong = new Element();
  button.selectors['.key-shortcut'] = new Element();
  return button;
});
const workflows = ['editing', 'studio', 'everyday'].map((workflow) => new Element({ workflow }));
const elementIds = ['assignment-title', 'assignment-description', 'assignment-label', 'assignment-steps', 'photo-highlight', 'locator-key-label'];
const elements = Object.fromEntries(elementIds.map((id) => [id, new Element()]));
const document = {
  querySelector: () => null,
  querySelectorAll: (selector) => selector === '.demo-key' ? keys : workflows,
  getElementById: (id) => elements[id] || null,
  createElement: () => new Element()
};
vm.runInNewContext(script, { document }, { timeout: 1000 });
let selections = 0;
for (const workflow of workflows) {
  workflow.click();
  assert.equal(workflows.filter((button) => button.getAttribute('aria-pressed') === 'true').length, 1);
  for (const [index, key] of keys.entries()) {
    key.click();
    assert.equal(keys.filter((button) => button.getAttribute('aria-pressed') === 'true').length, 1);
    assert.equal(key.getAttribute('aria-pressed'), 'true');
    assert.ok(elements['assignment-title'].textContent.length > 0);
    assert.ok(elements['assignment-description'].textContent.length > 0);
    assert.ok(elements['assignment-steps'].children.length > 0);
    assert.equal(elements['locator-key-label'].textContent, `G${index + 1} selected`);
    for (const coordinate of ['left', 'top', 'width', 'height']) {
      const value = parseFloat(elements['photo-highlight'].style[coordinate]);
      assert.ok(value > 0 && value < 100, `Invalid photo coordinate ${coordinate}`);
    }
    selections++;
  }
}
console.log(`PASS: all ${selections} example selections, assignment steps, and photo-highlight updates (in-memory check)`);

const luminance = (hex) => {
  const channels = hex.match(/[a-f\d]{2}/gi).map((channel) => parseInt(channel, 16) / 255).map((channel) => channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4);
  return channels[0] * .2126 + channels[1] * .7152 + channels[2] * .0722;
};
const pairs = [
  ['f2f6fc', '080b11'], ['b0bdce', '111823'], ['a0b1c7', '080b11'],
  ['061a15', '67e6c4'], ['c0cde0', '202d3d'], ['c7f4e8', '1b5044'],
  ['a9bdd3', '203340'], ['9bb1c9', '0b141c'], ['30a6e8', '080b11'],
  ['a4b5c8', '162130'], ['91bedf', '111923'], ['baf6e4', '080f19']
];
let minimum = Infinity;
for (const [foreground, background] of pairs) {
  const values = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  const ratio = (values[0] + .05) / (values[1] + .05);
  assert.ok(ratio >= 4.5, `Low text contrast: #${foreground} on #${background} (${ratio})`);
  minimum = Math.min(minimum, ratio);
}
console.log(`PASS: ${pairs.length} representative text pairs meet 4.5:1 contrast (minimum ${minimum.toFixed(2)}:1)`);
console.log('Static checks complete. These are not browser rendering or physical-device tests.');
