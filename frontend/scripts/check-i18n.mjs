// Fails when the English and Arabic translation files don't have exactly the same keys,
// or when any value is empty. Covers every scope folder under public/i18n automatically.
//   node scripts/check-i18n.mjs            (checks public/i18n)
//   node scripts/check-i18n.mjs <folder>   (checks another folder; used by the self-test)
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const LANGUAGES = ['en', 'ar'];
const root = process.argv[2] ?? 'public/i18n';

/** Flattens {"a": {"b": "x"}} into Map { "a.b" => "x" }. */
function flatten(object, prefix = '', out = new Map()) {
  for (const [key, value] of Object.entries(object)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value !== null && typeof value === 'object') flatten(value, path, out);
    else out.set(path, value);
  }
  return out;
}

/** Folders holding translation files: the root (root strings) plus one per scope. */
function folders(dir) {
  return [
    dir,
    ...readdirSync(dir, { withFileTypes: true })
      .filter((e) => e.isDirectory())
      .map((e) => join(dir, e.name)),
  ];
}

const problems = [];
for (const folder of folders(root)) {
  const keys = {};
  for (const lang of LANGUAGES) {
    const file = join(folder, `${lang}.json`);
    try {
      keys[lang] = flatten(JSON.parse(readFileSync(file, 'utf8')));
    } catch (error) {
      problems.push(`${file}: ${error.code === 'ENOENT' ? 'missing file' : error.message}`);
    }
  }
  if (!keys.en || !keys.ar) continue;

  for (const [lang, other] of [
    ['en', 'ar'],
    ['ar', 'en'],
  ]) {
    for (const [key, value] of keys[lang]) {
      if (!keys[other].has(key))
        problems.push(`${join(folder, `${other}.json`)}: missing key "${key}"`);
      if (typeof value !== 'string' || value.trim() === '')
        problems.push(`${join(folder, `${lang}.json`)}: empty value for "${key}"`);
    }
  }
}

if (problems.length) {
  console.error(`i18n check failed (${problems.length}):\n  ${problems.join('\n  ')}`);
  process.exit(1);
}
console.log(
  `i18n check passed: ${folders(root).length} folder(s), languages ${LANGUAGES.join(', ')}.`,
);
