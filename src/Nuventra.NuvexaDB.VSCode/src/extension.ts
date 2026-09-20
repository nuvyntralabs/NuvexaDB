import * as vscode from "vscode";
import { spawn } from "child_process";
import { existsSync } from "fs";
import { dirname, join } from "path";
import { cliErrorMessage, isEncryptedLockResponse } from "./cliJson";

const keys = new Map<string, string>();
let extensionRoot = "";

export function activate(context: vscode.ExtensionContext): void {
  extensionRoot = context.extensionPath;
  const workbench = new NuvexaWorkbench();
  context.subscriptions.push(workbench);
  context.subscriptions.push(
    vscode.window.registerWebviewViewProvider("nuvexadb.browser", new NuvexaBrowserViewProvider(workbench), {
      webviewOptions: { retainContextWhenHidden: true }
    })
  );
  context.subscriptions.push(
    vscode.window.registerCustomEditorProvider(
      "nuvexadb.explorer",
      new NuvexaEditorProvider(workbench),
      { webviewOptions: { retainContextWhenHidden: true } }
    )
  );
  context.subscriptions.push(
    vscode.commands.registerCommand("nuvexa.open", () => workbench.openDatabase())
  );
  context.subscriptions.push(
    vscode.commands.registerCommand("nuvexa.close", () => workbench.closeDatabase())
  );
  context.subscriptions.push(
    vscode.commands.registerCommand("nuvexa.about", async () => {
      await vscode.commands.executeCommand("workbench.view.extension.nuvexadb");
      workbench.showAbout();
    })
  );
  void vscode.commands.executeCommand("workbench.view.extension.nuvexadb");
}

class NuvexaBrowserViewProvider implements vscode.WebviewViewProvider {
  constructor(private readonly workbench: NuvexaWorkbench) {}

  resolveWebviewView(view: vscode.WebviewView): void {
    view.title = "Database Browser";
    this.workbench.attach(view.webview);
  }
}

class NuvexaEditorProvider implements vscode.CustomReadonlyEditorProvider {
  constructor(private readonly workbench: NuvexaWorkbench) {}

  async openCustomDocument(uri: vscode.Uri): Promise<vscode.CustomDocument> {
    return { uri, dispose: () => undefined };
  }

  async resolveCustomEditor(
    document: vscode.CustomDocument,
    webviewPanel: vscode.WebviewPanel
  ): Promise<void> {
    this.workbench.attach(webviewPanel.webview);
    await this.workbench.load(document.uri.fsPath);
  }
}

class NuvexaWorkbench implements vscode.Disposable {
  private readonly views = new Set<vscode.Webview>();
  private readonly subscriptions: vscode.Disposable[] = [];
  private path: string | undefined;

  dispose(): void {
    for (const item of this.subscriptions) {
      item.dispose();
    }
    this.views.clear();
  }

  attach(webview: vscode.Webview): void {
    webview.options = { enableScripts: true };
    webview.html = page();
    this.views.add(webview);
    this.subscriptions.push(
      webview.onDidReceiveMessage(async (msg: WorkbenchMessage) => {
        await this.onMessage(webview, msg);
      })
    );
    if (this.path) {
      void this.load(this.path);
    }
  }

  async openDatabase(): Promise<void> {
    await vscode.commands.executeCommand("workbench.view.extension.nuvexadb");
    const picked = await vscode.window.showOpenDialog({
      filters: { NuvexaDB: ["nvx"] },
      canSelectMany: false,
      title: "Open Database"
    });
    if (!picked?.[0]) {
      return;
    }

    await this.load(picked[0].fsPath);
    await vscode.commands.executeCommand("vscode.openWith", picked[0], "nuvexadb.explorer");
  }

  closeDatabase(): void {
    if (this.path) {
      keys.delete(this.path);
    }
    this.path = undefined;
    this.post({ type: "closed" });
  }

  showAbout(): void {
    this.post({ type: "about" });
  }

  async load(path: string): Promise<void> {
    try {
      const key = await ensureKey(path);
      if (key === "cancelled") {
        this.post({
          type: "error",
          surface: "browse",
          body: "Open cancelled. Encryption key required."
        });
        return;
      }

      this.path = path;
      const [treeJson, samplesJson] = await Promise.all([
        runNuvexa(["tree", path], keys.get(path)),
        runNuvexa(["samples"])
      ]);
      this.post({
        type: "opened",
        path,
        tree: parseTree(treeJson),
        samples: parseSamples(samplesJson)
      });
    } catch (e) {
      this.path = undefined;
      const body = e instanceof Error ? e.message : String(e);
      void vscode.window.showErrorMessage(body);
      this.post({ type: "error", surface: "browse", body });
    }
  }

  private async onMessage(webview: vscode.Webview, msg: WorkbenchMessage): Promise<void> {
    try {
      if (msg.type === "open") {
        await this.openDatabase();
        return;
      }
      if (msg.type === "close") {
        this.closeDatabase();
        return;
      }
      if (!this.path) {
        await webview.postMessage({ type: "error", surface: "browse", body: "Open a database first." });
        return;
      }

      const key = keys.get(this.path);
      if (msg.type === "query" && msg.query) {
        const result = await runNuvexa(["query", this.path, msg.query], key);
        const mutation = parseMutation(result);
        if (mutation) {
          this.post({
            type: "result",
            documents: [],
            explain: `${mutation.Operation} affected ${mutation.Affected} document(s).`
          });
          return;
        }

        const explain = await runNuvexa(["explain", this.path, msg.query], key);
        this.post({
          type: "result",
          documents: parseDocuments(result),
          explain: readExplain(explain)
        });
        return;
      }

      if (msg.type === "saveCell" && msg.collection && msg.json) {
        await runNuvexa(["replace", this.path, msg.collection, msg.json], key);
        const args = ["browse", this.path, msg.collection, "--page", String(msg.page ?? 0)];
        if (msg.filter) {
          args.push("--filter", msg.filter);
        }
        const browse = await runNuvexa(args, key);
        this.post({
          type: "browse",
          collection: msg.collection,
          body: parseBrowse(browse)
        });
        return;
      }

      if ((msg.type === "browse" || msg.type === "openCollection") && msg.collection) {
        const args = ["browse", this.path, msg.collection, "--page", String(msg.page ?? 0)];
        if (msg.filter) {
          args.push("--filter", msg.filter);
        }
        const result = await runNuvexa(args, key);
        this.post({
          type: "browse",
          collection: msg.collection,
          body: parseBrowse(result)
        });
      }
    } catch (e) {
      this.post({
        type: "error",
        surface: msg.type === "query" ? "query" : "browse",
        body: e instanceof Error ? e.message : String(e)
      });
    }
  }

  private post(message: Record<string, unknown>): void {
    for (const view of this.views) {
      void view.postMessage(message);
    }
  }
}

interface WorkbenchMessage {
  type: string;
  query?: string;
  collection?: string;
  filter?: string;
  page?: number;
  json?: string;
}

interface TreeNode {
  Name: string;
  Kind: string;
  Caption?: string;
  Detail?: string;
  Children: TreeNode[];
}

interface QuerySample {
  Title: string;
  Query: string;
}

async function ensureKey(path: string): Promise<string | undefined> {
  if (keys.has(path)) {
    return keys.get(path);
  }

  const info = await runNuvexa(["info", path]);
  if (!isEncryptedLockResponse(info)) {
    return undefined;
  }

  for (let attempt = 0; attempt < 3; attempt++) {
    const key = await vscode.window.showInputBox({
      prompt:
        attempt === 0
          ? "This .nvx file is encrypted. Enter the encryption key."
          : "Incorrect key. Enter the encryption key.",
      password: true
    });
    if (!key) {
      return "cancelled";
    }

    const check = await runNuvexa(["info", path], key);
    if (!isEncryptedLockResponse(check)) {
      keys.set(path, key);
      return key;
    }
  }

  return "cancelled";
}

function parseTree(json: string): TreeNode[] {
  try {
    const parsed = JSON.parse(json) as TreeNode[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function parseSamples(json: string): QuerySample[] {
  try {
    const parsed = JSON.parse(json) as QuerySample[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function parseDocuments(json: string): unknown[] {
  try {
    const parsed = JSON.parse(json) as unknown;
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function parseMutation(json: string): { Operation: string; Affected: number } | undefined {
  try {
    const parsed = JSON.parse(json) as { Operation?: string; Affected?: number };
    if (parsed.Operation === "update" || parsed.Operation === "delete") {
      return { Operation: parsed.Operation, Affected: parsed.Affected ?? 0 };
    }
  } catch {
    return undefined;
  }

  return undefined;
}

function parseBrowse(json: string): Record<string, unknown> {
  try {
    const parsed = JSON.parse(json) as Record<string, unknown>;
    return parsed && typeof parsed === "object" ? parsed : { Documents: [] };
  } catch {
    return { Documents: [], Status: json };
  }
}

function readExplain(json: string): string {
  try {
    const parsed = JSON.parse(json) as { explain?: string };
    return parsed.explain ?? json;
  } catch {
    return json;
  }
}

function page(): string {
  return `<!DOCTYPE html>
<html>
<head>
<style>
  html, body { height: 100%; }
  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); margin: 0; display: flex; flex-direction: column; }
  .toolbar { display: flex; align-items: center; gap: 8px; padding: 8px 12px; border-bottom: 1px solid var(--vscode-panel-border); }
  .toolbar button { margin: 0; }
  .shell { flex: 1; min-height: 0; display: flex; }
  aside { width: 280px; min-width: 180px; border-right: 1px solid var(--vscode-panel-border); padding: 10px 8px; overflow: auto; }
  @media (max-width: 560px) {
    .shell { flex-direction: column; }
    aside { width: auto; max-height: 38%; border-right: 0; border-bottom: 1px solid var(--vscode-panel-border); }
  }
  main { flex: 1; min-width: 0; display: flex; flex-direction: column; }
  .heading { font-weight: 600; margin: 0 0 6px; }
  .hint, .status { opacity: 0.75; font-size: 12px; white-space: pre-wrap; }
  .path { opacity: 0.8; font-size: 11px; margin: 0 0 10px; word-break: break-all; }
  details { margin-left: 4px; }
  details.collection { margin: 2px 0 6px; }
  summary { cursor: pointer; list-style: none; display: flex; align-items: center; gap: 4px; }
  summary::-webkit-details-marker { display: none; }
  .twist { width: 1em; opacity: 0.7; flex: none; }
  details[open] > summary .twist { transform: rotate(90deg); }
  .col { background: none; border: 0; color: inherit; text-align: left; padding: 2px 4px; cursor: pointer; flex: 1; }
  .col:hover, .col.active { background: var(--vscode-list-hoverBackground); }
  .g { opacity: 0.8; font-size: 12px; padding: 2px 0; }
  .leaf { opacity: 0.75; font-size: 12px; padding: 1px 0 1px 22px; }
  .tabs { display: flex; border-bottom: 1px solid var(--vscode-panel-border); }
  .tab, .doc-tab { padding: 8px 14px; cursor: pointer; background: none; border: 0; color: inherit; }
  .tab.active, .doc-tab.active { border-bottom: 2px solid var(--vscode-focusBorder); font-weight: 600; }
  .panel { display: none; flex: 1; flex-direction: column; min-height: 0; padding: 12px; gap: 8px; }
  .panel.active { display: flex; }
  textarea, pre, input, select { width: 100%; box-sizing: border-box; background: var(--vscode-editor-background); color: var(--vscode-editor-foreground); }
  textarea { height: 72px; font-family: var(--vscode-editor-font-family, monospace); }
  pre { height: 120px; overflow: auto; margin: 0; }
  button { margin: 4px 8px 4px 0; }
  .row { display: flex; align-items: center; gap: 8px; }
  .row input, .row select { flex: 1; }
  .error { color: #e07070; font-size: 12px; white-space: pre-wrap; }
  .grid-wrap { flex: 1; overflow: auto; min-height: 120px; border: 1px solid var(--vscode-panel-border); }
  table { border-collapse: collapse; width: 100%; font-size: 12px; }
  th, td { border: 1px solid var(--vscode-panel-border); padding: 4px 8px; max-width: 240px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  th { position: sticky; top: 0; background: var(--vscode-editor-background); text-align: left; }
  tr.selected { background: var(--vscode-list-activeSelectionBackground); color: var(--vscode-list-activeSelectionForeground); }
  tr { cursor: pointer; }
  #status { padding: 6px 12px; border-top: 1px solid var(--vscode-panel-border); opacity: 0.8; font-size: 12px; }
  .about { max-width: 560px; display: flex; flex-direction: column; gap: 10px; }
  .about h1 { font-size: 18px; margin: 0; }
  .about a { color: var(--vscode-textLink-foreground); }
</style>
</head>
<body>
  <div class="toolbar">
    <button id="openDb">Open Database</button>
    <button id="closeDb">Close Database</button>
    <button id="aboutDb">About</button>
  </div>
  <div class="shell">
    <aside>
      <div class="heading">Collections (tables)</div>
      <p class="path" id="dbPath">No database open.</p>
      <div id="tree"><p class="hint">Open a .nvx database to browse collections and columns.</p></div>
    </aside>
    <main>
      <div class="tabs">
        <button class="tab active" data-tab="browse">Browse Data</button>
        <button class="tab" data-tab="query">Execute Query</button>
        <button class="tab" data-tab="about">About</button>
      </div>
      <section id="browse" class="panel active">
        <div class="row">
          <label>Collection:</label>
          <select id="browseCollection"><option value="">Select a collection</option></select>
        </div>
        <div class="row">
          <label>Filter</label>
          <input id="filter" placeholder='status: paid   or   { status: "paid" }' />
          <button id="apply">Apply</button>
        </div>
        <div class="row">
          <label>Build</label>
          <select id="filterField"><option value="">field</option></select>
          <select id="filterOp">
            <option value="equals">equals</option>
            <option value="not equals">not equals</option>
            <option value=">">&gt;</option>
            <option value=">=">&gt;=</option>
            <option value="<">&lt;</option>
            <option value="<=">&lt;=</option>
            <option value="contains">contains</option>
            <option value="exists">exists</option>
          </select>
          <input id="filterValue" placeholder="value" />
          <button id="buildFilter">Build filter</button>
        </div>
        <div class="row">
          <label>Find in page</label>
          <input id="find" placeholder="Search _id, cells, or JSON on this page" />
          <button id="applyFind">Find</button>
        </div>
        <div class="row">
          <span class="status" id="browseStatus"></span>
          <span class="status" id="findStatus"></span>
          <span class="status" id="page"></span>
          <button id="prev" disabled>Previous</button>
          <button id="next" disabled>Next</button>
        </div>
        <div class="hint" id="browseExplain"></div>
        <div class="error" id="browseError"></div>
        <div class="grid-wrap"><div id="browseGrid"><p class="hint">Select a collection to browse.</p></div></div>
        <div class="row">
          <button class="doc-tab active" data-doc="json">JSON</button>
          <button class="doc-tab" data-doc="tree">Tree</button>
        </div>
        <pre id="browseJson"></pre>
        <div id="browseTree" class="hint" style="display:none;max-height:160px;overflow:auto"></div>
      </section>
      <section id="query" class="panel">
        <div class="row">
          <label>Examples</label>
          <select id="samples"><option value="">Choose an example…</option></select>
        </div>
        <textarea id="q" placeholder="db.users.find({ }).limit(50)"></textarea>
        <div class="row">
          <button id="run">Execute</button>
          <span class="hint">NQL: db.&lt;collection&gt;.find({ ... }).limit(n) — Ctrl+Enter</span>
        </div>
        <div class="error" id="queryError"></div>
        <div class="hint" id="queryExplain"></div>
        <div class="status" id="queryStatus"></div>
        <div class="grid-wrap"><div id="queryGrid"><p class="hint">Execute a query to see results.</p></div></div>
        <div class="hint">Selected record (JSON)</div>
        <pre id="queryJson"></pre>
      </section>
      <section id="about" class="panel">
        <div class="about">
          <h1>Nuvexa Data Studio</h1>
          <div class="hint">NuvexaDB 1.0.7 · .nvx format 2 (format 1 deprecated, still readable) · MIT</div>
          <p>Embedded NoSQL database for .NET and .NET MAUI. One portable .nvx file, BSON pages, optional AES-256-GCM, and NQL (Nuvexa Query Language).</p>
          <p class="hint">NuvexaDB is built by Niladri Prasad Padhy (Nuventra) and published with the MauiEssentials catalog under Nuvyntra Labs. Browse cells are editable. NQL find / aggregate / update / delete use the nuvexa CLI bundled in this VSIX (or <code>dotnet tool install -g Nuventra.NuvexaDB.Cli</code> on PATH).</p>
          <p>Author: Niladri Prasad Padhy / Nuventra<br />Organization: Nuvyntra Labs</p>
          <p>
            <a href="https://nuvyntralabs.github.io/">Website</a>
            · <a href="https://github.com/nuvyntralabs/NuvexaDB">GitHub</a>
            · <a href="https://www.nuget.org/packages/Nuventra.NuvexaDB">NuGet</a>
            · <a href="https://www.nuget.org/packages/Nuventra.NuvexaDB.Cli">CLI</a>
          </p>
        </div>
      </section>
    </main>
  </div>
  <div id="status">Closed.</div>
  <script>
    const vscode = acquireVsCodeApi();
    let collection = '';
    let pageIndex = 0;
    let browseDocs = [];
    let queryDocs = [];
    let samples = [];
    let findText = '';
    let sortField = '';
    let sortDesc = false;
    document.getElementById('openDb').onclick = () => vscode.postMessage({ type: 'open' });
    document.getElementById('closeDb').onclick = () => vscode.postMessage({ type: 'close' });
    document.getElementById('aboutDb').onclick = () => showTab('about');
    function showTab(name) {
      document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.getAttribute('data-tab') === name));
      document.querySelectorAll('.panel').forEach(p => p.classList.toggle('active', p.id === name));
    }
    document.querySelectorAll('.tab').forEach(tab => {
      tab.onclick = () => showTab(tab.getAttribute('data-tab'));
    });
    function esc(value) {
      return String(value ?? '').replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
    }
    function cellText(value) {
      if (value === null || value === undefined) return '';
      if (typeof value === 'object') return JSON.stringify(value);
      return String(value);
    }
    function renderGrid(hostId, jsonId, docs, selected, sortable) {
      const host = document.getElementById(hostId);
      if (!Array.isArray(docs) || docs.length === 0) {
        host.innerHTML = '<p class="hint">No rows.</p>';
        document.getElementById(jsonId).textContent = '';
        if (hostId === 'browseGrid') setBrowseDoc({});
        return;
      }
      const keys = [];
      docs.forEach(doc => {
        if (doc && typeof doc === 'object') {
          Object.keys(doc).forEach(k => { if (!keys.includes(k)) keys.push(k); });
        }
      });
      if (keys.includes('_id')) {
        keys.splice(keys.indexOf('_id'), 1);
        keys.unshift('_id');
      }
      let html = '<table><thead><tr>' + keys.map(k => '<th data-k="' + esc(k) + '">' + esc(k) + '</th>').join('') + '</tr></thead><tbody>';
      const editable = hostId === 'browseGrid';
      docs.forEach((doc, i) => {
        html += '<tr data-i="' + i + '"' + (i === selected ? ' class="selected"' : '') + '>';
        keys.forEach(k => {
          const text = esc(cellText(doc ? doc[k] : ''));
          if (editable && k !== '_id') {
            html += '<td contenteditable="true" data-k="' + esc(k) + '">' + text + '</td>';
          } else {
            html += '<td>' + text + '</td>';
          }
        });
        html += '</tr>';
      });
      host.innerHTML = html + '</tbody></table>';
      const current = docs[selected] ?? docs[0];
      document.getElementById(jsonId).textContent = JSON.stringify(current, null, 2);
      if (hostId === 'browseGrid') setBrowseDoc(current || {});
      host.querySelectorAll('tr[data-i]').forEach(row => {
        row.addEventListener('click', () => {
          host.querySelectorAll('tr').forEach(r => r.classList.remove('selected'));
          row.classList.add('selected');
          const doc = docs[Number(row.getAttribute('data-i'))];
          document.getElementById(jsonId).textContent = JSON.stringify(doc, null, 2);
          if (hostId === 'browseGrid') setBrowseDoc(doc || {});
        });
      });
      if (editable) {
        host.querySelectorAll('td[contenteditable]').forEach(td => {
          td.addEventListener('blur', () => {
            const tr = td.closest('tr');
            const i = Number(tr && tr.getAttribute('data-i'));
            const field = td.getAttribute('data-k');
            const doc = Object.assign({}, docs[i] || {});
            if (field) doc[field] = td.textContent || '';
            vscode.postMessage({
              type: 'saveCell',
              collection,
              json: JSON.stringify(doc),
              filter: document.getElementById('filter').value,
              page: pageIndex
            });
          });
        });
      }
      if (sortable) {
        host.querySelectorAll('th[data-k]').forEach(th => {
          th.addEventListener('click', () => {
            const field = th.getAttribute('data-k');
            if (sortField === field) sortDesc = !sortDesc;
            else { sortField = field; sortDesc = false; }
            renderBrowseGrid();
          });
        });
      }
    }
    function visibleBrowseDocs() {
      let docs = (browseDocs || []).slice();
      if (sortField) {
        docs.sort((a, b) => {
          const av = sortField === '_id' ? String(a && a._id || '') : cellText(a ? a[sortField] : '');
          const bv = sortField === '_id' ? String(b && b._id || '') : cellText(b ? b[sortField] : '');
          return av.localeCompare(bv, undefined, { numeric: true, sensitivity: 'base' });
        });
        if (sortDesc) docs.reverse();
      }
      const needle = (findText || '').trim().toLowerCase();
      if (!needle) return docs;
      return docs.filter(doc => JSON.stringify(doc || {}).toLowerCase().includes(needle));
    }
    function renderBrowseGrid() {
      const visible = visibleBrowseDocs();
      const findBox = document.getElementById('findStatus');
      findBox.textContent = findText.trim() ? ('Find: ' + visible.length + ' of ' + browseDocs.length + ' on this page.') : '';
      fillFilterFields(browseDocs);
      renderGrid('browseGrid', 'browseJson', visible, 0, true);
    }
    function fillFilterFields(docs) {
      const box = document.getElementById('filterField');
      const keep = box.value;
      const keys = [];
      (docs || []).forEach(doc => {
        if (doc && typeof doc === 'object') {
          Object.keys(doc).forEach(k => { if (!keys.includes(k)) keys.push(k); });
        }
      });
      if (keys.includes('_id')) {
        keys.splice(keys.indexOf('_id'), 1);
        keys.unshift('_id');
      } else {
        keys.unshift('_id');
      }
      box.innerHTML = keys.map(k => '<option value="' + esc(k) + '">' + esc(k) + '</option>').join('') || '<option value="">field</option>';
      box.value = keys.includes(keep) ? keep : (keys[1] || keys[0] || '');
    }
    function jsonToken(value) {
      const trimmed = String(value || '').trim();
      if (trimmed === 'true' || trimmed === 'false' || trimmed === 'null') return trimmed;
      if (trimmed !== '' && !Number.isNaN(Number(trimmed))) return trimmed;
      return JSON.stringify(value || '');
    }
    function buildFilter(field, op, value) {
      field = String(field || '').trim();
      if (!field) throw new Error('Choose a field for the filter.');
      if (!/^[A-Za-z0-9_.]+$/.test(field)) throw new Error('Field names may contain letters, digits, underscore, or dots.');
      value = value == null ? '' : String(value);
      if (op === 'equals') return value.trim() ? (field + ': ' + value.trim()) : ('{ ' + field + ': "" }');
      if (op === 'not equals') return '{ ' + field + ': { $ne: ' + jsonToken(value) + ' } }';
      if (op === '>' || op === '>=' || op === '<' || op === '<=') return '{ ' + field + ': { $' + (op === '>' ? 'gt' : op === '>=' ? 'gte' : op === '<' ? 'lt' : 'lte') + ': ' + jsonToken(value) + ' } }';
      if (op === 'contains') {
        if (!value.trim()) throw new Error('Enter text to match.');
        return '{ ' + field + ': { $regex: ' + JSON.stringify(value.trim()) + ' } }';
      }
      if (op === 'exists') return '{ ' + field + ': { $exists: true } }';
      throw new Error("Unknown filter operator '" + op + "'.");
    }
    function renderJsonTree(value, name) {
      if (value === null) return '<details open><summary>' + esc(name) + ': null</summary></details>';
      if (Array.isArray(value)) {
        return '<details open><summary>' + esc(name) + ' [' + value.length + ']</summary>' +
          value.map((item, i) => renderJsonTree(item, '[' + i + ']')).join('') + '</details>';
      }
      if (value && typeof value === 'object') {
        return '<details open><summary>' + esc(name) + '</summary>' +
          Object.keys(value).map(k => renderJsonTree(value[k], k)).join('') + '</details>';
      }
      return '<div class="leaf">' + esc(name) + ': ' + esc(value) + '</div>';
    }
    function setBrowseDoc(doc) {
      document.getElementById('browseJson').textContent = Object.keys(doc || {}).length ? JSON.stringify(doc, null, 2) : '';
      document.getElementById('browseTree').innerHTML = Object.keys(doc || {}).length ? renderJsonTree(doc, '(root)') : '';
    }
    function showDoc(name) {
      document.querySelectorAll('.doc-tab').forEach(t => t.classList.toggle('active', t.getAttribute('data-doc') === name));
      document.getElementById('browseJson').style.display = name === 'json' ? 'block' : 'none';
      document.getElementById('browseTree').style.display = name === 'tree' ? 'block' : 'none';
    }
    function renderTree(nodes) {
      return (nodes || []).map(n => {
        if (n.Kind === 'collection') {
          const kids = (n.Children || []).map(g => {
            if (g.Kind === 'group') {
              const leaves = (g.Children || []).map(c => '<div class="leaf">' + esc(c.Caption || c.Name) + '</div>').join('');
              return '<details class="group"><summary><span class="twist">▸</span><span class="g">' + esc(g.Caption || g.Name) + '</span></summary>' + leaves + '</details>';
            }
            return '<div class="leaf">' + esc(g.Caption || g.Name) + '</div>';
          }).join('');
          return '<details class="collection"><summary><span class="twist">▸</span><button class="col" data-col="' + esc(n.Name) + '">' + esc(n.Caption || n.Name) + '</button></summary>' + kids + '</details>';
        }
        return '';
      }).join('') || '<p class="hint">No collections.</p>';
    }
    function fillCollections(nodes) {
      const box = document.getElementById('browseCollection');
      const current = collection;
      box.innerHTML = '<option value="">Select a collection</option>' +
        (nodes || []).filter(n => n.Kind === 'collection').map(n =>
          '<option value="' + esc(n.Name) + '">' + esc(n.Caption || n.Name) + '</option>').join('');
      box.value = current;
    }
    function fillSamples(items, collectionName) {
      samples = items || [];
      const name = collectionName || 'users';
      document.getElementById('samples').innerHTML = '<option value="">Choose an example…</option>' +
        samples.map(s => {
          const q = String(s.Query || '').replace(/db\\.[A-Za-z0-9_]+\\./, 'db.' + name + '.');
          return '<option value="' + esc(q) + '">' + esc(s.Title) + '</option>';
        }).join('');
    }
    function bindTree() {
      document.querySelectorAll('.col').forEach(btn => btn.addEventListener('click', ev => {
        ev.preventDefault();
        ev.stopPropagation();
        const name = btn.getAttribute('data-col');
        selectCollection(name, true);
      }));
    }
    function selectCollection(name, browse) {
      if (!name) return;
      collection = name;
      pageIndex = 0;
      document.getElementById('browseCollection').value = name;
      document.querySelectorAll('.col').forEach(b => b.classList.toggle('active', b.getAttribute('data-col') === name));
      document.getElementById('q').value = 'db.' + name + '.find({}).limit(200)';
      fillSamples(samples, name);
      if (browse) {
        showTab('browse');
        vscode.postMessage({ type: 'openCollection', collection: name, page: 0 });
      }
    }
    function clearResults() {
      collection = '';
      pageIndex = 0;
      browseDocs = [];
      queryDocs = [];
      document.getElementById('browseCollection').innerHTML = '<option value="">Select a collection</option>';
      document.getElementById('browseGrid').innerHTML = '<p class="hint">Select a collection to browse.</p>';
      document.getElementById('queryGrid').innerHTML = '<p class="hint">Execute a query to see results.</p>';
      document.getElementById('browseJson').textContent = '';
      document.getElementById('queryJson').textContent = '';
      document.getElementById('browseStatus').textContent = '';
      document.getElementById('page').textContent = '';
      document.getElementById('browseExplain').textContent = '';
      document.getElementById('browseError').textContent = '';
      document.getElementById('findStatus').textContent = '';
      document.getElementById('queryExplain').textContent = '';
      document.getElementById('queryStatus').textContent = '';
      document.getElementById('queryError').textContent = '';
      document.getElementById('filter').value = '';
      document.getElementById('filterValue').value = '';
      document.getElementById('find').value = '';
      document.getElementById('browseTree').innerHTML = '';
      document.getElementById('q').value = '';
      findText = '';
      sortField = '';
      sortDesc = false;
      document.getElementById('prev').disabled = true;
      document.getElementById('next').disabled = true;
    }
    function runQuery() {
      document.getElementById('queryError').textContent = '';
      vscode.postMessage({ type: 'query', query: document.getElementById('q').value });
    }
    document.getElementById('run').onclick = runQuery;
    document.getElementById('q').addEventListener('keydown', ev => {
      if ((ev.ctrlKey || ev.metaKey) && ev.key === 'Enter') {
        ev.preventDefault();
        runQuery();
      }
    });
    document.getElementById('samples').onchange = ev => {
      const value = ev.target.value;
      if (value) document.getElementById('q').value = value;
    };
    document.getElementById('browseCollection').onchange = ev => {
      if (ev.target.value) selectCollection(ev.target.value, true);
    };
    document.getElementById('apply').onclick = () => {
      if (!collection) return;
      pageIndex = 0;
      document.getElementById('browseError').textContent = '';
      vscode.postMessage({ type: 'browse', collection, filter: document.getElementById('filter').value, page: 0 });
    };
    document.getElementById('filter').addEventListener('keydown', ev => {
      if (ev.key === 'Enter') document.getElementById('apply').click();
    });
    document.getElementById('buildFilter').onclick = () => {
      try {
        const text = buildFilter(
          document.getElementById('filterField').value,
          document.getElementById('filterOp').value,
          document.getElementById('filterValue').value
        );
        document.getElementById('filter').value = text;
        document.getElementById('browseError').textContent = '';
        document.getElementById('apply').click();
      } catch (err) {
        document.getElementById('browseError').textContent = err.message || String(err);
      }
    };
    document.getElementById('filterValue').addEventListener('keydown', ev => {
      if (ev.key === 'Enter') document.getElementById('buildFilter').click();
    });
    document.getElementById('applyFind').onclick = () => {
      findText = document.getElementById('find').value;
      renderBrowseGrid();
    };
    document.getElementById('find').addEventListener('keydown', ev => {
      if (ev.key === 'Enter') document.getElementById('applyFind').click();
    });
    document.querySelectorAll('.doc-tab').forEach(tab => {
      tab.onclick = () => showDoc(tab.getAttribute('data-doc'));
    });
    document.getElementById('prev').onclick = () => {
      if (!collection || pageIndex <= 0) return;
      pageIndex -= 1;
      vscode.postMessage({ type: 'browse', collection, filter: document.getElementById('filter').value, page: pageIndex });
    };
    document.getElementById('next').onclick = () => {
      if (!collection) return;
      pageIndex += 1;
      vscode.postMessage({ type: 'browse', collection, filter: document.getElementById('filter').value, page: pageIndex });
    };
    function showBrowse(data) {
      pageIndex = data.Page ?? data.page ?? 0;
      browseDocs = data.Documents ?? data.documents ?? [];
      document.getElementById('browseStatus').textContent = data.Status ?? data.status ?? '';
      document.getElementById('page').textContent = data.PageText ?? data.pageText ?? '';
      document.getElementById('browseExplain').textContent = data.Explain ?? data.explain ?? '';
      document.getElementById('prev').disabled = !(data.HasPrevious ?? data.hasPrevious);
      document.getElementById('next').disabled = !(data.HasNext ?? data.hasNext);
      document.getElementById('browseError').textContent = '';
      renderBrowseGrid();
      showTab('browse');
    }
    window.addEventListener('message', ev => {
      const m = ev.data;
      if (m.type === 'opened') {
        document.getElementById('dbPath').textContent = m.path || '';
        document.getElementById('status').textContent = (m.path || '') + ' (editable browse)';
        document.getElementById('tree').innerHTML = renderTree(m.tree);
        fillSamples(m.samples);
        fillCollections(m.tree);
        bindTree();
        const first = (m.tree || []).find(n => n.Kind === 'collection');
        if (first) {
          selectCollection(first.Name, true);
        }
      } else if (m.type === 'closed') {
        document.getElementById('dbPath').textContent = 'No database open.';
        document.getElementById('tree').innerHTML = '<p class="hint">Open a .nvx database to browse collections and columns.</p>';
        document.getElementById('samples').innerHTML = '<option value="">Choose an example…</option>';
        clearResults();
        document.getElementById('status').textContent = 'Closed.';
      } else if (m.type === 'browse') {
        if (m.collection) collection = m.collection;
        document.getElementById('browseCollection').value = collection;
        showBrowse(m.body || {});
      } else if (m.type === 'result') {
        queryDocs = m.documents || [];
        document.getElementById('queryError').textContent = '';
        document.getElementById('queryExplain').textContent = m.explain || '';
        document.getElementById('queryStatus').textContent = queryDocs.length + ' document(s).';
        renderGrid('queryGrid', 'queryJson', queryDocs, 0);
        showTab('query');
      } else if (m.type === 'about') {
        showTab('about');
      } else if (m.type === 'error') {
        if (m.surface === 'browse') {
          document.getElementById('browseStatus').textContent = m.body || '';
          document.getElementById('browseError').textContent = m.body || '';
          document.getElementById('status').textContent = m.body || '';
          showTab('browse');
        } else {
          document.getElementById('queryError').textContent = m.body || '';
          showTab('query');
        }
      }
    });
  </script>
</body>
</html>`;
}

function readCliError(out: string): string {
  return cliErrorMessage(out);
}

function resolveNuvexa(): { command: string; cwd?: string } {
  const exe = process.platform === "win32" ? "nuvexa.exe" : "nuvexa";
  if (extensionRoot) {
    const bundled = join(extensionRoot, "cli", exe);
    if (existsSync(bundled)) {
      return { command: bundled, cwd: dirname(bundled) };
    }
  }
  return { command: "nuvexa" };
}

function runNuvexa(args: string[], key?: string): Promise<string> {
  const extra = key ? ["--key", key] : [];
  const { command, cwd } = resolveNuvexa();
  return new Promise((resolve, reject) => {
    const child = spawn(command, [...args, ...extra], { shell: false, cwd });
    let out = "";
    let err = "";
    child.stdout.on("data", (c) => (out += c.toString()));
    child.stderr.on("data", (c) => (err += c.toString()));
    child.on("close", (code) => {
      const text = out || err;
      if (code === 0) {
        resolve(text);
        return;
      }
      // info peek: encrypted file without a key exits 2 with JSON (not a crash).
      if (code === 2 && args[0] === "info") {
        resolve(text);
        return;
      }
      reject(new Error(readCliError(text) || `nuvexa exited ${code}`));
    });
    child.on("error", (e) =>
      reject(
        new Error(
          `${e.message}. Reinstall the NuvexaDB VSIX for this OS, or install the standalone tool: dotnet tool install -g Nuventra.NuvexaDB.Cli`
        )
      )
    );
  });
}

export function deactivate(): void {
  // no-op
}
