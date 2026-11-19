// @vitest-environment node
import { describe, expect, it, vi } from 'vitest';

import { MOCK_PERMISSIONS } from '@/lib/mock/permissions';
import { testConfig } from '@/test/config';

import { loadCaller } from './caller';

const now = Date.UTC(2026, 9, 16, 9, 0, 0);
const researcher = { kind: 'development' as const, persona: 'researcher' as const, issuedAt: now };

function fetchReturning(response: Response | Error) {
  return vi.fn(async () => {
    if (response instanceof Error) throw response;
    return response;
  });
}

describe('loadCaller', () => {
  it('is signed out without a session', async () => {
    expect(await loadCaller(testConfig(), null, fetchReturning(new Response()))).toEqual({ kind: 'signed_out' });
  });

  it('reads the permissions the platform grants', async () => {
    const fetchApi = fetchReturning(Response.json({ permissions: ['analytics.read', 'exports.manage'], households: [] }));

    const result = await loadCaller(testConfig(), researcher, fetchApi);

    expect(result).toEqual({
      kind: 'caller',
      caller: { permissions: ['analytics.read', 'exports.manage'], persona: 'researcher', dataSource: 'api', authMode: 'development' },
    });
  });

  it('is signed out when the platform answers 401', async () => {
    expect(await loadCaller(testConfig(), researcher, fetchReturning(new Response(null, { status: 401 })))).toEqual({
      kind: 'signed_out',
    });
  });

  it('is unavailable when the platform cannot be reached or fails', async () => {
    expect(await loadCaller(testConfig(), researcher, fetchReturning(new TypeError('fetch failed')))).toEqual({
      kind: 'unavailable',
      reason: 'api_unreachable',
    });
    expect(await loadCaller(testConfig(), researcher, fetchReturning(new Response(null, { status: 503 })))).toEqual({
      kind: 'unavailable',
      reason: 'api_unreachable',
    });
  });

  it('is unavailable when the answer does not match the contract', async () => {
    expect(await loadCaller(testConfig(), researcher, fetchReturning(Response.json({ permissions: 'all' })))).toEqual({
      kind: 'unavailable',
      reason: 'unexpected_response',
    });
  });

  it('is unavailable when the persona has no development token', async () => {
    const fetchApi = fetchReturning(new Response());

    expect(await loadCaller(testConfig(), { ...researcher, persona: 'program_administrator' }, fetchApi)).toEqual({
      kind: 'unavailable',
      reason: 'dev_token_missing',
    });
    expect(fetchApi).not.toHaveBeenCalled();
  });

  it('takes the persona permissions from the mock without calling the API', async () => {
    const fetchApi = fetchReturning(new Response());
    const mock = testConfig({ data: { source: 'mock', scenario: 'no_approved_scores' } });

    const result = await loadCaller(mock, researcher, fetchApi);

    expect(result).toEqual({
      kind: 'caller',
      caller: { permissions: MOCK_PERMISSIONS.researcher, persona: 'researcher', dataSource: 'mock', authMode: 'development' },
    });
    expect(fetchApi).not.toHaveBeenCalled();
  });
});

describe('MOCK_PERMISSIONS', () => {
  it('mirrors the platform permission map for each role', () => {
    expect(MOCK_PERMISSIONS).toEqual({
      researcher: ['analytics.read', 'analytics.scores.read', 'exports.manage', 'questionnaires.read'],
      program_administrator: ['analytics.read', 'assessments.monitor', 'participation.read', 'privacy.requests.manage', 'questionnaires.read'],
      system_administrator: [
        'content.manage',
        'eligibility.run',
        'partners.manage',
        'questionnaires.read',
        'results.read',
        'rules.manage',
        'safety_alerts.manage',
        'safety_alerts.read',
        'scoring.run',
      ],
      support_coordinator: ['questionnaires.read', 'referrals.manage'],
    });
  });
});
