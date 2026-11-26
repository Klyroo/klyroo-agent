// Semantic Agent Runtime (snapshot, query, act), Unified Observe Stream, and WebMCP integration for Klyroo.UI.
// Inspired by ui-sdk.dev (@ui-sdk/core/agent and @ui-sdk/core/observe).

const observers = new Set();
const elementRefs = new WeakMap();
// ref -> WeakRef(element). Weak, and pruned of detached elements, so refs of removed DOM do not keep it alive.
const refToElement = new Map();
let refCounter = 0;

/** Normalizes text for accent- and case-insensitive label/query matching. */
function normalizeLabel(text) {
  if (!text) return '';
  return String(text)
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .trim()
    .toLowerCase()
    .replace(/\s+/g, ' ');
}

/** Returns true when an element holds sensitive data (password, OTP/PIN) whose value must be redacted in observe/snapshot. */
function isSensitiveElement(el) {
  if (!el || !(el instanceof Element)) return false;
  if (el.hasAttribute('data-agent-redact')) return true;
  if (el.localName === 'kly-pin-input') return true;
  if (el instanceof HTMLInputElement && el.type === 'password') return true;
  const input = el.querySelector?.('input[type="password"]');
  return Boolean(input);
}

/** Resolves the accessible label of an element. */
export function labelOf(el) {
  if (!el || !(el instanceof Element)) return '';
  const ariaLabel = el.getAttribute('aria-label');
  if (ariaLabel) return ariaLabel.trim();
  const labelledBy = el.getAttribute('aria-labelledby');
  if (labelledBy) {
    const text = labelledBy
      .split(/\s+/)
      .map((id) => document.getElementById(id)?.textContent?.trim())
      .filter(Boolean)
      .join(' ');
    if (text) return text;
  }
  if (el.id) {
    const labelEl = document.querySelector(`label[for="${CSS.escape(el.id)}"]`);
    if (labelEl) return labelEl.textContent.replace(/\*/g, '').trim();
  }
  const wrappingLabel = el.closest('label');
  if (wrappingLabel) {
    const clone = wrappingLabel.cloneNode(true);
    for (const ctrl of clone.querySelectorAll('input, select, textarea, button')) ctrl.remove();
    const t = clone.textContent.replace(/\*/g, '').trim();
    if (t) return t;
  }
  const fieldContainer = el.closest('[data-form-field], [data-slot="form-field"]');
  if (fieldContainer) {
    const lbl = fieldContainer.querySelector('[data-slot="form-field-label"], label');
    if (lbl) return lbl.textContent.replace(/\*/g, '').trim();
  }
  return el.getAttribute('placeholder') || el.getAttribute('title') || el.getAttribute('name') || el.getAttribute('data-form-field') || '';
}

function assignRef(el) {
  let ref = elementRefs.get(el);
  if (!ref) {
    ref = `c${++refCounter}`;
    elementRefs.set(el, ref);
    refToElement.set(ref, new WeakRef(el));
  }
  return ref;
}

/** Forgets the refs of elements that were removed from the document or garbage collected. */
function pruneRefs() {
  for (const [ref, weak] of refToElement) {
    const el = weak.deref();
    if (el === undefined || !el.isConnected) refToElement.delete(ref);
  }
}

/** The number of refs currently tracked, for tests and diagnostics. */
export function trackedRefCount() {
  return refToElement.size;
}

/** Emits an ObservedChange event to all registered `Klyroo.observe` subscribers. */
export function emitObservedChange(change) {
  const redacted = change.sensitive
    ? { ...change, value: '[redacted]', previous: '[redacted]' }
    : change;
  for (const entry of observers) {
    if (entry.within && change.element && !entry.within.contains(change.element)) continue;
    try {
      entry.callback(redacted);
    } catch {
      // Subscriber errors never break UI interactions.
    }
  }
}

/**
 * Subscribes to component changes across the page or within a container element.
 * Returns an unsubscribe function.
 */
export function observe(callback, { within = null } = {}) {
  const entry = { callback, within };
  observers.add(entry);
  return () => observers.delete(entry);
}

/** Discovers all agent-addressable components on the page (or within a container). */
function discoverComponents(root = document) {
  if (typeof document === 'undefined') return [];
  const selector = [
    '[data-kly-primitive]',
    'kly-select',
    'kly-select-menu',
    'kly-tabs',
    'kly-modal',
    'kly-collapsible',
    'kly-accordion',
    'kly-pin-input',
    'kly-color-picker',
    'kly-calendar',
    '[data-slot="datalist"]',
    '[data-slot="dashboard-filter"]',
    '[data-slot="form-engine"]',
    '[data-slot="spreadsheet-import"]',
    'input:not([type="hidden"])',
    'select',
    'textarea',
    'button',
  ].join(', ');

  const seen = new Set();
  const list = [];
  for (const el of root.querySelectorAll(selector)) {
    if (seen.has(el)) continue;
    // Skip inner native controls that belong to a higher-level kly-* custom element already in the list
    const ownerKly = el.parentElement?.closest('kly-select, kly-select-menu, kly-pin-input, kly-color-picker');
    if (ownerKly && seen.has(ownerKly)) continue;
    seen.add(el);
    list.push(el);
  }
  return list;
}

/** True when the element rejects interaction: disabled itself, inside a disabled fieldset, or marked aria-disabled. */
function isDisabledElement(el) {
  if (el.hasAttribute('disabled') || el.getAttribute('aria-disabled') === 'true') return true;
  try {
    return typeof el.matches === 'function' && el.matches(':disabled');
  } catch {
    return false;
  }
}

/** True when the element rejects edits: readonly or aria-readonly, itself or through an aria-readonly ancestor. */
function isReadOnlyElement(el) {
  if (el.readOnly === true || el.hasAttribute('readonly') || el.getAttribute('aria-readonly') === 'true') return true;
  return el.closest('[aria-readonly="true"]') !== null;
}

/**
 * True when the element is not displayed: detached, hidden, in a closed dialog, inert, under aria-hidden, or not
 * rendered by CSS (display none, visibility hidden or collapse, content-visibility hidden).
 */
function isOffPageElement(el) {
  if (!el.isConnected) return true;
  if (el.closest('[hidden], dialog:not([open]), [inert], [aria-hidden="true"]') !== null) return true;
  if (typeof el.checkVisibility === 'function') {
    // visibilityProperty is the standard option name; checkVisibilityCSS is the older spelling some browsers still honor.
    if (!el.checkVisibility({ visibilityProperty: true, checkVisibilityCSS: true })) return true;
  }
  if (typeof globalThis.getComputedStyle === 'function') {
    const visibility = globalThis.getComputedStyle(el)?.visibility;
    if (visibility === 'hidden' || visibility === 'collapse') return true;
  }
  return false;
}

/** True when the element, or an ancestor, asks for confirmation before an agent acts on it. */
function needsConfirmation(el) {
  return el.hasAttribute('data-agent-confirm') || el.closest('[data-agent-confirm]') !== null;
}

function describeElement(el) {
  const ref = assignRef(el);
  const sensitive = isSensitiveElement(el);
  const label = labelOf(el);
  const disabled = isDisabledElement(el);
  const offPage = isOffPageElement(el);
  const confirmRequired = needsConfirmation(el);
  const readOnly = isReadOnlyElement(el);
  return { ...describeKind(el, { ref, sensitive, label, disabled, offPage }), confirmRequired, readOnly };
}

function describeKind(el, { ref, sensitive, label, disabled, offPage }) {

  if (el.__klyPrimitive) {
    const prim = el.__klyPrimitive;
    const st = prim.store.get();
    const actions = prim.agent?.actions ?? Object.keys(prim.api).filter((k) => !['keydown', 'pointerdown', 'wheel'].includes(k));
    return {
      ref,
      component: prim.name,
      label,
      value: sensitive ? '[redacted]' : (st.value ?? st.open ?? st.muted ?? null),
      disabled,
      offPage,
      actions,
    };
  }

  const tag = el.localName;
  if (tag === 'select' || tag === 'kly-select' || tag === 'kly-select-menu') {
    const val = el.value ?? el.getAttribute('value') ?? '';
    return {
      ref,
      component: 'select',
      label,
      value: val,
      disabled,
      offPage,
      actions: ['setValue', 'clear'],
    };
  }

  if (tag === 'input' || tag === 'textarea' || tag === 'kly-pin-input') {
    const type = el.getAttribute('type') || 'text';
    if (type === 'checkbox' || type === 'radio') {
      return {
        ref,
        component: type,
        label,
        checked: Boolean(el.checked),
        disabled,
        offPage,
        actions: ['toggle', 'setChecked'],
      };
    }
    return {
      ref,
      component: 'input',
      label,
      value: sensitive ? '[redacted]' : (el.value ?? ''),
      disabled,
      offPage,
      actions: ['setValue', 'clear', 'focus'],
    };
  }

  if (tag === 'button') {
    return {
      ref,
      component: 'button',
      label: label || el.textContent.trim(),
      disabled,
      offPage,
      actions: ['press'],
    };
  }

  return {
    ref,
    component: el.getAttribute('data-slot') || tag,
    label,
    value: el.getAttribute('value') ?? null,
    disabled,
    offPage,
    actions: ['focus'],
  };
}

function extractChoices(el) {
  if (el instanceof HTMLSelectElement) {
    return [...el.options]
      .filter((o) => o.value !== '')
      .map((o) => ({ value: o.value, label: o.text.trim(), disabled: o.disabled, selected: o.selected }));
  }
  const options = el.querySelectorAll('[role="option"], option[value]:not([value=""])');
  return [...options].map((o) => ({
    value: o.getAttribute('data-value') ?? o.getAttribute('value') ?? o.textContent.trim(),
    label: o.textContent.trim(),
    disabled: o.hasAttribute('disabled') || o.getAttribute('aria-disabled') === 'true',
    selected: o.getAttribute('aria-selected') === 'true' || Boolean(o.selected),
  }));
}

/** Actions that change a value, refused on read-only elements. */
const EDIT_ACTIONS = new Set(['setValue', 'clear', 'toggle', 'setChecked']);

/** The most steps one `act` call accepts; the same cap as MaxStepsPerAct in the .NET bridge. */
export const MAX_STEPS_PER_ACT = 50;

/**
 * Creates the Klyroo Agent runtime (`snapshot`, `query`, `act`, `registerWebMcp`).
 * `root` (an element, or a function returning one) limits everything the agent sees and operates to that subtree:
 * snapshot, query and act use the same scope, so the agent cannot act on what it was not shown.
 */
export function createAgent({ allow = null, root = null } = {}) {
  function scopeRoot(within = null) {
    const base = (typeof root === 'function' ? root() : root) ?? null;
    const registered = base instanceof Element ? base : null;
    if (within instanceof Element && (!registered || registered.contains(within))) return within;
    return registered ?? document;
  }

  function snapshot({ within = null } = {}) {
    pruneRefs();
    const elements = discoverComponents(scopeRoot(within));
    const items = elements.slice(0, 100).map(describeElement);
    if (elements.length > 100) {
      items.push({ more: elements.length - 100 });
    }
    return items;
  }

  async function query({ within = null, text = '', kind = '' } = {}) {
    pruneRefs();
    const elements = discoverComponents(scopeRoot());
    const normWithin = normalizeLabel(within);
    const normText = normalizeLabel(text);
    const normKind = normalizeLabel(kind);

    if (normWithin) {
      const target = elements.find((el) => {
        const ref = elementRefs.get(el);
        return ref === within || normalizeLabel(labelOf(el)) === normWithin;
      });
      if (!target) {
        return { error: `No component found matching "${within}".` };
      }
      const allChoices = extractChoices(target);
      const filtered = normText
        ? allChoices.filter((c) => normalizeLabel(c.label).includes(normText) || normalizeLabel(c.value).includes(normText))
        : allChoices;
      return [
        {
          ...describeElement(target),
          items: filtered.slice(0, 50),
          more: Math.max(0, filtered.length - 50),
        },
      ];
    }

    const matches = [];
    for (const el of elements) {
      const desc = describeElement(el);
      if (normKind && normalizeLabel(desc.component) !== normKind) continue;
      const choices = extractChoices(el);
      const matchingChoices = normText
        ? choices.filter((c) => normalizeLabel(c.label).includes(normText) || normalizeLabel(c.value).includes(normText))
        : [];
      const labelMatches = !normText || normalizeLabel(desc.label).includes(normText);
      if (labelMatches || matchingChoices.length > 0) {
        matches.push({
          ...desc,
          ...(matchingChoices.length > 0 ? { items: matchingChoices.slice(0, 50), more: Math.max(0, matchingChoices.length - 50) } : {}),
        });
      }
    }
    return matches.slice(0, 50);
  }

  async function act({ steps = [], allow: callAllow = null } = {}) {
    const appliedSteps = [];
    const values = {};
    const fail = (error) => ({ ok: false, steps: appliedSteps, values, error });

    if (!Array.isArray(steps)) return fail('"steps" must be a list of steps.');
    if (steps.length > MAX_STEPS_PER_ACT) {
      return fail(`Too many steps: ${steps.length} given, at most ${MAX_STEPS_PER_ACT} per call. Nothing ran; send fewer steps.`);
    }
    pruneRefs();

    for (let i = 0; i < steps.length; i++) {
      const step = steps[i];
      try {
        if (!step || typeof step.ref !== 'string' || typeof step.action !== 'string') {
          return fail(`Step ${i + 1}: a step needs a "ref" and an "action" as text.`);
        }
        if (step.args !== undefined && step.args !== null && !Array.isArray(step.args)) {
          return fail(`Step ${i + 1}: "args" must be a list.`);
        }
        const elements = discoverComponents(scopeRoot());
        for (const candidate of elements) assignRef(candidate);
        // An exact ref wins over any label: a label that happens to read "c3" must not capture the step meant for ref c3.
        let matches = elements.filter((candidate) => elementRefs.get(candidate) === step.ref);
        if (matches.length === 0) {
          const normRef = normalizeLabel(step.ref);
          matches = elements.filter((candidate) => normalizeLabel(labelOf(candidate) || candidate.textContent) === normRef);
        }

        if (matches.length === 0) {
          return fail(`Step ${i + 1}: component "${step.ref}" not found.`);
        }
        if (matches.length > 1) {
          return fail(`Step ${i + 1}: ambiguous label "${step.ref}" matched ${matches.length} components (${matches.map(assignRef).join(', ')}). Use a specific ref.`);
        }

        const el = matches[0];
        const ref = assignRef(el);
        const name = labelOf(el) || el.textContent?.trim() || ref;

        if (isDisabledElement(el)) {
          return fail(`Step ${i + 1}: component "${name}" (${ref}) is disabled.`);
        }
        if (isOffPageElement(el)) {
          return fail(`Step ${i + 1}: component "${name}" (${ref}) is not displayed right now.`);
        }
        if (EDIT_ACTIONS.has(step.action) && isReadOnlyElement(el)) {
          return fail(`Step ${i + 1}: component "${name}" (${ref}) is read-only.`);
        }
        // Only the actions the snapshot lists for this element can run.
        const advertised = describeElement(el).actions ?? [];
        if (!advertised.includes(step.action)) {
          return fail(`Step ${i + 1}: action "${step.action}" is not supported by "${name}" (${ref}). Supported: ${advertised.join(', ') || 'none'}.`);
        }

        const needsConfirm = needsConfirmation(el);
        // A confirmation-required component is refused unless a policy decides about it.
        if (needsConfirm && !allow && !callAllow) {
          return fail(`Step ${i + 1}: action "${step.action}" on "${name}" (${ref}) needs confirmation and no allow policy is set.`);
        }
        const stepWithMeta = { ...step, ref, confirm: needsConfirm };

        // For a confirmation step only an explicit true approves: undefined, null or any other truthy value is a refusal.
        const approves = (decision) => (needsConfirm ? decision === true : decision !== false);
        if (callAllow && !approves(await callAllow(stepWithMeta))) {
          return fail(`Step ${i + 1}: action "${step.action}" refused by caller policy.`);
        }
        if (allow && !approves(await allow(stepWithMeta))) {
          return fail(`Step ${i + 1}: action "${step.action}" refused by agent policy.`);
        }

        const prevValue = el.value ?? el.getAttribute('value') ?? null;
        const args = step.args ?? [];

        if (el.__klyPrimitive && Object.hasOwn(el.__klyPrimitive.api, step.action) && typeof el.__klyPrimitive.api[step.action] === 'function') {
          await el.__klyPrimitive.api[step.action](...args);
        } else if (step.action === 'setValue') {
          const nextVal = args[0] ?? '';
          if ('value' in el) el.value = nextVal;
          el.setAttribute('value', String(nextVal));
          el.dispatchEvent(new Event('input', { bubbles: true }));
          el.dispatchEvent(new Event('change', { bubbles: true }));
          el.dispatchEvent(new CustomEvent('kly-change', { bubbles: true, detail: { value: nextVal, source: 'agent' } }));
        } else if (step.action === 'clear') {
          if ('value' in el) el.value = '';
          el.dispatchEvent(new Event('input', { bubbles: true }));
          el.dispatchEvent(new Event('change', { bubbles: true }));
        } else if (step.action === 'toggle' || step.action === 'setChecked') {
          if ('checked' in el) {
            el.checked = step.action === 'setChecked' ? Boolean(args[0]) : !el.checked;
            el.dispatchEvent(new Event('change', { bubbles: true }));
          } else {
            el.click();
          }
        } else if (step.action === 'press') {
          el.click();
        } else if (step.action === 'focus') {
          el.focus();
        } else {
          return fail(`Step ${i + 1}: unsupported action "${step.action}" on ${ref}.`);
        }

        const newValue = el.value ?? el.getAttribute('value') ?? null;
        const sensitive = isSensitiveElement(el);
        emitObservedChange({
          component: describeElement(el).component,
          ref,
          label: labelOf(el),
          key: 'value',
          previous: prevValue,
          value: newValue,
          reason: step.action,
          source: 'agent',
          sensitive,
          element: el,
        });

        values[ref] = sensitive ? '[redacted]' : newValue;
        appliedSteps.push({ ref, action: step.action, ok: true });
      } catch (err) {
        // Failures are values: a policy, a primitive or an event handler that throws fails the run instead of rejecting act().
        const message = err instanceof Error ? err.message : String(err);
        return fail(`Step ${i + 1}: action "${step?.action}" on "${step?.ref}" failed: ${message}`);
      }
    }

    return { ok: true, steps: appliedSteps, values };
  }

  return {
    snapshot,
    query,
    act,
    observe,
    agentTools,
    registerWebMcp: (options) => registerWebMcp({ snapshot, query, act }, options),
  };
}

/** Standard tool definitions for LLM / MCP / WebMCP integration. */
export const agentTools = [
  {
    name: 'snapshot',
    description: 'Lists the Klyroo.UI components on the page, their current state, and the actions they support.',
    annotations: { readOnlyHint: true, untrustedContentHint: true },
    inputSchema: {
      type: 'object',
      properties: {},
    },
  },
  {
    name: 'query',
    description: 'Finds components by label or kind, or lists available options inside a select, filter, or data list without opening popups.',
    annotations: { readOnlyHint: true, untrustedContentHint: true },
    inputSchema: {
      type: 'object',
      properties: {
        within: { type: 'string', description: 'Optional component ref or label to inspect choices within.' },
        text: { type: 'string', description: 'Optional search text to filter components or choices.' },
        kind: { type: 'string', description: 'Optional component kind filter (e.g. select, input, button).' },
      },
    },
  },
  {
    name: 'act',
    description: 'Calls one or more semantic actions on components by ref or label (e.g. setValue, clear, toggle, press).',
    annotations: { consequentialHint: true, untrustedContentHint: true },
    inputSchema: {
      type: 'object',
      required: ['steps'],
      properties: {
        steps: {
          type: 'array',
          maxItems: MAX_STEPS_PER_ACT,
          items: {
            type: 'object',
            required: ['ref', 'action'],
            properties: {
              ref: { type: 'string', minLength: 1, description: 'Component ref (e.g. c1) or accessible label.' },
              action: { type: 'string', minLength: 1, description: 'Action name from snapshot (e.g. setValue, clear, toggle, press).' },
              args: {
                type: 'array',
                items: { type: ['string', 'number', 'boolean', 'null', 'array'], items: { type: ['string', 'number', 'boolean', 'null'] } },
                description: 'Arguments for the action: text, numbers, booleans, null or lists of those.',
              },
            },
          },
        },
      },
    },
  },
];

/** Registers `snapshot`, `query`, and `act` with the browser's WebMCP (`document.modelContext.registerTool`) API if available. */
export function registerWebMcp(agent, { allow = null } = {}) {
  if (typeof document === 'undefined') return () => {};
  const mc = document.modelContext ?? globalThis.navigator?.modelContext;
  if (!mc || typeof mc.registerTool !== 'function') return () => {};

  const cleanups = [];
  for (const tool of agentTools) {
    const unregister = mc.registerTool({
      ...tool,
      execute: async (args) => {
        try {
          if (tool.name === 'snapshot') return agent.snapshot(args);
          if (tool.name === 'query') return agent.query(args);
          if (tool.name === 'act') return await agent.act({ ...args, allow });
          return null;
        } catch (err) {
          return { ok: false, error: err instanceof Error ? err.message : String(err) };
        }
      },
    });
    if (typeof unregister === 'function') cleanups.push(unregister);
  }
  return () => {
    for (const fn of cleanups) fn();
  };
}
