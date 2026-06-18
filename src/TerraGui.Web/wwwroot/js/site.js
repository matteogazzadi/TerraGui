/* ============================================================
   TerraGui – frontend logic
   ============================================================ */

'use strict';

// ---------- State -------------------------------------------
let parsedVariables = [];
let selectedFiles   = [];

// ---------- DOM refs ----------------------------------------
const dropZone        = document.getElementById('dropZone');
const fileInput       = document.getElementById('fileInput');
const selectFilesBtn  = document.getElementById('selectFilesBtn');
const clearFilesBtn   = document.getElementById('clearFilesBtn');
const fileListEl      = document.getElementById('fileList');
const fileListItems   = document.getElementById('fileListItems');
const parseBtn        = document.getElementById('parseBtn');
const parseSpinner    = document.getElementById('parseSpinner');
const warningsContainer = document.getElementById('warningsContainer');
const warningsList    = document.getElementById('warningsList');
const formArea        = document.getElementById('formArea');
const variablesForm   = document.getElementById('variablesForm');
const varCount        = document.getElementById('varCount');
const downloadBtn     = document.getElementById('downloadBtn');
const copyBtn         = document.getElementById('copyBtn');
const previewTextarea = document.getElementById('previewTextarea');
const validationStatus = document.getElementById('validationStatus');
const expandAllBtn    = document.getElementById('expandAllBtn');
const collapseAllBtn  = document.getElementById('collapseAllBtn');
const howItWorks      = document.getElementById('howItWorks');

// ---------- File drop / select ------------------------------
dropZone.addEventListener('dragover', e => { e.preventDefault(); dropZone.classList.add('tg-drag-over'); });
dropZone.addEventListener('dragleave', () => dropZone.classList.remove('tg-drag-over'));
dropZone.addEventListener('drop', e => {
  e.preventDefault();
  dropZone.classList.remove('tg-drag-over');
  addFiles([...e.dataTransfer.files]);
});
dropZone.addEventListener('click', e => { if (e.target !== selectFilesBtn) fileInput.click(); });
dropZone.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); fileInput.click(); } });
selectFilesBtn.addEventListener('click', e => { e.stopPropagation(); fileInput.click(); });
fileInput.addEventListener('change', () => { addFiles([...fileInput.files]); fileInput.value = ''; });
clearFilesBtn.addEventListener('click', clearFiles);

function addFiles(files) {
  files.forEach(f => {
    if (!selectedFiles.find(x => x.name === f.name)) selectedFiles.push(f);
  });
  renderFileList();
}

function clearFiles() {
  selectedFiles = [];
  renderFileList();
  resetForm();
}

function renderFileList() {
  if (selectedFiles.length === 0) {
    fileListEl.classList.add('d-none');
    return;
  }
  fileListEl.classList.remove('d-none');
  fileListItems.innerHTML = selectedFiles.map(f => {
    const badge = fileBadge(f.name);
    return `<li class="list-group-item d-flex align-items-center gap-2">
      <span class="tg-file-badge ${badge.cls}">${badge.label}</span>
      <span class="fw-medium">${escHtml(f.name)}</span>
      <span class="text-muted ms-auto" style="font-size:.8rem">${formatBytes(f.size)}</span>
    </li>`;
  }).join('');
}

function fileBadge(name) {
  const n = name.toLowerCase();
  if (n.endsWith('variables.tf')) return { label: 'vars', cls: 'badge-vars' };
  if (n.endsWith('.tfvars.example') || n.endsWith('.tfvars')) return { label: 'tfvars', cls: 'badge-tfvars' };
  return { label: 'tf', cls: 'badge-main' };
}

function formatBytes(b) {
  if (b < 1024) return b + ' B';
  if (b < 1024 * 1024) return (b / 1024).toFixed(1) + ' KB';
  return (b / (1024 * 1024)).toFixed(1) + ' MB';
}

// ---------- Parse -------------------------------------------
parseBtn.addEventListener('click', parseFiles);

async function parseFiles() {
  if (selectedFiles.length === 0) return;

  parseSpinner.classList.remove('d-none');
  formArea.classList.add('d-none');
  warningsContainer.classList.add('d-none');
  parseBtn.disabled = true;

  const formData = new FormData();
  selectedFiles.forEach(f => formData.append('files', f, f.name));

  try {
    const res = await fetch('/api/terraform/parse', { method: 'POST', body: formData });
    const data = await res.json();

    if (!res.ok) {
      showError(data.error || 'Parse failed');
      return;
    }

    if (data.warnings && data.warnings.length > 0) {
      warningsList.innerHTML = '<strong>Warnings:</strong><ul class="mb-0 mt-1">' +
        data.warnings.map(w => `<li>${escHtml(w)}</li>`).join('') + '</ul>';
      warningsContainer.classList.remove('d-none');
    }

    parsedVariables = data.variables || [];
    renderForm(parsedVariables);

  } catch (err) {
    showError('Network error: ' + err.message);
  } finally {
    parseSpinner.classList.add('d-none');
    parseBtn.disabled = false;
  }
}

// ---------- Form rendering ----------------------------------
function renderForm(variables) {
  if (variables.length === 0) {
    showError('No variables found. Please upload a valid variables.tf file.');
    return;
  }

  varCount.textContent = variables.length;
  variablesForm.innerHTML = '';

  variables.forEach((v, idx) => {
    const card = buildVariableCard(v, idx);
    variablesForm.appendChild(card);
  });

  formArea.classList.remove('d-none');
  howItWorks.classList.add('d-none');
  downloadBtn.disabled = false;
  copyBtn.disabled = false;

  // trigger initial preview
  updatePreview();

  // animate in
  requestAnimationFrame(() => {
    document.querySelectorAll('.tg-var-card').forEach((c, i) => {
      c.style.opacity = '0';
      c.style.transform = 'translateY(12px)';
      setTimeout(() => {
        c.style.transition = 'opacity .25s ease, transform .25s ease';
        c.style.opacity = '1';
        c.style.transform = 'none';
      }, i * 35);
    });
  });
}

function buildVariableCard(v, idx) {
  const card = document.createElement('div');
  card.className = 'tg-var-card' + (v.required ? ' tg-required' : '');
  card.dataset.varName = v.name;

  const collapseId = `vc-${idx}`;

  // determine initial value: exampleValue > defaultValue > ''
  const initVal = v.exampleValue != null ? v.exampleValue
                : v.defaultValue  != null ? v.defaultValue
                : '';

  const prefilledIcon = v.exampleValue != null
    ? `<i class="bi bi-check-circle-fill tg-prefilled-icon" title="Pre-filled from tfvars.example"></i>`
    : '';

  card.innerHTML = `
    <button class="tg-var-header" type="button"
            data-bs-toggle="collapse" data-bs-target="#${collapseId}"
            aria-expanded="true" aria-controls="${collapseId}">
      <span class="tg-var-name">${escHtml(v.name)}</span>
      <span class="tg-var-type-badge">${escHtml(v.type)}</span>
      ${v.required ? '<span class="tg-required-star" title="Required">*</span>' : ''}
      ${v.sensitive ? '<i class="bi bi-shield-lock-fill tg-sensitive-icon" title="Sensitive"></i>' : ''}
      ${prefilledIcon}
      <i class="bi bi-chevron-down tg-var-chevron"></i>
    </button>
    <div class="collapse show" id="${collapseId}">
      <div class="tg-var-body">
        ${v.description ? `<p class="tg-var-description"><i class="bi bi-info-circle me-1"></i>${escHtml(v.description)}</p>` : ''}
        ${buildInputHtml(v, idx, initVal)}
        ${v.validations && v.validations.length > 0
          ? v.validations.map(val => `<div class="tg-validation-hint"><i class="bi bi-exclamation-triangle me-1"></i>${escHtml(val.errorMessage)}</div>`).join('')
          : ''}
        <div class="tg-field-error" id="err-${idx}"></div>
      </div>
    </div>`;

  // wire up input events
  setTimeout(() => wireInputEvents(card, v, idx), 0);

  return card;
}

function buildInputHtml(v, idx, initVal) {
  const inputId = `var-${idx}`;
  const t = v.inputType;

  if (t === 'text' || t === 'number') {
    return `<input type="${t}" class="form-control tg-var-input font-monospace"
             id="${inputId}" data-var="${escHtml(v.name)}"
             value="${escHtml(stripQuotes(initVal))}"
             placeholder="${v.required ? 'Required' : 'Optional'}"
             autocomplete="off" />`;
  }

  if (t === 'password') {
    return `<div class="input-group">
      <input type="password" class="form-control tg-var-input font-monospace"
             id="${inputId}" data-var="${escHtml(v.name)}"
             value="${escHtml(stripQuotes(initVal))}"
             placeholder="Sensitive — required"
             autocomplete="off" />
      <button class="btn btn-outline-secondary" type="button"
              onclick="togglePassword('${inputId}')">
        <i class="bi bi-eye" id="eye-${inputId}"></i>
      </button>
    </div>`;
  }

  if (t === 'bool') {
    const checked = ['true', '1', 'yes'].includes(stripQuotes(initVal).toLowerCase());
    return `<div class="form-check form-switch">
      <input class="form-check-input tg-var-input" type="checkbox"
             role="switch" id="${inputId}" data-var="${escHtml(v.name)}"
             ${checked ? 'checked' : ''} />
      <label class="form-check-label" for="${inputId}" id="bool-label-${inputId}">
        ${checked ? 'true' : 'false'}
      </label>
    </div>`;
  }

  if (t === 'list' || t === 'set') {
    const items = parseListInit(initVal);
    const rows  = items.length > 0 ? items : [''];
    return `<div class="tg-list-editor" id="${inputId}" data-var="${escHtml(v.name)}">
      ${rows.map((item, i) => listRowHtml(inputId, i, item, v)).join('')}
      <button type="button" class="btn btn-sm btn-outline-secondary tg-add-row-btn"
              onclick="addListRow('${inputId}', ${JSON.stringify(v)})">
        <i class="bi bi-plus-lg me-1"></i>Add item
      </button>
    </div>`;
  }

  if (t === 'map') {
    const pairs = parseMapInit(initVal);
    const rows  = pairs.length > 0 ? pairs : [['', '']];
    return `<div class="tg-map-editor" id="${inputId}" data-var="${escHtml(v.name)}">
      ${rows.map((p, i) => mapRowHtml(inputId, i, p[0], p[1])).join('')}
      <button type="button" class="btn btn-sm btn-outline-secondary tg-add-row-btn"
              onclick="addMapRow('${inputId}')">
        <i class="bi bi-plus-lg me-1"></i>Add pair
      </button>
    </div>`;
  }

  if (t === 'object') {
    const attrs = v.objectAttributes || {};
    const initObj = parseObjectInit(initVal);
    return `<div class="tg-object-editor" id="${inputId}" data-var="${escHtml(v.name)}">
      ${Object.entries(attrs).map(([attrName, attrType], i) => {
        const attrVal = initObj[attrName] || '';
        return `<div>
          <div class="tg-obj-attr-label">${escHtml(attrName)} <span class="text-muted">(${escHtml(attrType)})</span></div>
          <input type="${attrType === 'number' ? 'number' : attrType === 'bool' ? 'checkbox' : 'text'}"
                 class="form-control form-control-sm font-monospace tg-obj-attr"
                 data-attr="${escHtml(attrName)}" value="${escHtml(stripQuotes(attrVal))}"
                 placeholder="${escHtml(attrName)}" />
        </div>`;
      }).join('')}
    </div>`;
  }

  // any / fallback → textarea
  return `<textarea class="form-control tg-var-input font-monospace"
           id="${inputId}" data-var="${escHtml(v.name)}"
           rows="3" placeholder='Raw HCL value'>${escHtml(initVal)}</textarea>`;
}

function listRowHtml(editorId, idx, value, v) {
  const isNumeric = v && v.elementType === 'number';
  return `<div class="tg-list-row" data-row="${idx}">
    <input type="${isNumeric ? 'number' : 'text'}" class="form-control form-control-sm font-monospace tg-list-item"
           value="${escHtml(stripQuotes(value))}" placeholder="Item ${idx + 1}" />
    <button type="button" class="btn btn-sm btn-outline-danger"
            onclick="removeListRow(this)"><i class="bi bi-dash-lg"></i></button>
  </div>`;
}

function mapRowHtml(editorId, idx, key, val) {
  return `<div class="tg-map-row" data-row="${idx}">
    <input type="text" class="form-control form-control-sm font-monospace tg-map-key"
           value="${escHtml(stripQuotes(key))}" placeholder="key" />
    <input type="text" class="form-control form-control-sm font-monospace tg-map-val"
           value="${escHtml(stripQuotes(val))}" placeholder="value" />
    <button type="button" class="btn btn-sm btn-outline-danger"
            onclick="removeMapRow(this)"><i class="bi bi-dash-lg"></i></button>
  </div>`;
}

// ---------- Dynamic row add/remove --------------------------
function addListRow(editorId, v) {
  const editor = document.getElementById(editorId);
  const rows   = editor.querySelectorAll('.tg-list-row');
  const newIdx = rows.length;
  const addBtn = editor.querySelector('.tg-add-row-btn');
  const div    = document.createElement('div');
  div.innerHTML = listRowHtml(editorId, newIdx, '', v);
  editor.insertBefore(div.firstElementChild, addBtn);
  updatePreview();
}

function removeListRow(btn) {
  btn.closest('.tg-list-row').remove();
  updatePreview();
}

function addMapRow(editorId) {
  const editor = document.getElementById(editorId);
  const rows   = editor.querySelectorAll('.tg-map-row');
  const addBtn = editor.querySelector('.tg-add-row-btn');
  const div    = document.createElement('div');
  div.innerHTML = mapRowHtml(editorId, rows.length, '', '');
  editor.insertBefore(div.firstElementChild, addBtn);
  updatePreview();
}

function removeMapRow(btn) {
  btn.closest('.tg-map-row').remove();
  updatePreview();
}

window.addListRow   = addListRow;
window.removeListRow = removeListRow;
window.addMapRow    = addMapRow;
window.removeMapRow = removeMapRow;

// ---------- Password toggle ---------------------------------
function togglePassword(id) {
  const inp = document.getElementById(id);
  const eye = document.getElementById('eye-' + id);
  if (inp.type === 'password') { inp.type = 'text'; eye.className = 'bi bi-eye-slash'; }
  else { inp.type = 'password'; eye.className = 'bi bi-eye'; }
}
window.togglePassword = togglePassword;

// ---------- Wire input events -------------------------------
function wireInputEvents(card, v, idx) {
  card.querySelectorAll('input, textarea, select').forEach(el => {
    el.addEventListener('input', () => {
      updatePreview();
      updateCardState(card, v, idx);
    });
    el.addEventListener('change', () => {
      updatePreview();
      updateCardState(card, v, idx);
      if (el.type === 'checkbox') {
        const lbl = document.getElementById('bool-label-var-' + idx);
        if (lbl) lbl.textContent = el.checked ? 'true' : 'false';
      }
    });
  });
}

function updateCardState(card, v, idx) {
  const val = getVariableValue(v.name, v.inputType);
  const errEl = document.getElementById('err-' + idx);
  card.classList.remove('tg-required', 'tg-has-error', 'tg-filled');

  if (v.required && !val) {
    card.classList.add('tg-has-error');
    if (errEl) { errEl.textContent = 'This field is required.'; errEl.classList.add('visible'); }
  } else if (val) {
    card.classList.add('tg-filled');
    if (errEl) { errEl.textContent = ''; errEl.classList.remove('visible'); }
  } else {
    card.classList.add('tg-required');
    if (errEl) errEl.classList.remove('visible');
  }
}

// ---------- Expand / Collapse all ---------------------------
expandAllBtn?.addEventListener('click', () => {
  document.querySelectorAll('.tg-var-card .collapse').forEach(el => {
    if (!el.classList.contains('show')) new bootstrap.Collapse(el, { show: true });
  });
});

collapseAllBtn?.addEventListener('click', () => {
  document.querySelectorAll('.tg-var-card .collapse.show').forEach(el => {
    new bootstrap.Collapse(el, { hide: true });
  });
});

// ---------- Collect values ----------------------------------
function collectValues() {
  const values = {};
  parsedVariables.forEach(v => {
    const val = getVariableValue(v.name, v.inputType);
    if (val !== null && val !== undefined) values[v.name] = val;
  });
  return values;
}

function getVariableValue(varName, inputType) {
  // Find by data-var attribute
  const el = document.querySelector(`[data-var="${CSS.escape(varName)}"]`);
  if (!el) return '';

  if (el.tagName === 'INPUT' && el.type === 'checkbox') return el.checked ? 'true' : 'false';

  if (inputType === 'list' || inputType === 'set') {
    const items = [...el.querySelectorAll('.tg-list-item')]
      .map(i => i.value.trim()).filter(i => i !== '');
    return JSON.stringify(items); // will be formatted by generator
  }

  if (inputType === 'map') {
    const pairs = [...el.querySelectorAll('.tg-map-row')].map(row => {
      const k = row.querySelector('.tg-map-key')?.value.trim() || '';
      const v2 = row.querySelector('.tg-map-val')?.value.trim() || '';
      return [k, v2];
    }).filter(([k]) => k !== '');
    const obj = {};
    pairs.forEach(([k, v2]) => obj[k] = v2);
    return JSON.stringify(obj);
  }

  if (inputType === 'object') {
    const obj = {};
    el.querySelectorAll('.tg-obj-attr').forEach(attr => {
      const name = attr.dataset.attr;
      const val  = attr.type === 'checkbox' ? (attr.checked ? 'true' : 'false') : attr.value.trim();
      if (name && val !== '') obj[name] = val;
    });
    return JSON.stringify(obj);
  }

  return el.value;
}

// ---------- Preview update ----------------------------------
function updatePreview() {
  const values = collectValues();
  const lines  = [];

  lines.push('# Generated by TerraGui');
  lines.push(`# ${new Date().toISOString().replace('T', ' ').substring(0, 19)} UTC`);
  lines.push('');

  let allRequiredFilled = true;

  parsedVariables.forEach(v => {
    const raw = values[v.name] || '';
    const hasVal = raw !== '' && raw !== '[]' && raw !== '{}';

    if (v.required && !hasVal) { allRequiredFilled = false; return; }
    if (!hasVal) return; // skip blanks / use defaults

    if (v.description) lines.push(`# ${v.description}`);
    if (v.sensitive) lines.push('# sensitive');
    lines.push(`${v.name} = ${formatValueForPreview(v, raw)}`);
    lines.push('');
  });

  previewTextarea.value = lines.join('\n');

  // validation badge
  if (validationStatus) {
    const reqCount = parsedVariables.filter(v => v.required).length;
    const filled   = parsedVariables.filter(v => {
      if (!v.required) return true;
      const raw = values[v.name] || '';
      return raw !== '' && raw !== '[]' && raw !== '{}';
    }).length;
    if (allRequiredFilled) {
      validationStatus.className = 'badge bg-success';
      validationStatus.textContent = 'Ready';
    } else {
      const missing = reqCount - (filled - parsedVariables.filter(v => !v.required).length);
      validationStatus.className = 'badge bg-warning text-dark';
      validationStatus.textContent = `${Math.max(0, parsedVariables.filter(v => v.required).length - parsedVariables.filter(v => {
        if (!v.required) return false;
        const r = values[v.name] || '';
        return r !== '' && r !== '[]' && r !== '{}';
      }).length)} required missing`;
    }
  }
}

function formatValueForPreview(v, raw) {
  const t = v.inputType;
  if (t === 'bool') return raw === 'true' ? 'true' : 'false';
  if (t === 'number') return raw;
  if (t === 'list' || t === 'set') {
    try {
      const arr = JSON.parse(raw);
      if (!Array.isArray(arr)) return raw;
      const inner = v.elementType === 'number' ? arr.join(', ') : arr.map(x => `"${x}"`).join(', ');
      return `[${inner}]`;
    } catch { return raw; }
  }
  if (t === 'map') {
    try {
      const obj = JSON.parse(raw);
      const pairs = Object.entries(obj).map(([k, vv]) => `  ${k} = "${vv}"`).join('\n');
      return `{\n${pairs}\n}`;
    } catch { return raw; }
  }
  if (t === 'object') {
    try {
      const obj = JSON.parse(raw);
      const pairs = Object.entries(obj).map(([k, vv]) => `  ${k} = "${vv}"`).join('\n');
      return `{\n${pairs}\n}`;
    } catch { return raw; }
  }
  if (t === 'password' || t === 'text') {
    const s = stripQuotes(raw);
    return `"${s.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"`;
  }
  return raw;
}

// ---------- Download ----------------------------------------
downloadBtn?.addEventListener('click', async () => {
  const payload = {
    variables: parsedVariables,
    values: collectValues(),
    download: true
  };
  try {
    const res = await fetch('/api/terraform/generate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) {
      const d = await res.json();
      alert('Error: ' + (d.error || 'Generation failed'));
      return;
    }
    const blob = await res.blob();
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = 'terraform.tfvars';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
    flashButton(downloadBtn, '<i class="bi bi-check-lg me-2"></i>Downloaded!', 'btn-success');
  } catch (err) {
    alert('Error: ' + err.message);
  }
});

// ---------- Copy to clipboard -------------------------------
copyBtn?.addEventListener('click', async () => {
  const text = previewTextarea.value;
  if (!text) return;
  try {
    await navigator.clipboard.writeText(text);
    flashButton(copyBtn, '<i class="bi bi-check-lg me-2"></i>Copied!', 'btn-success');
  } catch {
    previewTextarea.select();
    document.execCommand('copy');
    flashButton(copyBtn, '<i class="bi bi-check-lg me-2"></i>Copied!', 'btn-success');
  }
});

function flashButton(btn, html, cls) {
  const orig = btn.innerHTML;
  const origCls = [...btn.classList].find(c => c.startsWith('btn-'));
  btn.innerHTML = html;
  btn.classList.replace(origCls, cls);
  setTimeout(() => { btn.innerHTML = orig; if (origCls) btn.classList.replace(cls, origCls); }, 1800);
}

// ---------- Helpers -----------------------------------------
function resetForm() {
  formArea.classList.add('d-none');
  warningsContainer.classList.add('d-none');
  parsedVariables = [];
  variablesForm.innerHTML = '';
  previewTextarea.value = '';
  downloadBtn.disabled = true;
  copyBtn.disabled = true;
  howItWorks?.classList.remove('d-none');
}

function showError(msg) {
  warningsList.innerHTML = `<strong>Error:</strong> ${escHtml(msg)}`;
  warningsContainer.classList.remove('d-none');
  warningsContainer.querySelector('.alert').className = 'alert alert-danger';
}

function escHtml(s) {
  if (s == null) return '';
  return String(s)
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#039;');
}

function stripQuotes(s) {
  if (!s) return '';
  s = String(s).trim();
  if (s.startsWith('"') && s.endsWith('"') && s.length >= 2) return s.slice(1, -1);
  return s;
}

function parseListInit(raw) {
  if (!raw) return [];
  raw = raw.trim();
  if (raw.startsWith('[') && raw.endsWith(']')) {
    try {
      const arr = JSON.parse(raw);
      if (Array.isArray(arr)) return arr.map(String);
    } catch {}
    // manual parse
    return raw.slice(1, -1).split(',').map(s => s.trim().replace(/^"|"$/g, '')).filter(Boolean);
  }
  if (raw.includes('\n')) return raw.split('\n').map(s => s.trim()).filter(Boolean);
  if (raw.includes(',')) return raw.split(',').map(s => s.trim()).filter(Boolean);
  return raw ? [raw] : [];
}

function parseMapInit(raw) {
  if (!raw) return [];
  raw = raw.trim();
  if (raw.startsWith('{') && raw.endsWith('}')) {
    const inner = raw.slice(1, -1).trim();
    const lines = inner.split(/[\n,]+/).map(s => s.trim()).filter(Boolean);
    return lines.map(line => {
      const eq = line.indexOf('=');
      if (eq < 0) return [line, ''];
      return [line.slice(0, eq).trim().replace(/^"|"$/g, ''), line.slice(eq + 1).trim().replace(/^"|"$/g, '')];
    });
  }
  return [];
}

function parseObjectInit(raw) {
  if (!raw) return {};
  raw = raw.trim();
  if (raw.startsWith('{')) {
    const inner = raw.slice(1, raw.length - 1).trim();
    const result = {};
    inner.split(/[\n,]+/).forEach(line => {
      const eq = line.indexOf('=');
      if (eq > 0) {
        result[line.slice(0, eq).trim()] = line.slice(eq + 1).trim().replace(/^"|"$/g, '');
      }
    });
    return result;
  }
  return {};
}
