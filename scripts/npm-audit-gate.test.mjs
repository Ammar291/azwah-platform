// node --test scripts/npm-audit-gate.test.mjs
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { evaluate } from './npm-audit-gate.mjs';

const TODAY = '2026-10-18';

function report(...advisories) {
  const vulnerabilities = {};
  for (const [index, advisory] of advisories.entries()) {
    vulnerabilities[`package-${index}`] = {
      via: [{ url: `https://github.com/advisories/${advisory.id}`, severity: advisory.severity, name: advisory.name, range: '<9', title: 't' }],
    };
  }
  return JSON.stringify({ auditReportVersion: 2, vulnerabilities, metadata: { vulnerabilities: {} } });
}

const braces = { id: 'GHSA-vfj7-8cjw-p6xm', severity: 'high', name: 'braces' };
const allowlist = [{ id: braces.id, package: 'braces', reason: 'tooling only', reviewBy: '2027-01-17' }];

test('a report with no gated advisory passes', () => {
  assert.deepEqual(evaluate(report({ id: 'GHSA-low', severity: 'moderate', name: 'uuid' }), [], TODAY).failures, []);
});

test('a high or critical advisory that is not allowlisted fails', () => {
  const { failures } = evaluate(report(braces, { id: 'GHSA-new', severity: 'critical', name: 'left-pad' }), allowlist, TODAY);
  assert.equal(failures.length, 1);
  assert.match(failures[0], /GHSA-new/);
});

test('an allowlisted advisory passes until its review date, then fails', () => {
  assert.deepEqual(evaluate(report(braces), allowlist, TODAY).failures, []);
  assert.deepEqual(evaluate(report(braces), allowlist, '2027-01-17').failures, []);
  assert.match(evaluate(report(braces), allowlist, '2027-01-18').failures[0], /expired on 2027-01-17/);
});

test('an audit that did not run fails instead of passing', () => {
  for (const output of ['', 'not json', '{}', JSON.stringify({ error: { code: 'ENOTFOUND', summary: 'request failed' } })]) {
    const { failures } = evaluate(output, allowlist, TODAY);
    assert.equal(failures.length, 1, `output ${JSON.stringify(output)} should fail`);
    assert.match(failures[0], /did not produce a report/);
  }
});

test('an allowlist entry no longer reported is pointed out', () => {
  const { notes } = evaluate(report(), allowlist, TODAY);
  assert.match(notes.join('\n'), /no longer reported, remove from the allowlist: GHSA-vfj7-8cjw-p6xm/);
});
