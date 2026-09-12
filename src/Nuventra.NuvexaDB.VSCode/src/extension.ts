import * as vscode from "vscode";
import { spawn } from "child_process";

const keys = new Map<string, string>();

export function activate(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.window.registerCustomEditorProvider(
      "nuvexadb.explorer",
      new NuvexaEditorProvider(),
      { webviewOptions: { retainContextWhenHidden: true } }
    )
  );

  context.subscriptions.push(
    vscode.commands.registerCommand("nuvexa.find", () => runFindCommand())
  );
  context.subscriptions.push(
    vscode.commands.registerCommand("nuvexa.changeKey", () => runChangeKeyCommand())
  );
}

class NuvexaEditorProvider implements vscode.CustomReadonlyEditorProvider {
  async openCustomDocument(uri: vscode.Uri): Promise<vscode.CustomDocument> {
    return { uri, dispose: () => undefined };
  }

  async resolveCustomEditor(
    document: vscode.CustomDocument,
    webviewPanel: vscode.WebviewPanel
  ): Promise<void> {
    webviewPanel.webview.options = { enableScripts: true };
    const path = document.uri.fsPath;

    try {
      const key = await ensureKey(path);
      if (key === "cancelled") {
        webviewPanel.webview.html = page(path, [], "Open cancelled. Encryption key required.", "");
        return;
      }

      const tree = parseTree(await runNuvexa(["tree", path], keys.get(path)));
      webviewPanel.webview.html = page(path, tree, "[]", "db.users.find({}).limit(50)");

      webviewPanel.webview.onDidReceiveMessage(async (msg: { type: string; query?: string; collection?: string }) => {
        try {
          if (msg.type === "query" && msg.query) {
            const result = await runNuvexa(["query", path, msg.query], keys.get(path));
            await webviewPanel.webview.postMessage({ type: "result", body: result });
          } else if (msg.type === "openCollection" && msg.collection) {
            const result = await runNuvexa(["find", path, msg.collection, "{}"], keys.get(path));
            const indexes = await runNuvexa(["indexes", path, msg.collection], keys.get(path));
            await webviewPanel.webview.postMessage({
              type: "collection",
              collection: msg.collection,
              body: result,
              indexes
            });
          }
        } catch (e) {
          await webviewPanel.webview.postMessage({
            type: "error",
            body: e instanceof Error ? e.message : String(e)
          });
        }
      });
    } catch (e) {
      webviewPanel.webview.html = page(
        path,
        [],
        e instanceof Error ? e.message : String(e),
        ""
      );
    }
  }
}

async function runFindCommand(): Promise<void> {
  const file = await vscode.window.showOpenDialog({ filters: { NuvexaDB: ["nvx"] } });
  if (!file?.[0]) {
    return;
  }
  const query = await vscode.window.showInputBox({
    prompt: "Mongo-style query",
    value: "db.users.find({}).limit(20)"
  });
  if (!query) {
    return;
  }
  const status = await ensureKey(file[0].fsPath);
  if (status === "cancelled") {
    return;
  }
  const result = await runNuvexa(["query", file[0].fsPath, query], keys.get(file[0].fsPath));
  const doc = await vscode.workspace.openTextDocument({ language: "json", content: result });
  await vscode.window.showTextDocument(doc);
}

async function runChangeKeyCommand(): Promise<void> {
  const file = await vscode.window.showOpenDialog({ filters: { NuvexaDB: ["nvx"] } });
  if (!file?.[0]) {
    return;
  }
  const current =
    keys.get(file[0].fsPath) ??
    (await vscode.window.showInputBox({ prompt: "Current encryption key", password: true }));
  const next = await vscode.window.showInputBox({ prompt: "New encryption key", password: true });
  if (!current || !next) {
    return;
  }
  await runNuvexa(["changkey", file[0].fsPath, "--new", next], current);
  keys.set(file[0].fsPath, next);
  void vscode.window.showInformationMessage("NuvexaDB encryption key updated.");
}

async function ensureKey(path: string): Promise<string | undefined> {
  if (keys.has(path)) {
    return keys.get(path);
  }
  const info = await runNuvexa(["info", path]);
  if (info.includes('"encrypted":true') && (info.includes("error") || info.includes("Encryption"))) {
    const key = await vscode.window.showInputBox({
      prompt: "This .nvx file is encrypted. Enter the encryption key.",
      password: true
    });
    if (!key) {
      return "cancelled";
    }
    keys.set(path, key);
    return key;
  }
  return undefined;
}

interface TreeNode {
  Name: string;
  Kind: string;
  Children: TreeNode[];
}

function parseTree(json: string): TreeNode[] {
  try {
    const parsed = JSON.parse(json) as TreeNode[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function page(title: string, tree: TreeNode[], results: string, query: string): string {
  const items = tree
    .map((n) => {
      const indexes = (n.Children ?? [])
        .map((c) => `<div class="idx">${escapeHtml(c.Name)}</div>`)
        .join("");
      return `<button class="col" data-col="${escapeHtml(n.Name)}">${escapeHtml(n.Name)}</button>${indexes}`;
    })
    .join("");
  return `<!DOCTYPE html>
<html>
<head>
<style>
  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); margin: 0; display: flex; height: 100vh; }
  aside { width: 240px; border-right: 1px solid var(--vscode-panel-border); padding: 12px; overflow: auto; }
  main { flex: 1; padding: 12px; display: flex; flex-direction: column; min-width: 0; }
  textarea, pre { width: 100%; box-sizing: border-box; background: var(--vscode-editor-background); color: var(--vscode-editor-foreground); }
  textarea { height: 72px; }
  pre { flex: 1; overflow: auto; }
  button { margin: 4px 8px 4px 0; }
  .col { display: block; width: 100%; text-align: left; }
  .idx { opacity: 0.7; padding-left: 12px; font-size: 12px; }
</style>
</head>
<body>
  <aside>
    <h3>NuvexaDB</h3>
    <p>${escapeHtml(title)}</p>
    <h4>Collections</h4>
    <div id="tree">${items || "<p>No collections.</p>"}</div>
  </aside>
  <main>
    <textarea id="q">${escapeHtml(query)}</textarea>
    <div><button id="run">Run query</button></div>
    <pre id="out">${escapeHtml(results)}</pre>
    <pre id="idx"></pre>
  </main>
  <script>
    const vscode = acquireVsCodeApi();
    document.getElementById('run').onclick = () =>
      vscode.postMessage({ type: 'query', query: document.getElementById('q').value });
    document.querySelectorAll('.col').forEach(btn => btn.addEventListener('click', () => {
      const name = btn.getAttribute('data-col');
      document.getElementById('q').value = 'db.' + name + '.find({}).limit(200)';
      vscode.postMessage({ type: 'openCollection', collection: name });
    }));
    window.addEventListener('message', ev => {
      const m = ev.data;
      if (m.type === 'result' || m.type === 'collection') document.getElementById('out').textContent = m.body;
      if (m.indexes) document.getElementById('idx').textContent = 'Indexes\\n' + m.indexes;
      if (m.type === 'error') document.getElementById('out').textContent = m.body;
    });
  </script>
</body>
</html>`;
}

function runNuvexa(args: string[], key?: string): Promise<string> {
  const extra = key ? ["--key", key] : [];
  return new Promise((resolve, reject) => {
    const child = spawn("nuvexa", [...args, ...extra], { shell: false });
    let out = "";
    let err = "";
    child.stdout.on("data", (c) => (out += c.toString()));
    child.stderr.on("data", (c) => (err += c.toString()));
    child.on("close", (code) => {
      if (code === 0 || code === 2) {
        resolve(out || err);
      } else {
        reject(new Error(err || `nuvexa exited ${code}`));
      }
    });
    child.on("error", (e) =>
      reject(
        new Error(
          `${e.message}. Install the CLI: dotnet tool install -g Nuventra.NuvexaDB.Cli`
        )
      )
    );
  });
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]!));
}

export function deactivate(): void {
  // no-op
}
