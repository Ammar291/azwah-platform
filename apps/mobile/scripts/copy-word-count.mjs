#!/usr/bin/env node
// Counts the words of the app's copy per top-level group of src/i18n/resources/{en,ar}.json, now and (with --base) at
// a git revision, so a copy edit can say how much it removed. Run from apps/mobile:
//
//   node scripts/copy-word-count.mjs              # the working tree
//   node scripts/copy-word-count.mjs --base e08beaf
//
// "Visible" leaves out the strings that are only read by screen readers (keys ending in Label or Hint, and the few
// listed below): they are not words on the screen, and a copy edit must not shorten them into something meaningless.
// A word is any run of characters between spaces that holds a letter or a digit; {{placeholders}} count as one word.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const RESOURCES = 'src/i18n/resources';

/** Keys whose last part ends in Label or Hint but which are shown on screen. */
const VISIBLE_DESPITE_SUFFIX = new Set([
  'personaLabel',
  'accountLabel',
  'typeLabel',
  'memberLabel',
  'websiteLabel',
  'phoneLabel',
  'whyLabel',
  'birthYearHint',
  'birthMonthHint',
  'isHeadHint',
  'isPrimaryEarnerHint',
  'everyoneListedHint',
  'nameHint',
  'switchHint',
  'radioLabel',
]);

/** Screen-reader-only keys that do not end in Label or Hint. */
const SCREEN_READER_ONLY = new Set([
  'menu.open',
  'menu.close',
  'pillars.title',
  'history.openResult',
  'resilience.trend.point',
  'safety.crisisCard.announcement',
  'assistant.conversation',
  'result.scale.summary',
  'home.path.stepOf',
  'assistant.sources.label',
]);

function flatten(tree, prefix = '') {
  return Object.entries(tree).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    return typeof value === 'string' ? [[path, value]] : flatten(value, path);
  });
}

function isScreenReaderOnly(path) {
  const last = path.split('.').at(-1);
  if (SCREEN_READER_ONLY.has(path)) return true;
  if (path === 'language.switchHint') return true;
  return /(Label|Hint)$/.test(last) && !VISIBLE_DESPITE_SUFFIX.has(last);
}

const words = (text) => text.split(/\s+/).filter((token) => /[\p{L}\p{N}]/u.test(token)).length;

function count(json) {
  const groups = new Map();
  for (const [path, value] of flatten(json)) {
    const group = path.split('.')[0];
    const entry = groups.get(group) ?? { strings: 0, all: 0, visible: 0 };
    entry.strings += 1;
    entry.all += words(value);
    if (!isScreenReaderOnly(path)) entry.visible += words(value);
    groups.set(group, entry);
  }
  return groups;
}

function load(language, base) {
  const path = `${RESOURCES}/${language}.json`;
  const text = base
    ? execFileSync('git', ['show', `${base}:apps/mobile/${path}`], { cwd: ROOT, encoding: 'utf8' })
    : readFileSync(join(ROOT, path), 'utf8');
  return count(JSON.parse(text));
}

const baseIndex = process.argv.indexOf('--base');
const base = baseIndex > 0 ? process.argv[baseIndex + 1] : null;

const now = { en: load('en'), ar: load('ar') };
const before = base ? { en: load('en', base), ar: load('ar', base) } : null;

const groups = [...new Set([...now.en.keys(), ...(before ? before.en.keys() : [])])].sort(
  (a, b) => (before?.en.get(b)?.visible ?? now.en.get(b)?.visible ?? 0) - (before?.en.get(a)?.visible ?? now.en.get(a)?.visible ?? 0),
);

const pct = (from, to) => (from === 0 ? '' : `${Math.round(((to - from) / from) * 100)}%`);
const cell = (value) => String(value ?? 0);

const header = before
  ? ['group', 'en before', 'en after', 'en change', 'ar before', 'ar after', 'ar change', 'strings before', 'strings after']
  : ['group', 'en words', 'ar words', 'strings'];
const rows = [];
const totals = { enBefore: 0, enAfter: 0, arBefore: 0, arAfter: 0, sBefore: 0, sAfter: 0, enAllBefore: 0, enAllAfter: 0, arAllBefore: 0, arAllAfter: 0 };
for (const group of groups) {
  const en = now.en.get(group);
  const ar = now.ar.get(group);
  totals.enAfter += en?.visible ?? 0;
  totals.arAfter += ar?.visible ?? 0;
  totals.sAfter += en?.strings ?? 0;
  totals.enAllAfter += en?.all ?? 0;
  totals.arAllAfter += ar?.all ?? 0;
  if (before) {
    const enB = before.en.get(group);
    const arB = before.ar.get(group);
    totals.enBefore += enB?.visible ?? 0;
    totals.arBefore += arB?.visible ?? 0;
    totals.sBefore += enB?.strings ?? 0;
    totals.enAllBefore += enB?.all ?? 0;
    totals.arAllBefore += arB?.all ?? 0;
    rows.push([
      group,
      cell(enB?.visible),
      cell(en?.visible),
      pct(enB?.visible ?? 0, en?.visible ?? 0),
      cell(arB?.visible),
      cell(ar?.visible),
      pct(arB?.visible ?? 0, ar?.visible ?? 0),
      cell(enB?.strings),
      cell(en?.strings),
    ]);
  } else {
    rows.push([group, cell(en?.visible), cell(ar?.visible), cell(en?.strings)]);
  }
}
rows.push(
  before
    ? [
        'TOTAL visible',
        cell(totals.enBefore),
        cell(totals.enAfter),
        pct(totals.enBefore, totals.enAfter),
        cell(totals.arBefore),
        cell(totals.arAfter),
        pct(totals.arBefore, totals.arAfter),
        cell(totals.sBefore),
        cell(totals.sAfter),
      ]
    : ['TOTAL visible', cell(totals.enAfter), cell(totals.arAfter), cell(totals.sAfter)],
);
rows.push(
  before
    ? [
        'TOTAL incl. screen-reader',
        cell(totals.enAllBefore),
        cell(totals.enAllAfter),
        pct(totals.enAllBefore, totals.enAllAfter),
        cell(totals.arAllBefore),
        cell(totals.arAllAfter),
        pct(totals.arAllBefore, totals.arAllAfter),
        '',
        '',
      ]
    : ['TOTAL incl. screen-reader', cell(totals.enAllAfter), cell(totals.arAllAfter), ''],
);

console.log(`| ${header.join(' | ')} |`);
console.log(`| ${header.map(() => '---').join(' | ')} |`);
for (const row of rows) console.log(`| ${row.join(' | ')} |`);
