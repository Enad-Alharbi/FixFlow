// Self-test for check-i18n.mjs. Run: npm run test:scripts
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

function run(files) {
  const dir = mkdtempSync(join(tmpdir(), 'i18n-'));
  for (const [path, content] of Object.entries(files)) {
    const full = join(dir, path);
    mkdirSync(join(full, '..'), { recursive: true });
    writeFileSync(full, JSON.stringify(content));
  }
  return spawnSync(process.execPath, [join(import.meta.dirname, 'check-i18n.mjs'), dir], {
    encoding: 'utf8',
  });
}

test('passes when both languages have the same keys', () => {
  const result = run({ 'en.json': { a: { b: 'x' } }, 'ar.json': { a: { b: 'س' } } });
  assert.equal(result.status, 0, result.stderr);
});

test('fails on a key missing in a scope folder, and names it', () => {
  const result = run({
    'en.json': { a: 'x' },
    'ar.json': { a: 'س' },
    'requests/en.json': { list: { title: 'Requests', empty: 'None' } },
    'requests/ar.json': { list: { title: 'الطلبات' } },
  });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /missing key "list\.empty"/);
});

test('fails on an empty value', () => {
  const result = run({ 'en.json': { a: 'x' }, 'ar.json': { a: '  ' } });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /empty value for "a"/);
});
