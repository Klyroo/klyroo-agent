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
    this.readOnly = false;
    this.visibility = 'visible';
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
  contains(other) {
    for (let el = other; el; el = el.parent) if (el === this) return true;
    return false;
  }
  closest(selector) {
    const parts = selector.split(',').map((p) => p.trim());
    for (let el = this; el; el = el.parent) {
      for (const part of parts) {
        const attr = /^\[([\w-]+)(?:="([^"]*)")?\]$/.exec(part);
        if (attr && el.hasAttribute(attr[1]) && (attr[2] === undefined || el.getAttribute(attr[1]) === attr[2])) return el;
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
// visibility inherits, so the fake walks the ancestors like the cascade does.
globalThis.getComputedStyle = (el) => {
  for (let e = el; e; e = e.parent) if (e.visibility !== 'visible') return { visibility: e.visibility };
  return { visibility: 'visible' };
};
globalThis.document = {
  querySelectorAll: () => page,
  getElementById: () => null,
  querySelector: () => null,
};

const { createAgent, trackedRefCount, MAX_STEPS_PER_ACT } = await import('../index.js');

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

test('a confirmation step runs only when the policy returns exactly true', async () => {
  const b = button('Delete', { 'data-agent-confirm': '' });
  for (const decision of [undefined, null, 1, 'yes', {}]) {
    const result = await createAgent({ allow: () => decision }).act({ steps: [{ ref: 'Delete', action: 'press' }] });
    assert.equal(result.ok, false, `decision ${String(decision)}`);
    assert.match(result.error, /refused by agent policy/);
  }
  const caller = await createAgent().act({ steps: [{ ref: 'Delete', action: 'press' }], allow: () => undefined });
  assert.equal(caller.ok, false);
  assert.equal(b.clicks, 0);
  const approved = await createAgent({ allow: async () => true }).act({ steps: [{ ref: 'Delete', action: 'press' }] });
  assert.equal(approved.ok, true);
  assert.equal(b.clicks, 1);
});

test('a policy that returns nothing does not block a step that needs no confirmation', async () => {
  const b = button('Save');
  const result = await createAgent({ allow: () => undefined }).act({ steps: [{ ref: 'Save', action: 'press' }] });
  assert.equal(result.ok, true);
  assert.equal(b.clicks, 1);
});

function input(label, attrs = {}, parent = null) {
  const el = new globalThis.HTMLInputElement('input', { 'aria-label': label, ...attrs }, parent);
  page.push(el);
  return el;
}

test('readonly and aria-readonly inputs refuse edits but can be focused', async () => {
  const byAttr = input('A', { readonly: '' });
  const byAria = input('B', { 'aria-readonly': 'true' });
  const byProperty = input('C');
  byProperty.readOnly = true;
  const group = new FakeElement('div', { 'aria-readonly': 'true' });
  input('D', {}, group);
  const agent = createAgent();
  for (const name of ['A', 'B', 'C', 'D']) {
    for (const action of ['setValue', 'clear']) {
      const result = await agent.act({ steps: [{ ref: name, action, args: ['x'] }] });
      assert.equal(result.ok, false, `${name} ${action}`);
      assert.match(result.error, /read-only/);
    }
  }
  assert.equal(byAttr.value, '');
  assert.equal(byAria.value, '');
  assert.equal((await agent.act({ steps: [{ ref: 'A', action: 'focus' }] })).ok, true);
  assert.equal(agent.snapshot().find((i) => i.label === 'A').readOnly, true);
});

test('act only runs actions the element advertises in the snapshot', async () => {
  const b = button('Go');
  const field = input('Name');
  const agent = createAgent();
  const advertised = Object.fromEntries(agent.snapshot().map((i) => [i.label, i.actions]));
  assert.deepEqual(advertised.Go, ['press']);

  for (const action of ['setValue', 'clear', 'toggle', 'focus', 'constructor', '__proto__']) {
    const result = await agent.act({ steps: [{ ref: 'Go', action, args: ['x'] }] });
    assert.equal(result.ok, false, action);
    assert.match(result.error, /is not supported/);
  }
  assert.equal(b.clicks, 0);
  assert.equal((await agent.act({ steps: [{ ref: 'Name', action: 'press' }] })).ok, false);
  assert.equal(field.clicks, 0);
  assert.equal((await agent.act({ steps: [{ ref: 'Name', action: 'setValue', args: ['Ada'] }] })).ok, true);
  assert.equal(field.value, 'Ada');
});

test('an exact ref beats a label that happens to read like a ref', async () => {
  const decoy = button('c2'); // gets ref c-something; its label is "c2"
  const target = button('Target');
  const agent = createAgent();
  const snap = agent.snapshot();
  const targetRef = snap.find((i) => i.label === 'Target').ref;
  decoy.attrs['aria-label'] = targetRef; // now a label equals the target's ref
  decoy.textContent = targetRef;
  const result = await agent.act({ steps: [{ ref: targetRef, action: 'press' }] });
  assert.equal(result.ok, true);
  assert.equal(target.clicks, 1);
  assert.equal(decoy.clicks, 0);
});

test('act and query use the root the host registered, not the whole document', async () => {
  const inside = new FakeElement('button', { 'aria-label': 'Inside' });
  const outside = new FakeElement('button', { 'aria-label': 'Outside' });
  page.push(inside, outside);
  const root = new FakeElement('main');
  inside.parent = root;
  root.querySelectorAll = () => [inside];
  const agent = createAgent({ root });

  assert.deepEqual(agent.snapshot().map((i) => i.label), ['Inside']);
  assert.deepEqual((await agent.query({})).map((i) => i.label), ['Inside']);
  assert.equal((await agent.act({ steps: [{ ref: 'Inside', action: 'press' }] })).ok, true);
  const refused = await agent.act({ steps: [{ ref: 'Outside', action: 'press' }] });
  assert.equal(refused.ok, false);
  assert.match(refused.error, /not found/);
  assert.equal(outside.clicks, 0);

  // A within outside the root is ignored: the scope never widens.
  assert.deepEqual(agent.snapshot({ within: outside }).map((i) => i.label), ['Inside']);
  // A root given as a function is read on every call.
  assert.deepEqual(createAgent({ root: () => root }).snapshot().map((i) => i.label), ['Inside']);
});

test('visibility hidden, inert and aria-hidden ancestors make an element not displayed', async () => {
  const hiddenByStyle = button('Style');
  hiddenByStyle.visibility = 'hidden';
  const collapsed = new FakeElement('div');
  collapsed.visibility = 'hidden';
  const nestedStyle = button('Nested', {}, collapsed);
  const inertParent = new FakeElement('div', { inert: '' });
  const inert = button('Inert', {}, inertParent);
  const ariaParent = new FakeElement('section', { 'aria-hidden': 'true' });
  const aria = button('Aria', {}, ariaParent);
  const noCss = button('NoCss');
  noCss.checkVisibility = () => false;
  const visible = button('Visible');
  const agent = createAgent();

  for (const name of ['Style', 'Nested', 'Inert', 'Aria', 'NoCss']) {
    const result = await agent.act({ steps: [{ ref: name, action: 'press' }] });
    assert.equal(result.ok, false, name);
    assert.match(result.error, /not displayed/);
  }
  assert.equal([hiddenByStyle, nestedStyle, inert, aria, noCss].reduce((n, b) => n + b.clicks, 0), 0);
  assert.equal((await agent.act({ steps: [{ ref: 'Visible', action: 'press' }] })).ok, true);
  assert.equal(visible.clicks, 1);
  assert.equal(agent.snapshot().find((i) => i.label === 'Aria').offPage, true);
});

test('refs of detached elements are dropped instead of kept forever', () => {
  const gone = button('Gone');
  const agent = createAgent();
  agent.snapshot();
  const before = trackedRefCount();
  gone.isConnected = false;
  page.splice(page.indexOf(gone), 1);
  agent.snapshot();
  assert.equal(trackedRefCount(), before - 1);
});

test('act refuses more steps than the cap and runs none', async () => {
  const b = button('Go');
  const steps = Array.from({ length: MAX_STEPS_PER_ACT + 1 }, () => ({ ref: 'Go', action: 'press' }));
  const result = await createAgent().act({ steps });
  assert.equal(result.ok, false);
  assert.match(result.error, /Too many steps/);
  assert.equal(b.clicks, 0);
  assert.equal((await createAgent().act({ steps: steps.slice(0, MAX_STEPS_PER_ACT) })).ok, true);
  assert.equal(b.clicks, MAX_STEPS_PER_ACT);
});
