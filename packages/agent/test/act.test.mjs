// Runs the browser runtime against a minimal fake DOM, so `node --test` needs no browser and no extra package.
import test from 'node:test';
import assert from 'node:assert/strict';

class FakeElement {
  constructor(tag, attrs = {}, parent = null) {
    this.localName = tag;
    this.attrs = { ...attrs };
    this.parent = parent;
    this.isConnected = true;
    this.textContent = attrs.text ?? '';
    this.value = attrs.value ?? '';
    this.disabled = 'disabled' in attrs;
    this.clicks = 0;
    this.throwOnClick = false;
  }
  hasAttribute(name) { return name in this.attrs; }
  getAttribute(name) { return this.attrs[name] ?? null; }
  setAttribute(name, value) { this.attrs[name] = String(value); }
  querySelector() { return null; }
  querySelectorAll() { return []; }
  dispatchEvent() { return true; }
  focus() {}
  click() {
    if (this.throwOnClick) throw new Error('handler exploded');
    this.clicks += 1;
  }
  cloneNode() { return this; }
  matches(selector) { return selector === ':disabled' ? this.disabled : false; }
  closest(selector) {
    const parts = selector.split(',').map((p) => p.trim());
    for (let el = this; el; el = el.parent) {
      for (const part of parts) {
        const attr = /^\[([\w-]+)\]$/.exec(part);
        if (attr && el.hasAttribute(attr[1])) return el;
        if (part === 'dialog:not([open])' && el.localName === 'dialog' && !el.hasAttribute('open')) return el;
        if (part === el.localName) return el;
      }
    }
    return null;
  }
}

const page = [];
globalThis.Element = FakeElement;
globalThis.HTMLInputElement = class extends FakeElement {};
globalThis.HTMLSelectElement = class extends FakeElement {};
globalThis.CSS = { escape: (s) => s };
globalThis.document = {
  querySelectorAll: () => page,
  getElementById: () => null,
  querySelector: () => null,
};

const { createAgent } = await import('../index.js');

function button(label, attrs = {}, parent = null) {
  const el = new FakeElement('button', { 'aria-label': label, ...attrs }, parent);
  el.textContent = label;
  page.push(el);
  return el;
}

test.beforeEach(() => { page.length = 0; });

test('a disabled component is refused', async () => {
  const b = button('Save', { disabled: '' });
  const result = await createAgent().act({ steps: [{ ref: 'Save', action: 'press' }] });
  assert.equal(result.ok, false);
  assert.match(result.error, /is disabled/);
  assert.equal(b.clicks, 0);
});

test('a component inside a disabled fieldset is refused', async () => {
  const b = button('Save');
  b.disabled = true;
  const result = await createAgent().act({ steps: [{ ref: 'Save', action: 'press' }] });
  assert.equal(result.ok, false);
  assert.match(result.error, /is disabled/);
});

test('a hidden component, or one inside a closed dialog, is refused', async () => {
  const hidden = button('Hidden', { hidden: '' });
  const dialog = new FakeElement('dialog');
  const inner = button('Inner', {}, dialog);
  const agent = createAgent();

  const first = await agent.act({ steps: [{ ref: 'Hidden', action: 'press' }] });
  assert.equal(first.ok, false);
  assert.match(first.error, /not displayed/);

  const second = await agent.act({ steps: [{ ref: 'Inner', action: 'press' }] });
  assert.equal(second.ok, false);
  assert.match(second.error, /not displayed/);
  assert.equal(hidden.clicks + inner.clicks, 0);
});

test('an exception thrown by the action becomes a failed result', async () => {
  const b = button('Boom');
  b.throwOnClick = true;
  const result = await createAgent().act({ steps: [{ ref: 'Boom', action: 'press' }] });
  assert.equal(result.ok, false);
  assert.match(result.error, /failed: handler exploded/);
});

test('an exception thrown by a policy becomes a failed result and nothing runs', async () => {
  const b = button('Go');
  const result = await createAgent({ allow: () => { throw new Error('policy down'); } })
    .act({ steps: [{ ref: 'Go', action: 'press' }] });
  assert.equal(result.ok, false);
  assert.match(result.error, /policy down/);
  assert.equal(b.clicks, 0);
});

test('a confirmation-required component is refused when no policy is set', async () => {
  const b = button('Delete', { 'data-agent-confirm': '' });
  const result = await createAgent().act({ steps: [{ ref: 'Delete', action: 'press' }] });
  assert.equal(result.ok, false);
  assert.match(result.error, /needs confirmation/);
  assert.equal(b.clicks, 0);
});

test('a confirmation-required component runs when a policy approves it', async () => {
  const b = button('Delete', { 'data-agent-confirm': '' });
  const seen = [];
  const result = await createAgent({ allow: (step) => { seen.push(step.confirm); return true; } })
    .act({ steps: [{ ref: 'Delete', action: 'press' }] });
  assert.equal(result.ok, true);
  assert.deepEqual(seen, [true]);
  assert.equal(b.clicks, 1);
});

test('a per-call policy also counts as a policy, and can refuse', async () => {
  const b = button('Delete', { 'data-agent-confirm': '' });
  const agent = createAgent();
  const refused = await agent.act({ steps: [{ ref: 'Delete', action: 'press' }], allow: () => false });
  assert.equal(refused.ok, false);
  assert.match(refused.error, /refused by caller policy/);
  assert.equal(b.clicks, 0);
  const approved = await agent.act({ steps: [{ ref: 'Delete', action: 'press' }], allow: () => true });
  assert.equal(approved.ok, true);
});

test('a component without the confirm marker needs no policy', async () => {
  const b = button('Save');
  const result = await createAgent().act({ steps: [{ ref: 'Save', action: 'press' }] });
  assert.equal(result.ok, true);
  assert.equal(b.clicks, 1);
});

test('malformed steps fail with a message instead of throwing', async () => {
  button('Save');
  const agent = createAgent();
  assert.equal((await agent.act({ steps: 'nope' })).ok, false);
  assert.equal((await agent.act({ steps: [{ action: 'press' }] })).ok, false);
  assert.equal((await agent.act({ steps: [{ ref: 'Save', action: 'press', args: 'x' }] })).ok, false);
});

test('snapshot reports disabled, off page and confirmRequired', () => {
  button('Save', { disabled: '' });
  button('Delete', { 'data-agent-confirm': '' });
  const [save, del] = createAgent().snapshot();
  assert.equal(save.disabled, true);
  assert.equal(save.confirmRequired, false);
  assert.equal(del.confirmRequired, true);
});
