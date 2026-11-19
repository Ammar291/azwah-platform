// @vitest-environment node
import { describe, expect, it } from 'vitest';

import type { Persona } from '@/lib/personas';

import { createMockBackend, type MockRequest } from './backend';
import { MOCK_WAVES } from './fixtures';

const now = new Date(Date.UTC(2026, 9, 16, 9, 0, 0));

function request(apiPath: string, persona: Persona, query: Record<string, string> = {}, extra: Partial<MockRequest> = {}): MockRequest {
  return { method: 'GET', apiPath, query: new URLSearchParams(query), body: null, persona, idempotencyKey: null, now, ...extra };
}

async function json(response: Response) {
  return (await response.json()) as Record<string, unknown> & { items: Record<string, unknown>[]; totalCount: number };
}

describe('mock assessments', () => {
  const backend = createMockBackend('no_approved_scores');

  it('pages on the server and reports the total', async () => {
    const first = await json(await backend.handle(request('/api/v1/admin/assessments', 'program_administrator', { pageSize: '10' })));
    const second = await json(
      await backend.handle(request('/api/v1/admin/assessments', 'program_administrator', { page: '2', pageSize: '10' })),
    );

    expect(first.items).toHaveLength(10);
    expect(first.totalCount).toBeGreaterThan(100);
    expect(second.page).toBe(2);
    expect(second.items[0].assignmentId).not.toBe(first.items[0].assignmentId);
  });

  it('refuses a page size over 100 with a camelCase key', async () => {
    const response = await backend.handle(request('/api/v1/admin/assessments', 'program_administrator', { pageSize: '101' }));

    expect(response.status).toBe(400);
    expect(response.headers.get('content-type')).toContain('application/problem+json');
    expect(Object.keys(((await response.json()) as { errors: object }).errors)).toEqual(['pageSize']);
  });

  it('combines the wave, instrument, version, status and date filters', async () => {
    const wave = MOCK_WAVES[0];
    const response = await backend.handle(
      request('/api/v1/admin/assessments', 'program_administrator', {
        waveId: wave.id,
        instrumentCode: 'household_assessment',
        versionNumber: '1',
        status: 'completed',
        assignedFrom: '2026-01-01',
        assignedTo: '2026-03-31',
        pageSize: '100',
      }),
    );
    const body = await json(response);

    expect(body.totalCount).toBeGreaterThan(0);
    for (const row of body.items) {
      expect(row).toMatchObject({ waveCode: wave.code, instrumentCode: 'household_assessment', versionNumber: 1, assignmentStatus: 'completed' });
      expect(String(row.assignedAt) >= '2026-01-01' && String(row.assignedAt) < '2026-04-01').toBe(true);
    }
  });

  it('refuses a version number without an instrument, as the platform does', async () => {
    const response = await backend.handle(request('/api/v1/admin/assessments', 'program_administrator', { versionNumber: '1' }));

    expect(response.status).toBe(400);
    expect(Object.keys(((await response.json()) as { errors: object }).errors)).toEqual(['versionNumber']);
  });

  it('answers 403 to a persona the platform would refuse', async () => {
    expect((await backend.handle(request('/api/v1/admin/assessments', 'researcher'))).status).toBe(403);
  });

  it('never returns answers or scores', async () => {
    const text = await (await backend.handle(request('/api/v1/admin/assessments', 'program_administrator'))).text();

    for (const hidden of ['tierCode', 'totalScore', 'responses', 'optionId']) expect(text).not.toContain(hidden);
  });
});

describe('mock participation', () => {
  const backend = createMockBackend('no_approved_scores');

  it('finds a household by its exact reference only', async () => {
    const all = await json(await backend.handle(request('/api/v1/admin/participation', 'program_administrator')));
    const reference = String(all.items[0].reference);

    const found = await json(await backend.handle(request('/api/v1/admin/participation', 'program_administrator', { reference })));
    const partial = await backend.handle(
      request('/api/v1/admin/participation', 'program_administrator', { reference: reference.slice(0, 6) }),
    );

    expect(found.items.map((row) => row.reference)).toEqual([reference]);
    expect(partial.status).toBe(400);
  });

  it('filters by status and roster confirmation', async () => {
    const body = await json(
      await backend.handle(
        request('/api/v1/admin/participation', 'program_administrator', { status: 'withdrawn', rosterConfirmed: 'true', pageSize: '100' }),
      ),
    );

    for (const row of body.items) {
      expect(row.status).toBe('withdrawn');
      expect(row.rosterConfirmedAt).not.toBeNull();
    }
  });
});

describe('mock privacy requests', () => {
  const list = '/api/v1/admin/privacy-requests';
  type Item = { id: string; status: string; createdAt: string; closedAt: string | null; resolution: string | null; householdReference: string };

  function close(backend: ReturnType<typeof createMockBackend>, id: string, body: unknown, persona: Persona = 'program_administrator') {
    return backend.handle(request(`${list}/${id}/close`, persona, {}, { method: 'POST', body }));
  }

  async function items(backend: ReturnType<typeof createMockBackend>, query: Record<string, string>) {
    return (await json(await backend.handle(request(list, 'program_administrator', { pageSize: '100', ...query })))).items as unknown as Item[];
  }

  it.each(['researcher', 'system_administrator', 'support_coordinator'] as const)('refuses %s, as the platform does', async (persona) => {
    const backend = createMockBackend('no_approved_scores');
    const [first] = await items(backend, { status: 'open' });

    expect((await backend.handle(request(list, persona, { status: 'open' }))).status).toBe(403);
    expect((await close(backend, first.id, { resolution: 'completed' }, persona)).status).toBe(403);
    expect((await items(backend, { status: 'open' })).map((item) => item.id)).toContain(first.id);
  });

  it('lists open requests oldest first, and closed ones with their outcome', async () => {
    const backend = createMockBackend('no_approved_scores');

    const open = await items(backend, { status: 'open' });
    const closed = await items(backend, { status: 'closed' });

    expect(open.length).toBeGreaterThan(20);
    expect(closed.length).toBeGreaterThan(0);
    expect(open.map((item) => item.createdAt)).toEqual([...open.map((item) => item.createdAt)].sort());
    for (const item of open) expect(item).toMatchObject({ status: 'open', closedAt: null, resolution: null });
    for (const item of closed) {
      expect(item.status).toBe('closed');
      expect(item.closedAt).not.toBeNull();
      expect(['completed', 'declined', 'withdrawn']).toContain(item.resolution);
    }
  });

  it('pages on the server', async () => {
    const backend = createMockBackend('no_approved_scores');

    const first = await json(await backend.handle(request(list, 'program_administrator', { status: 'open', pageSize: '20' })));
    const second = await json(await backend.handle(request(list, 'program_administrator', { status: 'open', page: '2', pageSize: '20' })));

    expect(first.items).toHaveLength(20);
    expect(second.page).toBe(2);
    expect(second.items[0].id).not.toBe(first.items[0].id);
    expect(second.totalCount).toBe(first.totalCount);
  });

  it('refuses an unknown status and a bad page size with camelCase keys', async () => {
    const backend = createMockBackend('no_approved_scores');

    const response = await backend.handle(request(list, 'program_administrator', { status: 'pending', pageSize: '101' }));

    expect(response.status).toBe(400);
    expect(Object.keys(((await response.json()) as { errors: object }).errors).sort()).toEqual(['pageSize', 'status']);
  });

  it('closes a request with its outcome, which then appears among the closed ones', async () => {
    const backend = createMockBackend('no_approved_scores');
    const [first] = await items(backend, { status: 'open' });

    const response = await close(backend, first.id, { resolution: 'declined' });

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ ...first, status: 'closed', closedAt: '2026-10-16T09:00:00Z', resolution: 'declined' });
    expect((await items(backend, { status: 'open' })).map((item) => item.id)).not.toContain(first.id);
    expect((await items(backend, { status: 'closed' })).find((item) => item.id === first.id)).toMatchObject({ resolution: 'declined' });
  });

  it('refuses to close a request twice, and keeps the first outcome', async () => {
    const backend = createMockBackend('no_approved_scores');
    const [first] = await items(backend, { status: 'open' });
    await close(backend, first.id, { resolution: 'completed' });

    const again = await close(backend, first.id, { resolution: 'withdrawn' });

    expect(again.status).toBe(409);
    expect(((await again.json()) as { code: string }).code).toBe('privacy.request_already_closed');
    expect((await items(backend, { status: 'closed' })).find((item) => item.id === first.id)?.resolution).toBe('completed');
  });

  it('answers 404 for a request it does not have', async () => {
    const response = await close(createMockBackend('no_approved_scores'), '00000000-0000-4000-8000-000000000000', { resolution: 'completed' });

    expect(response.status).toBe(404);
    expect(((await response.json()) as { code: string }).code).toBe('privacy.request_not_found');
  });

  it('refuses an outcome it does not know, or none, without closing anything', async () => {
    const backend = createMockBackend('no_approved_scores');
    const [first] = await items(backend, { status: 'open' });

    const unknown = await close(backend, first.id, { resolution: 'erased' });
    const missing = await close(backend, first.id, {});

    for (const response of [unknown, missing]) {
      expect(response.status).toBe(400);
      expect(Object.keys(((await response.json()) as { errors: object }).errors)).toEqual(['resolution']);
    }
    expect((await items(backend, { status: 'open' })).map((item) => item.id)).toContain(first.id);
  });

  it('uses the platform household reference format and holds no free text', async () => {
    const backend = createMockBackend('no_approved_scores');
    const text = await (await backend.handle(request(list, 'program_administrator', { status: 'closed', pageSize: '100' }))).text();

    for (const item of await items(backend, { status: 'open' })) {
      expect(item.householdReference).toMatch(/^HH-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$/);
    }
    for (const hidden of ['name', 'email', 'phone', 'reason', 'note', 'detail']) expect(text).not.toContain(hidden);
  });
});

describe('mock analytics', () => {
  it('answers the overview per wave', async () => {
    const backend = createMockBackend('no_approved_scores');
    const all = await json(await backend.handle(request('/api/v1/admin/analytics/overview', 'researcher')));
    const wave = await json(
      await backend.handle(request('/api/v1/admin/analytics/overview', 'researcher', { waveId: MOCK_WAVES[1].id })),
    );

    expect(all.waveId).toBeNull();
    expect(wave.waveId).toBe(MOCK_WAVES[1].id);
    expect((wave.assignments as { total: number }).total).toBeLessThan((all.assignments as { total: number }).total);
  });

  it('shapes the trend for the requested window and interval', async () => {
    const backend = createMockBackend('no_approved_scores');
    const body = await json(
      await backend.handle(
        request('/api/v1/admin/analytics/overview', 'program_administrator', { from: '2026-09-01', to: '2026-09-30', interval: 'week' }),
      ),
    );

    expect(body.interval).toBe('week');
    expect((body.completionTrend as { periodStart: string }[]).map((point) => point.periodStart)).toEqual([
      '2026-08-31',
      '2026-09-07',
      '2026-09-14',
      '2026-09-21',
      '2026-09-28',
    ]);
  });

  it('has no score distributions until the approved-scores scenario is chosen', async () => {
    const none = await json(await createMockBackend('no_approved_scores').handle(request('/api/v1/admin/analytics/score-distributions', 'researcher')));
    const some = await json(await createMockBackend('approved_scores').handle(request('/api/v1/admin/analytics/score-distributions', 'researcher')));

    expect(none.distributions).toEqual([]);
    expect((some.distributions as unknown[]).length).toBeGreaterThan(0);
  });

  it('keeps score distributions from program administrators', async () => {
    const response = await createMockBackend('approved_scores').handle(
      request('/api/v1/admin/analytics/score-distributions', 'program_administrator'),
    );

    expect(response.status).toBe(403);
  });
});

describe('mock household results', () => {
  const backend = createMockBackend('approved_scores');
  type Row = { sessionId: string; householdReference: string; waveCode: string; instrumentCode: string; state: string; totalScore: number | null; tierCode: string | null; computedAt: string };
  const results = async (query: Record<string, string> = {}) =>
    (await json(await backend.handle(request('/api/v1/admin/results', 'system_administrator', { pageSize: '100', ...query })))) as unknown as {
      items: Row[];
      totalCount: number;
      page: number;
    };

  it('answers system administrators only, as the platform does', async () => {
    for (const persona of ['researcher', 'program_administrator', 'support_coordinator'] as const) {
      expect((await backend.handle(request('/api/v1/admin/results', persona))).status).toBe(403);
    }
    expect((await backend.handle(request('/api/v1/admin/results', 'system_administrator'))).status).toBe(200);
  });

  it('lets system administrators list the waves the results are filtered by, as the platform does', async () => {
    for (const persona of ['researcher', 'program_administrator', 'system_administrator'] as const) {
      expect((await backend.handle(request('/api/v1/admin/waves', persona))).status).toBe(200);
    }
    expect((await backend.handle(request('/api/v1/admin/waves', 'support_coordinator'))).status).toBe(403);
  });

  it('has no results until the approved-scores scenario is chosen', async () => {
    const none = await json(await createMockBackend('no_approved_scores').handle(request('/api/v1/admin/results', 'system_administrator')));

    expect(none.totalCount).toBe(0);
  });

  it('holds the three named bands and no other code, and only complete results, as the household assessment allows', async () => {
    const all = await results();
    const bands = new Set(all.items.map((row) => row.tierCode));

    expect(bands).toEqual(new Set(['vulnerable', 'transitional', 'flourishing']));
    for (const row of all.items) expect(row.state).toBe('complete');
  });

  it('lists the newest result first and pages on the server', async () => {
    const all = await results();
    const second = await results({ page: '2', pageSize: '10' });

    expect(all.totalCount).toBeGreaterThan(10);
    expect(all.items.map((row) => row.computedAt)).toEqual([...all.items.map((row) => row.computedAt)].sort().reverse());
    expect(second.page).toBe(2);
    expect(second.items.map((row) => row.sessionId)).toEqual(all.items.slice(10, 20).map((row) => row.sessionId));
  });

  it('combines the wave, questionnaire and exact reference filters', async () => {
    const wave = MOCK_WAVES[0];
    const byWave = await results({ waveId: wave.id, instrumentCode: 'household_assessment' });
    const reference = byWave.items[0].householdReference;
    const byReference = await results({ reference });

    expect(byWave.totalCount).toBeGreaterThan(0);
    for (const row of byWave.items) expect(row).toMatchObject({ waveCode: wave.code, instrumentCode: 'household_assessment' });
    expect(byReference.totalCount).toBeGreaterThan(0);
    for (const row of byReference.items) expect(row.householdReference).toBe(reference);
  });

  it('refuses a malformed questionnaire code, reference, wave or page size with camelCase keys', async () => {
    const response = await backend.handle(
      request('/api/v1/admin/results', 'system_administrator', { instrumentCode: 'Bad Code', reference: 'HH-12', waveId: 'x', pageSize: '101' }),
    );

    expect(response.status).toBe(400);
    expect(Object.keys(((await response.json()) as { errors: object }).errors).sort()).toEqual(['instrumentCode', 'pageSize', 'reference', 'waveId']);
  });
});

describe('mock exports', () => {
  const body = { dataset: 'score_results', columns: ['participant_pseudonym', 'tier_code'], filters: { waveId: null, instrumentCode: null } };

  function create(backend: ReturnType<typeof createMockBackend>, key: string, payload: unknown = body, at = now) {
    return backend.handle(request('/api/v1/admin/exports', 'researcher', {}, { method: 'POST', body: payload, idempotencyKey: key, now: at }));
  }

  it('queues an export, then runs and completes it over a few seconds', async () => {
    const backend = createMockBackend('no_approved_scores');
    const created = await create(backend, '11111111-1111-4111-8111-111111111111');
    const exportId = String(((await created.json()) as { id: string }).id);
    const status = async (seconds: number) =>
      ((await (await backend.handle(request(`/api/v1/admin/exports/${exportId}`, 'researcher', {}, { now: new Date(now.getTime() + seconds * 1000) }))).json()) as {
        status: string;
        downloadPath: string | null;
      });

    expect(created.status).toBe(202);
    expect(created.headers.get('location')).toBe(`/api/v1/admin/exports/${exportId}`);
    expect((await status(1)).status).toBe('queued');
    expect((await status(5)).status).toBe('running');
    const done = await status(20);
    expect(done).toMatchObject({ status: 'completed', downloadPath: `/api/v1/admin/exports/${exportId}/file` });
  });

  it('replays a request with the same idempotency key and refuses the key for another request', async () => {
    const backend = createMockBackend('no_approved_scores');
    const key = '22222222-2222-4222-8222-222222222222';
    const first = (await (await create(backend, key)).json()) as { id: string };
    const again = (await (await create(backend, key)).json()) as { id: string };
    const other = await create(backend, key, { ...body, dataset: 'assessment_sessions', columns: ['wave_code'] });

    expect(again.id).toBe(first.id);
    expect(other.status).toBe(409);
    expect(((await other.json()) as { code: string }).code).toBe('requests.idempotency_key_reused');
  });

  it('refuses columns outside the catalog and a missing idempotency key', async () => {
    const backend = createMockBackend('no_approved_scores');

    const column = await create(backend, '33333333-3333-4333-8333-333333333333', { ...body, columns: ['member_id'] });
    const missingKey = await backend.handle(request('/api/v1/admin/exports', 'researcher', {}, { method: 'POST', body }));

    expect(column.status).toBe(400);
    expect(missingKey.status).toBe(400);
    expect(Object.keys(((await missingKey.json()) as { errors: object }).errors)).toEqual(['idempotencyKey']);
  });

  it('lists the requester exports newest first and serves the file without caching', async () => {
    const backend = createMockBackend('no_approved_scores');
    await create(backend, '44444444-4444-4444-8444-444444444444');
    const second = (await (await create(backend, '55555555-5555-4555-8555-555555555555', body, new Date(now.getTime() + 1000))).json()) as { id: string };
    const later = new Date(now.getTime() + 60_000);

    const list = await json(await backend.handle(request('/api/v1/admin/exports', 'researcher', {}, { now: later })));
    const file = await backend.handle(request(`/api/v1/admin/exports/${second.id}/file`, 'researcher', {}, { now: later }));

    expect(list.items[0].id).toBe(second.id);
    expect(list.totalCount).toBe(2);
    expect(file.status).toBe(200);
    expect(file.headers.get('content-type')).toContain('text/csv');
    expect(file.headers.get('cache-control')).toBe('no-store');
    expect((await file.text()).split('\r\n')[0]).toBe('participant_pseudonym,tier_code');
  });

  it('writes the rows of the population the pages show, and reports how many', async () => {
    const backend = createMockBackend('approved_scores');
    const columns = ['participant_pseudonym', 'instrument_code', 'section_code', 'rule_set_code', 'score_status', 'total_score', 'tier_code'];
    const created = (await (await create(backend, '77777777-7777-4777-8777-777777777777', { ...body, columns })).json()) as { id: string };
    const later = new Date(now.getTime() + 60_000);

    const job = (await (await backend.handle(request(`/api/v1/admin/exports/${created.id}`, 'researcher', {}, { now: later }))).json()) as { rowCount: number };
    const file = await (await backend.handle(request(`/api/v1/admin/exports/${created.id}/file`, 'researcher', {}, { now: later }))).text();
    const listed = await json(await backend.handle(request('/api/v1/admin/results', 'system_administrator', { pageSize: '1' })));
    const rows = file.trimEnd().split('\r\n').slice(1).map((line) => line.split(','));

    expect(rows).toHaveLength(listed.totalCount);
    expect(job.rowCount).toBe(listed.totalCount);
    expect(rows.map((row) => row[0])).toEqual([...rows.map((row) => row[0])].sort());
    for (const [, instrument, section, ruleSet, status, total, tier] of rows) {
      expect([instrument, section, ruleSet, status]).toEqual(['household_assessment', '', 'household_assessment_total', 'complete']);
      expect(tier).toBe(Number(total) <= 78 ? 'vulnerable' : Number(total) <= 117 ? 'transitional' : 'flourishing');
    }
  });

  it('has no score rows to export until scoring is approved', async () => {
    const backend = createMockBackend('no_approved_scores');
    const created = (await (await create(backend, '88888888-8888-4888-8888-888888888888')).json()) as { id: string };

    const file = await backend.handle(request(`/api/v1/admin/exports/${created.id}/file`, 'researcher', {}, { now: new Date(now.getTime() + 60_000) }));

    expect(await file.text()).toBe('participant_pseudonym,tier_code\r\n');
  });

  it('serves the catalog', async () => {
    const catalog = await json(await createMockBackend('no_approved_scores').handle(request('/api/v1/admin/exports/catalog', 'researcher')));

    expect((catalog.datasets as { code: string }[]).map((dataset) => dataset.code)).toEqual(['assessment_sessions', 'score_results']);
    expect(catalog.filters).toEqual(['wave_id', 'instrument_code']);
  });
});

describe('mock configuration', () => {
  const backend = createMockBackend('no_approved_scores');

  it('lists the development sample instruments for system administrators', async () => {
    const instruments = await json(await backend.handle(request('/api/v1/instruments', 'system_administrator')));

    expect(instruments.items.length).toBeGreaterThan(0);
    for (const instrument of instruments.items) expect(JSON.stringify(instrument.name)).toContain('DEV SAMPLE');
  });

  it('answers 404 for an unknown version', async () => {
    const response = await backend.handle(
      request('/api/v1/instrument-versions/00000000-0000-4000-8000-000000000000', 'system_administrator'),
    );

    expect(response.status).toBe(404);
    expect(((await response.json()) as { code: string }).code).toBe('questionnaires.version_not_found');
  });

  it('answers 404 for a route the platform does not have', async () => {
    expect((await backend.handle(request('/api/v1/admin/unknown', 'researcher'))).status).toBe(404);
  });
});

describe('mock safety alerts', () => {
  const list = '/api/v1/admin/safety-alerts';
  type Item = {
    id: string;
    status: string;
    severity: string;
    category: string;
    source: string;
    lastDetectedAt: string;
    acknowledgedAt: string | null;
    resolvedAt: string | null;
    resolution: string | null;
  };
  type Backend = ReturnType<typeof createMockBackend>;

  function post(backend: Backend, id: string, action: string, body: unknown = null, persona: Persona = 'system_administrator') {
    return backend.handle(request(`${list}/${id}/${action}`, persona, {}, { method: 'POST', body }));
  }

  async function items(backend: Backend, query: Record<string, string> = {}) {
    return (await json(await backend.handle(request(list, 'system_administrator', { pageSize: '100', ...query })))).items as unknown as Item[];
  }

  it.each(['researcher', 'program_administrator', 'support_coordinator'] as const)('refuses %s, as the platform does', async (persona) => {
    const backend = createMockBackend('no_approved_scores');
    const [first] = await items(backend);

    expect((await backend.handle(request(list, persona))).status).toBe(403);
    expect((await backend.handle(request(`${list}/summary`, persona))).status).toBe(403);
    expect((await post(backend, first.id, 'acknowledge', null, persona)).status).toBe(403);
    expect((await post(backend, first.id, 'resolve', { resolution: 'false_alarm' }, persona)).status).toBe(403);
    expect((await items(backend)).find((item) => item.id === first.id)).toEqual(first);
  });

  it('covers every concern, status and source, with no free text', async () => {
    const all = await items(createMockBackend('approved_scores'));

    expect(all).toHaveLength(6);
    expect(new Set(all.map((item) => item.category))).toEqual(
      new Set(['suicide_or_self_harm', 'harm_to_others', 'abuse_or_danger', 'low_resilience_score']),
    );
    expect(new Set(all.map((item) => item.status))).toEqual(new Set(['open', 'acknowledged', 'resolved']));
    expect(new Set(all.map((item) => item.source))).toEqual(
      new Set(['assistant_chat', 'consultant_voice', 'support_search', 'household_assessment']),
    );
  });

  it('lists open before acknowledged before resolved, critical before high, then newest first', async () => {
    const all = await items(createMockBackend('no_approved_scores'));
    const rank = (item: Item) => ['open', 'acknowledged', 'resolved'].indexOf(item.status);
    const severity = (item: Item) => ['critical', 'high'].indexOf(item.severity);

    for (let index = 1; index < all.length; index++) {
      const [a, b] = [all[index - 1], all[index]];
      const order = rank(a) - rank(b) || severity(a) - severity(b) || b.lastDetectedAt.localeCompare(a.lastDetectedAt);
      expect(order).toBeLessThanOrEqual(0);
    }
    expect(all[0].status).toBe('open');
    expect(all[all.length - 1].status).toBe('resolved');
  });

  it('filters by status, with unresolved meaning open and acknowledged, and by category', async () => {
    const backend = createMockBackend('approved_scores');

    expect((await items(backend, { status: 'open' })).every((item) => item.status === 'open')).toBe(true);
    expect((await items(backend, { status: 'acknowledged' })).map((item) => item.status)).toEqual(['acknowledged']);
    expect((await items(backend, { status: 'resolved' })).every((item) => item.status === 'resolved')).toBe(true);
    const unresolved = await items(backend, { status: 'unresolved' });
    expect(unresolved.length).toBeGreaterThan(0);
    expect(unresolved.every((item) => item.status === 'open' || item.status === 'acknowledged')).toBe(true);
    expect(unresolved.length + (await items(backend, { status: 'resolved' })).length).toBe((await items(backend)).length);
    const category = await items(backend, { category: 'low_resilience_score' });
    expect(category.length).toBeGreaterThan(0);
    expect(category.every((item) => item.category === 'low_resilience_score')).toBe(true);
  });

  it('pairs every signal code with its real concern, and gives score alerts the vulnerable signal', async () => {
    const all = (await items(createMockBackend('approved_scores'))) as (Item & { signalCode: string | null })[];
    const concernOf = (code: string) =>
      /^(suicide|self_harm)\./.test(code)
        ? 'suicide_or_self_harm'
        : code.startsWith('harm_others.')
          ? 'harm_to_others'
          : /^(abuse|danger)\./.test(code)
            ? 'abuse_or_danger'
            : 'low_resilience_score';

    for (const alert of all.filter((item) => item.signalCode !== null)) {
      expect(alert.category, alert.signalCode ?? '').toBe(concernOf(alert.signalCode ?? ''));
    }
    const scores = all.filter((item) => item.category === 'low_resilience_score');
    expect(scores.length).toBeGreaterThan(0);
    expect(scores.every((item) => item.signalCode === 'vulnerable')).toBe(true);
  });

  it('pages on the server', async () => {
    const backend = createMockBackend('approved_scores');

    const second = await json(await backend.handle(request(list, 'system_administrator', { page: '2', pageSize: '4' })));

    expect(second.items).toHaveLength(2);
    expect(second.page).toBe(2);
    expect(second.totalCount).toBe(6);
  });

  it('raises no score alert where no score is approved, as the platform cannot', async () => {
    const all = await items(createMockBackend('no_approved_scores'));

    expect(all).toHaveLength(4);
    expect(all.some((item) => item.category === 'low_resilience_score' || item.source === 'household_assessment')).toBe(false);
    expect(new Set(all.map((item) => item.status))).toEqual(new Set(['open', 'acknowledged', 'resolved']));
  });

  it('refuses an unknown status or category and a bad page size with camelCase keys', async () => {
    const response = await createMockBackend('no_approved_scores').handle(
      request(list, 'system_administrator', { status: 'pending', category: 'other', pageSize: '101' }),
    );

    expect(response.status).toBe(400);
    expect(Object.keys(((await response.json()) as { errors: object }).errors).sort()).toEqual(['category', 'pageSize', 'status']);
  });

  it('summarises the counts, and the summary follows what is acknowledged and resolved', async () => {
    const backend = createMockBackend('no_approved_scores');
    const summary = async () => (await (await backend.handle(request(`${list}/summary`, 'system_administrator'))).json()) as Record<string, unknown>;
    const before = await summary();
    const all = await items(backend);
    const openCritical = all.filter((item) => item.status === 'open' && item.severity === 'critical');

    expect(before).toEqual({
      open: all.filter((item) => item.status === 'open').length,
      acknowledged: 1,
      openCritical: openCritical.length,
      latestDetectedAt: all
        .filter((item) => item.status !== 'resolved')
        .map((item) => item.lastDetectedAt)
        .sort()
        .at(-1),
    });
    expect(openCritical.length).toBeGreaterThan(0);

    await post(backend, openCritical[0].id, 'acknowledge');
    expect(await summary()).toMatchObject({
      open: (before.open as number) - 1,
      acknowledged: 2,
      openCritical: (before.openCritical as number) - 1,
    });
    await post(backend, openCritical[0].id, 'resolve', { resolution: 'family_contacted' });
    expect(await summary()).toMatchObject({ open: (before.open as number) - 1, acknowledged: 1 });
  });

  it('moves latestDetectedAt back to the next unresolved alert when the newest is resolved, and to null when none is left', async () => {
    const backend = createMockBackend('no_approved_scores');
    const latest = async () =>
      ((await (await backend.handle(request(`${list}/summary`, 'system_administrator'))).json()) as { latestDetectedAt: string | null }).latestDetectedAt;
    const unresolved = (await items(backend, { status: 'unresolved' })).sort((a, b) => b.lastDetectedAt.localeCompare(a.lastDetectedAt));

    expect(await latest()).toBe(unresolved[0].lastDetectedAt);
    await post(backend, unresolved[0].id, 'resolve', { resolution: 'false_alarm' });
    expect(await latest()).toBe(unresolved[1].lastDetectedAt);
    for (const alert of unresolved.slice(1)) await post(backend, alert.id, 'resolve', { resolution: 'false_alarm' });
    expect(await latest()).toBeNull();
  });

  it('acknowledges an open alert, which then leaves the open ones', async () => {
    const backend = createMockBackend('no_approved_scores');
    const [first] = await items(backend, { status: 'open' });

    const response = await post(backend, first.id, 'acknowledge');

    expect(response.status).toBe(200);
    expect(await response.json()).toMatchObject({ id: first.id, status: 'acknowledged', acknowledgedAt: '2026-10-16T09:00:00Z', resolvedAt: null, resolution: null });
    expect((await items(backend, { status: 'open' })).map((item) => item.id)).not.toContain(first.id);
  });

  it('refuses to acknowledge an alert that is not open', async () => {
    const backend = createMockBackend('no_approved_scores');
    const [acknowledged] = await items(backend, { status: 'acknowledged' });
    const [resolved] = await items(backend, { status: 'resolved' });

    for (const alert of [acknowledged, resolved]) {
      const response = await post(backend, alert.id, 'acknowledge');
      expect(response.status).toBe(409);
      expect(((await response.json()) as { code: string }).code).toBe('safeguarding.alert_not_open');
    }
  });

  it('resolves an open or acknowledged alert with its outcome, and keeps the first outcome', async () => {
    const backend = createMockBackend('no_approved_scores');
    const [open] = await items(backend, { status: 'open' });
    const [acknowledged] = await items(backend, { status: 'acknowledged' });

    const fromOpen = await post(backend, open.id, 'resolve', { resolution: 'no_action_needed' });
    const fromAcknowledged = await post(backend, acknowledged.id, 'resolve', { resolution: 'referred_to_service' });

    expect(fromOpen.status).toBe(200);
    expect(await fromOpen.json()).toMatchObject({ status: 'resolved', resolvedAt: '2026-10-16T09:00:00Z', resolution: 'no_action_needed', acknowledgedAt: null });
    expect(await fromAcknowledged.json()).toMatchObject({ status: 'resolved', resolution: 'referred_to_service' });
    const again = await post(backend, open.id, 'resolve', { resolution: 'false_alarm' });
    expect(again.status).toBe(409);
    expect(((await again.json()) as { code: string }).code).toBe('safeguarding.alert_resolved');
    expect((await items(backend, { status: 'resolved' })).find((item) => item.id === open.id)?.resolution).toBe('no_action_needed');
  });

  it('answers 404 for an alert it does not have', async () => {
    const backend = createMockBackend('no_approved_scores');
    const missing = '00000000-0000-4000-8000-000000000000';

    for (const [action, body] of [['acknowledge', null], ['resolve', { resolution: 'false_alarm' }]] as const) {
      const response = await post(backend, missing, action, body);
      expect(response.status).toBe(404);
      expect(((await response.json()) as { code: string }).code).toBe('safeguarding.alert_not_found');
    }
  });

  it('refuses an outcome it does not know, or none, without resolving anything', async () => {
    const backend = createMockBackend('no_approved_scores');
    const [first] = await items(backend, { status: 'open' });

    for (const body of [{}, { resolution: 'closed' }, { resolution: 5 }, null]) {
      const response = await post(backend, first.id, 'resolve', body);
      expect(response.status).toBe(400);
      expect(Object.keys(((await response.json()) as { errors: object }).errors)).toEqual(['resolution']);
    }
    expect((await items(backend, { status: 'open' })).map((item) => item.id)).toContain(first.id);
  });
});
