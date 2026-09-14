import assert from "node:assert/strict";
import { readFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

const root = dirname(fileURLToPath(new URL(".", import.meta.url)));
const pkg = JSON.parse(readFileSync(join(root, "package.json"), "utf8"));
const source = readFileSync(join(root, "src/extension.ts"), "utf8");

test("package identity", () => {
  assert.equal(pkg.name, "nuvexadb");
  assert.equal(pkg.publisher, "nuventra");
  assert.equal(pkg.main, "./out/extension.js");
  assert.match(String(pkg.engines.vscode), /^\^1\./);
  assert.equal(pkg.license, "MIT");
});

test("commands match extension registrations", () => {
  const commands = pkg.contributes.commands.map((item) => item.command).sort();
  assert.deepEqual(commands, ["nuvexa.about", "nuvexa.close", "nuvexa.open"]);
  for (const command of commands) {
    assert.match(source, new RegExp(`registerCommand\\("${command}"`));
    assert.ok(pkg.activationEvents.includes(`onCommand:${command}`));
  }
});

test("browser view and custom editor", () => {
  assert.equal(pkg.contributes.viewsContainers.activitybar[0].id, "nuvexadb");
  assert.equal(pkg.contributes.views.nuvexadb[0].id, "nuvexadb.browser");
  assert.equal(pkg.contributes.customEditors[0].viewType, "nuvexadb.explorer");
  assert.equal(pkg.contributes.customEditors[0].selector[0].filenamePattern, "*.nvx");
  assert.ok(pkg.activationEvents.includes("onView:nuvexadb.browser"));
  assert.ok(pkg.activationEvents.includes("onCustomEditor:nuvexadb.explorer"));
  assert.match(source, /registerWebviewViewProvider\("nuvexadb\.browser"/);
  assert.match(source, /registerCustomEditorProvider\(\s*"nuvexadb\.explorer"/);
});

test("editable browse workbench copy", () => {
  assert.match(source, /editable browse/i);
  assert.match(source, /Nuvexa Data Studio/);
  assert.match(source, /Niladri Prasad Padhy/);
  assert.match(source, /\.nvx format 2 \(format 1 deprecated, still readable\)/);
});

test("TypeScript compile output", () => {
  const compiled = join(root, "out/extension.js");
  assert.ok(existsSync(compiled), "run npm run compile before npm test");
  const js = readFileSync(compiled, "utf8");
  assert.match(js, /nuvexa\.open/);
  assert.match(js, /nuvexa\.close/);
  assert.match(js, /nuvexa\.about/);
});

test("VSIX bundles nuvexa instead of a global tool", () => {
  assert.match(source, /join\(extensionRoot, "cli"/);
  assert.doesNotMatch(source, /dotnet tool install/);
});
