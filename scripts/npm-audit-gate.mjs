#!/usr/bin/env node
// Fails when `npm audit` reports a high or critical advisory that is not on the app's reviewed allowlist, when an
// allowlisted advisory is past its review date, or when the audit produced no report at all (a network or registry
// failure must not read as "nothing found"). Usage, from an app folder:
//   npm audit --json | node ../../scripts/npm-audit-gate.mjs audit-allowlist.json
// The allowlist holds advisories with no fixed release that only reach build or test tooling, each with a reason and
// a review date (docs/decisions.md D-87). No dependencies: Node's standard library only. Tests:
// `node --test scripts/npm-audit-gate.test.mjs`.
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const GATED = new Set(['high', 'critical']);

function parseReport(output) {
  try {
    const report = JSON.parse(output);
    return report && typeof report === 'object' && !report.error && report.metadata ? report : null;
  } catch {
    return null;
  }
}

/**
 * The gate's verdict on `npm audit --json` output against an allowlist, on a given day (`YYYY-MM-DD`): `failures`
 * fail the build, `notes` are printed.
 */
export function evaluate(output, allowlist, today) {
  const report = parseReport(output);
  if (!report) {
    return { failures: ['npm audit did not produce a report (see its output above); the gate cannot pass without one'], notes: [] };
  }

  const advisories = new Map();
  for (const vulnerability of Object.values(report.vulnerabilities ?? {})) {
    for (const via of vulnerability.via ?? []) {
      if (typeof via === 'object' && via.url) {
        const id = via.url.split('/').pop();
        advisories.set(id, { id, severity: via.severity, name: via.name, range: via.range, title: via.title });
      }
    }
  }

  const allowed = new Map(allowlist.map((entry) => [entry.id, entry]));
  const failures = [];
  const notes = [];
  for (const advisory of advisories.values()) {
    if (!GATED.has(advisory.severity)) continue;
    const entry = allowed.get(advisory.id);
    if (!entry) {
      failures.push(`${advisory.severity} ${advisory.id} ${advisory.name} ${advisory.range}: ${advisory.title}`);
    } else if (entry.reviewBy < today) {
      failures.push(`${advisory.id} ${advisory.name}: the allowlist entry expired on ${entry.reviewBy}; review it`);
    } else {
      notes.push(`allowed until ${entry.reviewBy}: ${advisory.id} ${advisory.name} (${entry.reason})`);
    }
  }

  for (const entry of allowlist) {
    if (!advisories.has(entry.id)) notes.push(`no longer reported, remove from the allowlist: ${entry.id} ${entry.package}`);
  }

  return { failures, notes };
}

function readStdin() {
  try {
    return readFileSync(0, 'utf8');
  } catch {
    return '';
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const allowlistPath = process.argv[2];
  if (!allowlistPath) {
    console.error('usage: npm audit --json | node npm-audit-gate.mjs <allowlist.json>');
    process.exit(2);
  }

  const allowlist = JSON.parse(readFileSync(allowlistPath, 'utf8')).advisories ?? [];
  const { failures, notes } = evaluate(readStdin(), allowlist, new Date().toISOString().slice(0, 10));
  for (const note of notes) console.log(note);
  if (failures.length > 0) {
    console.error(`npm audit gate failed:\n  ${failures.join('\n  ')}`);
    process.exit(1);
  }
  console.log('npm audit gate passed.');
}
