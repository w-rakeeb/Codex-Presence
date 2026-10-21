const icons = {
  Manual: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  Overview: '<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="4" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="11" width="7" height="10" rx="1"/>',
  Privacy: '<path d="m12 3 8 3v6c0 4-4 7-8 9-4-2-8-5-8-9V6l8-3Z"/><path d="m8 12 3 3 5-6"/>',
  Layout: '<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 9h18M10 9v11"/>',
  Settings: '<path d="M4 7h16M4 17h16"/><circle cx="9" cy="7" r="3"/><circle cx="15" cy="17" r="3"/>',
  Tools: '<path d="M15 4a5 5 0 0 0-6 6L3 17a3 3 0 0 0 4 4l7-7a5 5 0 0 0 6-6l-4 3-3-3 2-4Z"/>',
  activity: '<path d="M3 12h4l3-8 4 16 3-8h4"/>',
  pause: '<path d="M9 5v14M15 5v14"/>',
  play: '<path d="m8 5 11 7-11 7V5Z"/>',
  background: '<rect x="3" y="4" width="18" height="14" rx="2"/><path d="M8 22h8M12 18v4M7 9h10M7 13h6"/>',
  more: '<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
  arrow: '<path d="M5 12h14m-5-5 5 5-5 5"/>',
  copy: '<rect x="8" y="8" width="12" height="13" rx="2"/><path d="M15 8V3H3v13h5"/>',
  check: '<path d="m5 12 4 4L19 6"/>',
  search: '<circle cx="10" cy="10" r="6"/><path d="m15 15 5 5"/>',
  token: '<path d="m12 3 9 5v8l-9 5-9-5V8l9-5Z"/><path d="m3 8 9 5 9-5M12 13v8"/>',
  cache: '<path d="M4 10a8 8 0 1 1 2 8M4 4v6h6"/>',
  context: '<path d="M8 3H4v18h4M16 3h4v18h-4M9 8h6M9 12h6M9 16h6"/>',
  cost: '<path d="M12 3v18M17 7H9a3 3 0 0 0 0 6h6a3 3 0 0 1 0 6H6"/>',
  close: '<path d="m6 6 12 12M6 18 18 6"/>'
};
const icon = (name) => `<svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${icons[name] || icons.activity}</svg>`;
const escape = (value) => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const number = value => typeof value === 'number' && Number.isFinite(value) ? value : null;
const compact = value => number(value) === null ? '—' : new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(value);
const percent = value => number(value) === null ? '—' : `${Math.max(0, Math.min(100, value)).toLocaleString(undefined, { maximumFractionDigits: 1 })}%`;
const post = (type, values = {}) => window.chrome?.webview?.postMessage({ type, ...values });
const pages = ['Overview', 'Privacy', 'Layout', 'Settings', 'Tools', 'Manual'];
const descriptions = {
  Overview: 'Your Codex session, connected to Discord.',
  Privacy: 'Choose what people see. Keep the rest to yourself.',
  Layout: 'Make room for the details that matter.',
  Settings: 'Make Presence work the way you do.',
  Tools: 'Check your connection and keep things running.',
  Manual: 'Choose a recent Codex chat or custom text, then set your timeline.'
};
let current = null;
let shape = '';
let saveShape = '';
let lastPage = '';
let lastFooter = '';
let navigationShape = '';
let engineShape = '';
let breakdownShape = '';
let railShape = '';
let search = '';
const scrollPositions = new Map();
const $ = selector => document.querySelector(selector);
document.getElementById('background-icon').innerHTML = icon('background');

function toast(message) {
  const node = document.createElement('div');
  node.className = 'alert bg-base-100 border border-base-content/10 shadow-lg text-sm';
  node.textContent = message;
  $('#toasts').append(node);
  setTimeout(() => node.remove(), 2500);
}

function preview(state, editor = false) {
  const data = state.snapshot?.preview;
  const idle = !data || !state.previewVisible;
  return `<section class="card card-border bg-base-100"><div class="card-body">
    <div class="flex items-center justify-between gap-3"><h2 class="card-title">Discord preview</h2><span class="badge badge-sm badge-ghost">${editor ? 'Saved output' : 'Live preview'}</span></div>
    ${state.snapshot?.manual_mode ? '<span class="badge badge-ghost badge-sm">Manual presence</span>' : ''}
    <div class="preview"><div class="text-xs muted mb-4">Playing</div>${idle ? '<div class="preview-empty"><p class="font-medium">Nothing to show yet</p><p class="text-xs muted mt-2">Send a new Codex prompt to get started.</p></div>' : `<div class="flex items-start gap-3"><img src="logo.png" class="preview-icon" alt="Codex Presence"><div class="preview-lines secondary min-w-0"><div class="preview-title" data-preview="app_name">${escape(data.app_name)}</div><div data-preview="details">${escape(data.details)}</div><div data-preview="state">${escape(data.state)}</div>${state.snapshot?.manual_mode ? `<div data-manual-timer class="text-xs mt-2">${manualTime(state.snapshot)}</div>` : ''}</div></div>`}</div>
    <p class="text-xs muted leading-relaxed">${editor ? 'This is your saved Discord text. Save your changes to update it.' : 'Text comes directly from the presence engine. Discord controls line wrapping.'}</p>
  </div></section>`;
}

function manualTime(snapshot) {
  const now = Math.floor(Date.now() / 1000), start = snapshot?.manual_start_epoch, end = snapshot?.manual_end_epoch;
  if (!start) return 'Timer starts when Discord connects';
  const seconds = Math.max(0, end ? end - now : now - start);
  const hours = Math.floor(seconds / 3600), minutes = Math.floor(seconds / 60) % 60, remainder = seconds % 60;
  return `${hours ? `${hours}:` : ''}${hours ? String(minutes).padStart(2, '0') : minutes}:${String(remainder).padStart(2, '0')} ${end ? 'remaining' : 'elapsed'}`;
}

function snapshotSessionLabel(state) { return state.snapshot?.manual_session ? 'Selected manual session' : 'Current session'; }

function engine(state) {
  const mode = state.visual;
  const active = state.snapshot?.manual_session || state.snapshot?.sessions?.find(item => item.session_id === state.snapshot?.active_session_id) || state.snapshot?.sessions?.[0];
  const primary = !state.running ? 'Start engine' : state.publicationEnabled ? 'Pause presence' : 'Resume presence';
  return `<section class="card card-border bg-base-100"><div class="card-body engine-card">
    <div class="flex items-center justify-between"><div class="engine-icon">${icon('activity')}</div><span class="badge badge-sm badge-ghost"><span class="status status-xs ${toneClass(mode.tone)}"></span>${escape(mode.tone === 'Success' ? 'Connected' : state.running ? 'Monitoring' : 'Stopped')}</span></div>
    <div><h2>${escape(mode.headline)}</h2><p class="secondary text-sm mt-2 leading-relaxed">${escape(mode.detail)}</p></div>
    ${active ? `<div class="border-l-2 border-base-content/15 pl-3 mt-1"><p class="text-xs muted mb-1">${snapshotSessionLabel(state)}</p><p class="text-sm font-medium truncate" title="${escape(active.project_name)}">${escape(active.project_name || 'Untitled session')}</p><p class="text-xs muted mt-1">${escape(active.model)} · ${escape(active.reasoning_effort || 'Default effort')}</p></div>` : ''}
    <div class="card-actions items-center mt-auto pt-3"><button class="btn btn-primary" data-command="engine-primary" title="${state.monitorOnly && state.running ? 'Publication is disabled in local-only monitoring' : primary}" ${state.busy || state.monitorOnly && state.running ? 'disabled' : ''}>${icon(!state.running || !state.publicationEnabled ? 'play' : 'pause')}${primary}</button>${state.running ? '<button class="btn btn-ghost" data-command="stop">Stop</button>' : ''}<button id="engine-more" class="btn btn-ghost btn-square ml-auto" popovertarget="engine-actions" aria-label="More engine actions" title="More engine actions">${icon('more')}</button></div>
  </div></section>`;
}

function stat(title, value, detail, glyph, progress = null, exact = null) {
  return `<div class="stat" data-stat="${glyph}"><div class="stat-title flex items-center gap-2">${icon(glyph)}${title}</div><div class="stat-value" title="${escape(exact ?? value)}">${value}</div>${progress === null ? `<div class="stat-desc">${detail}</div>` : `<progress class="progress ${progress >= 80 && glyph === 'context' ? 'progress-warning' : ''}" value="${progress}" max="100" aria-label="${title}"></progress><div class="stat-desc">${detail}</div>`}</div>`;
}

function dashboard(state) {
  const snapshot = state.snapshot || {};
  const totals = snapshot.metrics?.totals || {};
  const active = snapshot.manual_session || (snapshot.sessions || []).find(item => item.session_id === snapshot.active_session_id) || snapshot.sessions?.[0];
  const cache = number(totals.cache_hit_ratio) === null ? null : totals.cache_hit_ratio * 100;
  const remaining = number(active?.context_window?.remaining_percent);
  const context = remaining === null ? null : 100 - remaining;
  const input = Math.max(0, totals.input_tokens || 0), cached = Math.min(input, Math.max(0, totals.cached_input_tokens || 0)), output = Math.max(0, totals.output_tokens || 0);
  const total = input + output;
  const breakdown = total ? `<div class="flex h-2 overflow-hidden rounded-full bg-base-300 mt-5" role="img" aria-label="Token breakdown"><span class="bg-info" style="width:${100 * (input - cached) / total}%"></span><span class="bg-success" style="width:${100 * cached / total}%"></span><span class="bg-warning" style="width:${100 * output / total}%"></span></div>` : '';
  const cost = number(totals.known_cost_usd);
  return `<div class="dashboard-top" id="engine-region">${engine(state)}${preview(state)}</div>
    <section class="mt-7"><div class="flex items-center justify-between mb-3"><h2 class="section-caption mb-0!">Session at a glance</h2><button class="btn btn-ghost btn-xs muted" data-command="copy-tokens" title="Copy the full token count">${icon('copy')}Copy tokens</button></div>
    <div class="stats stats-horizontal">${stat('Total tokens', compact(totals.total_tokens), 'Reported by Codex', 'token', null, number(totals.total_tokens)?.toLocaleString())}${stat('Cache hit', percent(cache), 'Reused input tokens', 'cache', cache)}${stat('Context used', percent(context), 'Current session window', 'context', context)}${stat('Known cost', cost === null ? '—' : `$${cost.toFixed(2)}`, cost === null ? 'Pricing data unavailable' : totals.pricing_status === 'partial' ? 'Partial pricing coverage' : 'Available pricing only', 'cost')}</div></section>
    <section class="card card-border bg-base-100 mt-4" id="breakdown-region"><div class="card-body"><div class="flex flex-wrap items-center justify-between gap-3"><h2 class="card-title">Token breakdown</h2><div class="flex gap-2"><span class="badge badge-ghost badge-sm">${escape(snapshot.plan || 'Plan unavailable')}</span><span class="badge badge-ghost badge-sm">${escape(snapshot.speed === 'On' ? 'Fast' : snapshot.speed === 'Off' ? 'Standard' : 'Speed unavailable')}</span></div></div>${breakdown}<div class="grid grid-cols-1 sm:grid-cols-3 gap-4 mt-3">${[['Input', input, 'info'], ['Cached input', cached, 'success'], ['Output', output, 'warning']].map(([name, value, color]) => `<div class="flex items-center justify-between gap-3"><span class="text-xs secondary flex items-center gap-2"><span class="status status-xs status-${color}"></span>${name}</span><span class="font-medium" title="${value.toLocaleString()}">${total ? compact(value) : '—'}</span></div>`).join('')}</div><p class="muted text-xs">Cached input is included in input. The bar separates it to avoid double counting.</p></div></section>
    <div class="mt-7" id="native-content">${state.nodes.map(renderNode).join('')}</div>`;
}

function renderNode(node) {
  if (!node) return '';
  const id = `data-node="${node.id}"`;
  const disabled = node.disabled ? 'disabled' : '';
  const label = escape(node.label || '');
  const children = () => (node.children || []).map(renderNode).join('');
  switch (node.kind) {
    case 'text': return `<div class="native-text" ${id} data-tone="${node.tone}" data-heading="${!!node.heading}">${escape(node.text)}</div>`;
    case 'card': return `<section class="card card-border bg-base-100 native-card" ${id}><div class="card-body"><fieldset class="fieldset p-0 min-w-0"><legend class="fieldset-legend card-title p-0 mb-3">${escape(node.title)}</legend><div class="native-stack">${children()}</div></fieldset></div></section>`;
    case 'row': return `<div class="native-row" ${id} data-kind="${node.layoutField ? 'layout-field' : 'row'}">${children()}</div>`;
    case 'actions': return `<div class="native-actions" ${id}>${children()}</div>`;
    case 'stack': return `<div class="native-stack" ${id}>${children()}</div>`;
    case 'toggle': return `<label class="toggle-row" ${id}><div class="native-stack">${node.children?.length ? children() : `<div class="native-text">${label}</div>`}</div><span class="flex items-center gap-3 shrink-0"><span class="text-xs muted toggle-state">${node.value ? 'On' : 'Off'}</span><input type="checkbox" class="toggle toggle-sm" data-control="${node.id}" aria-label="${label}" ${node.value ? 'checked' : ''} ${disabled}></span></label>`;
    case 'select': return `<div class="native-control" ${id}><select class="select select-sm" data-control="${node.id}" aria-label="${label}" ${disabled}>${node.options.map((item, index) => `<option value="${index}" ${index === node.value ? 'selected' : ''}>${escape(item)}</option>`).join('')}</select><div class="text-error text-xs mt-1" data-feedback="${node.id}" hidden></div></div>`;
    case 'input': return `<div class="native-control" ${id}>${node.multiline ? `<textarea class="textarea" data-control="${node.id}" aria-label="${label}" ${node.readOnly ? 'readonly' : ''} ${disabled}>${escape(node.value)}</textarea>` : `<input class="input input-sm" data-control="${node.id}" aria-label="${label}" type="${label.includes('local date and time') ? 'datetime-local' : 'text'}" value="${escape(node.value)}" ${node.maximum ? `maxlength="${node.maximum}"` : ''} ${node.readOnly ? 'readonly' : ''} ${disabled}>`}<div class="text-error text-xs mt-1" data-feedback="${node.id}" hidden></div></div>`;
    case 'button': return `<button class="btn ${node.primary ? 'btn-primary' : 'btn-sm'}" data-action="${node.id}" ${id} aria-label="${label}" title="${escape(node.tip || node.label)}" ${disabled}>${escape(node.text || node.label)}</button>`;
    case 'collapse': return `<details class="collapse collapse-arrow native-collapse" ${id} ${node.expanded ? 'open' : ''}><summary class="collapse-title" data-expand="${node.id}">${label}</summary><div class="collapse-content">${children()}</div></details>`;
    case 'progress': return `<progress class="progress" ${id} value="${node.value}" max="${node.maximum || 100}" aria-label="${label}"></progress>`;
    default: return children();
  }
}

function editor(state) {
  const sections = [['Appearance', 'Appearance'], ['Activity', 'Behavior'], ['Background', 'Startup'], ['Advanced', 'Advanced']];
  const tabs = state.page === 'Settings' ? `<div class="settings-sections" role="group" aria-label="Settings sections">${sections.map(([key, name]) => `<button class="btn btn-sm ${state.section === key ? 'btn-active' : 'btn-ghost'}" data-section="${key}">${icon(key === 'Appearance' ? 'Settings' : key === 'Activity' ? 'activity' : key === 'Background' ? 'background' : 'Tools')}${name}</button>`).join('')}</div>` : '';
  const rail = ['Privacy', 'Layout', 'Manual'].includes(state.page) ? `<aside class="preview-rail">${preview(state, state.page !== 'Manual')}<section class="card card-border bg-base-100"><div class="card-body"><h2 class="card-title">Your settings stay local</h2><p class="secondary text-sm leading-relaxed">Only enabled presence fields are shared with Discord. Your session files stay on this PC.</p><button class="btn btn-ghost btn-sm justify-between" data-page="${state.page === 'Privacy' ? 'Layout' : 'Privacy'}">${state.page === 'Privacy' ? 'Arrange your presence' : 'Choose visible fields'}${icon('arrow')}</button></div></section></aside>` : '';
  return `${tabs}<div class="editor-layout ${state.page === 'Manual' ? 'manual-editor' : ''}"><div id="native-content">${state.nodes.map(renderNode).join('')}<p id="search-empty" class="muted p-8 text-center" hidden>No matching settings in this section.</p></div>${rail}</div>`;
}

function toneClass(tone) { return ({ Success: 'status-success', Warning: 'status-warning', Danger: 'status-error', Accent: 'status-info' })[tone] || 'status-neutral'; }
function structural(nodes) { return nodes.map(node => `${node.id}:${node.kind}:${node.options?.join('|') || ''}:${structural(node.children || [])}`).join(';'); }

function updateDashboard(state) {
  const snapshot = state.snapshot || {};
  const active = snapshot.manual_session || snapshot.sessions?.find(item => item.session_id === snapshot.active_session_id) || snapshot.sessions?.[0];
  const nextEngine = JSON.stringify([state.visual, state.running, state.publicationEnabled, state.busy, snapshot.preview, state.previewVisible, active?.project_name, active?.model, active?.reasoning_effort]);
  if (nextEngine !== engineShape && !$('#engine-actions').matches(':popover-open')) {
    $('#engine-region').innerHTML = engine(state) + preview(state);
    engineShape = nextEngine;
  }
  const totals = snapshot.metrics?.totals || {};
  const remaining = number(active?.context_window?.remaining_percent);
  const values = { token: compact(totals.total_tokens), cache: percent(number(totals.cache_hit_ratio) === null ? null : totals.cache_hit_ratio * 100), context: percent(remaining === null ? null : 100 - remaining), cost: number(totals.known_cost_usd) === null ? '—' : `$${totals.known_cost_usd.toFixed(2)}` };
  for (const [key, value] of Object.entries(values)) {
    const stat = document.querySelector(`[data-stat=${key}]`);
    if (stat) { stat.querySelector('.stat-value').textContent = value; const progress = stat.querySelector('progress'); if (progress) progress.value = key === 'cache' ? (totals.cache_hit_ratio || 0) * 100 : remaining === null ? 0 : 100 - remaining; }
  }
  const nextBreakdown = JSON.stringify([totals.input_tokens, totals.cached_input_tokens, totals.output_tokens, snapshot.plan, snapshot.speed]);
  if (nextBreakdown !== breakdownShape) {
    const template = document.createElement('template'); template.innerHTML = dashboard(state);
    $('#breakdown-region').innerHTML = template.content.querySelector('#breakdown-region').innerHTML;
    breakdownShape = nextBreakdown;
  }
}

function patchNodes(nodes) {
  for (const node of nodes) {
    const root = document.querySelector(`[data-node="${node.id}"]`);
    const input = root?.matches('input,select,textarea') ? root : root?.querySelector(`[data-control="${node.id}"]`);
    if (input) {
      input.disabled = !!node.disabled;
      if (input.type === 'checkbox') { input.checked = !!node.value; const text = root.querySelector('.toggle-state'); if (text) text.textContent = node.value ? 'On' : 'Off'; }
      else if (input !== document.activeElement) { if (input.tagName === 'SELECT') input.selectedIndex = node.value; else input.value = node.value ?? ''; }
      const feedback = root.querySelector('[data-feedback]');
      if (feedback) { const newError = !!node.error && input.getAttribute('aria-invalid') !== 'true'; feedback.hidden = !node.error; feedback.textContent = node.error || ''; input.setAttribute('aria-invalid', node.error ? 'true' : 'false'); if (newError) { input.focus(); input.scrollIntoView({ block: 'center' }); } }
    } else if (root && node.kind === 'text' && root.textContent !== node.text) root.textContent = node.text;
    else if (root && node.kind === 'button') { root.disabled = !!node.disabled; if (root.textContent !== node.text) root.textContent = node.text || node.label; }
    else if (root && node.kind === 'progress') root.value = node.value;
    patchNodes(node.children || []);
  }
}

function applySearch() {
  const query = search.toLocaleLowerCase().trim();
  const cards = [...document.querySelectorAll('#native-content > .native-card')];
  for (const card of cards) card.hidden = !!query && !card.textContent.toLocaleLowerCase().includes(query);
  const empty = $('#search-empty'); if (empty) empty.hidden = !query || cards.some(card => !card.hidden);
}

window.receive = state => {
  if (state.type === 'resizing') { document.documentElement.toggleAttribute('data-resizing', state.active); return; }
  current = state;
  document.documentElement.dataset.theme = state.light ? 'nord' : 'dim';
  document.documentElement.dataset.style = state.style || 'minimal';
  for (const [key, value] of Object.entries(state.theme || {})) document.documentElement.style.setProperty(key, value);
  const changingPage = state.page !== lastPage;
  if (changingPage) { scrollPositions.set(lastPage, $('#scroll').scrollTop); search = ''; }
  const nextShape = `${state.page}:${state.section}:${structural(state.nodes)}`;
  const nextNavigation = `${state.page}:${state.dirtyTabs.join('|')}`;
  if (nextNavigation !== navigationShape) {
    const focused = !!document.activeElement?.closest('#navigation');
    $('#navigation').innerHTML = pages.map(name => `<li><button class="${state.page === name ? 'menu-active' : ''}" aria-label="${name}${state.dirtyTabs.includes(name) ? ', unsaved changes' : ''}" aria-current="${state.page === name ? 'page' : 'false'}" title="${name}" data-page="${name}">${icon(name)}<span class="nav-copy">${name}</span>${state.dirtyTabs.includes(name) ? '<span class="status status-xs ml-auto" aria-hidden="true"></span>' : ''}</button></li>`).join('');
    if ($('#engine-actions').matches(':popover-open')) $('#engine-actions').hidePopover();
    if (focused) document.querySelector(`#navigation [data-page=${state.page}]`)?.focus();
    navigationShape = nextNavigation;
  }
  $('#sidebar-status').className = `status status-sm ${toneClass(state.visual.tone)}`;
  $('#sidebar-state').textContent = state.visual.headline;
  if (nextShape !== shape) {
    const heading = `<header class="page-heading"><div><h1>${state.page === 'Overview' ? 'Your workspace' : state.page === 'Manual' ? 'Manual presence' : state.page}</h1><p class="secondary text-sm">${descriptions[state.page]}</p></div>${state.page !== 'Overview' ? `<label class="input input-sm settings-search bg-base-100 border-base-content/15">${icon('search')}<input id="setting-search" type="search" placeholder="Find in this section" aria-label="Find settings" value="${escape(search)}"></label>` : '<span class="badge badge-ghost badge-sm">Desktop companion</span>'}</header>`;
    const oldScroll = $('#scroll').scrollTop;
    $('#page').innerHTML = heading + (state.page === 'Overview' ? dashboard(state) : editor(state));
    $('#scroll').scrollTop = changingPage ? scrollPositions.get(state.page) || 0 : oldScroll;
    shape = nextShape;
    applySearch();
  }
  patchNodes(state.nodes);
  document.querySelectorAll('[data-manual-timer]').forEach(node => { node.textContent = manualTime(state.snapshot); });
  if (state.page === 'Overview') updateDashboard(state);
  if (['Privacy', 'Layout', 'Manual'].includes(state.page)) {
    const nextRail = JSON.stringify([state.snapshot?.preview, state.previewVisible, state.snapshot?.manual_mode]);
    if (nextRail !== railShape) { const rail = document.querySelector('.preview-rail > .card'); if (rail) { const template = document.createElement('template'); template.innerHTML = preview(state, state.page !== 'Manual'); rail.replaceWith(template.content.firstElementChild); } railShape = nextRail; }
  }
  const nextSave = `${state.page}:${state.dirty}:${state.busy}:${state.canSave}`;
  if (nextSave !== saveShape) {
    $('#save-strip').hidden = !state.canSave;
    $('#save-strip').innerHTML = `<div class="text-xs ${state.dirty ? 'secondary' : 'muted'}">${state.dirty ? 'You have unsaved changes' : 'All changes saved'}<span class="hidden sm:inline muted"> · Changes stay with each tab</span></div><div class="flex gap-2 shrink-0"><button class="btn btn-ghost" data-command="reload" ${state.busy ? 'disabled' : ''}>Reload</button><button class="btn btn-primary" data-command="save" ${state.busy ? 'disabled' : ''}>${state.busy ? 'Working…' : state.page === 'Manual' ? 'Save profile' : 'Save changes'}</button></div>`;
    saveShape = nextSave;
  }
  $('#footer-dot').className = `status status-xs ${toneClass(state.visual.tone)}`;
  $('#footer-text').textContent = state.footer;
  if (lastFooter !== state.footer && /saved\.|reloaded\.|created\.|copied/i.test(state.footer)) toast(state.footer);
  lastFooter = state.footer;
  const decision = $('#decision');
  if (state.decision) { $('#decision-body').innerHTML = `<div class="native-stack gap-4">${state.decision.map(renderNode).join('')}</div>`; if (!decision.open) decision.showModal(); }
  else if (decision.open) decision.close();
  lastPage = state.page;
};

document.addEventListener('click', event => {
  const button = event.target.closest('button');
  if (!button || button.disabled) return;
  if (button.dataset.page) post('navigate', { page: button.dataset.page });
  else if (button.dataset.section) post('section', { section: button.dataset.section });
  else if (button.dataset.action) post('invoke', { id: button.dataset.action });
  else if (button.dataset.command) post('command', { name: button.dataset.command });
  else return;
  if (button.dataset.command && !['background', 'copy-tokens', 'exit'].includes(button.dataset.command)) button.disabled = true;
});
document.addEventListener('change', event => {
  const control = event.target.closest('[data-control]');
  if (!control) return;
  post('edit', { id: control.dataset.control, value: control.type === 'checkbox' ? control.checked : control.tagName === 'SELECT' ? control.selectedIndex : control.value });
});
document.addEventListener('input', event => {
  if (event.target.id === 'setting-search') { search = event.target.value; applySearch(); return; }
  const control = event.target.closest('input[data-control]:not([type=checkbox]),textarea[data-control]');
  if (control) post('edit', { id: control.dataset.control, value: control.value });
});
document.addEventListener('contextmenu', event => {
  const stat = event.target.closest('[data-stat]');
  if (stat) { event.preventDefault(); post('command', { name: 'copy-stat', field: stat.dataset.stat }); }
});
document.addEventListener('toggle', event => {
  if (event.target.matches('details[data-node]')) post('expand', { id: event.target.dataset.node, value: event.target.open });
}, true);
$('#decision').addEventListener('cancel', event => { event.preventDefault(); post('command', { name: 'cancel-decision' }); });
document.addEventListener('keydown', event => {
  if (event.ctrlKey && event.key.toLowerCase() === 's') { event.preventDefault(); post('command', { name: 'save' }); }
  else if (event.ctrlKey && /^[1-6]$/.test(event.key)) { event.preventDefault(); post('navigate', { page: pages[Number(event.key) - 1] }); }
  else if (event.key === 'Escape') document.querySelectorAll('details.dropdown[open]').forEach(menu => menu.open = false);
  else if (event.target.closest('#navigation') && ['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) {
    event.preventDefault(); const index = pages.indexOf(current.page); const next = event.key === 'Home' ? 0 : event.key === 'End' ? pages.length - 1 : (index + (event.key === 'ArrowDown' ? 1 : pages.length - 1)) % pages.length; post('navigate', { page: pages[next] });
  }
});
window.chrome?.webview?.addEventListener('message', event => window.receive(event.data));
post('ready');

const engineActions = $('#engine-actions');
function positionEngineActions() {
  if (!engineActions.matches(':popover-open')) return;
  const button = $('#engine-more');
  if (!button) { engineActions.hidePopover(); return; }
  const bounds = button.getBoundingClientRect();
  const width = engineActions.offsetWidth, height = engineActions.offsetHeight;
  const left = Math.max(8, Math.min(innerWidth - width - 8, bounds.right - width));
  const below = bounds.bottom + 8;
  const top = below + height <= innerHeight - 8 ? below : Math.max(8, bounds.top - height - 8);
  Object.assign(engineActions.style, { left: `${left}px`, top: `${top}px` });
}
engineActions.addEventListener('toggle', positionEngineActions);
engineActions.addEventListener('click', event => { if (event.target.closest('button')) engineActions.hidePopover(); });
window.addEventListener('resize', positionEngineActions);
$('#scroll').addEventListener('scroll', () => { if (engineActions.matches(':popover-open')) engineActions.hidePopover(); }, { passive: true });
