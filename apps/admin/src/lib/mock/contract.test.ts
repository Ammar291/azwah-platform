// @vitest-environment node
import { describe, expect, it } from 'vitest';
import type { z } from 'zod';

import {
  adminPrivacyRequestSchema,
  adminSafetyAlertSchema,
  assessmentRowSchema,
  exportCatalogSchema,
  exportSchema,
  householdResultSchema,
  instrumentSchema,
  instrumentVersionSchema,
  instrumentVersionSummarySchema,
  overviewSchema,
  pagedSchema,
  participationRowSchema,
  ruleSetSchema,
  safetyAlertSummarySchema,
  scoreDistributionsSchema,
  waveSchema,
} from '@/lib/api/schemas';
import type { Persona } from '@/lib/personas';

import { createMockBackend } from './backend';

const now = new Date(Date.UTC(2026, 9, 16, 9, 0, 0));
const backend = createMockBackend('approved_scores');

async function get<T>(apiPath: string, persona: Persona, schema: z.ZodType<T>, query: Record<string, string> = {}): Promise<T> {
  const response = await backend.handle({ method: 'GET', apiPath, query: new URLSearchParams(query), body: null, persona, idempotencyKey: null, now });
  expect(response.status).toBe(200);
  const parsed = schema.safeParse(await response.json());
  expect(parsed.error?.issues ?? []).toEqual([]);
  return parsed.data as T;
}

describe('the mock answers in the shapes the dashboard accepts', () => {
  it('for analytics and monitoring', async () => {
    await get('/api/v1/admin/analytics/overview', 'researcher', overviewSchema);
    await get('/api/v1/admin/analytics/overview', 'researcher', overviewSchema, { interval: 'week', from: '2026-08-01' });
    await get('/api/v1/admin/analytics/score-distributions', 'researcher', scoreDistributionsSchema);
    await get('/api/v1/admin/waves', 'program_administrator', pagedSchema(waveSchema));
    await get('/api/v1/admin/assessments', 'program_administrator', pagedSchema(assessmentRowSchema), { pageSize: '100' });
    await get('/api/v1/admin/participation', 'program_administrator', pagedSchema(participationRowSchema), { pageSize: '100' });
  });

  it('for exports', async () => {
    await get('/api/v1/admin/exports/catalog', 'researcher', exportCatalogSchema);
    const created = await backend.handle({
      method: 'POST',
      apiPath: '/api/v1/admin/exports',
      query: new URLSearchParams(),
      body: { dataset: 'assessment_sessions', columns: ['wave_code'], filters: { waveId: null, instrumentCode: 'household_assessment' } },
      persona: 'researcher',
      idempotencyKey: '66666666-6666-4666-8666-666666666666',
      now,
    });
    expect(exportSchema.safeParse(await created.json()).success).toBe(true);
    await get('/api/v1/admin/exports', 'researcher', pagedSchema(exportSchema));
  });

  it('for privacy requests', async () => {
    const list = pagedSchema(adminPrivacyRequestSchema);
    const open = await get('/api/v1/admin/privacy-requests', 'program_administrator', list, { status: 'open', pageSize: '100' });
    await get('/api/v1/admin/privacy-requests', 'program_administrator', list, { status: 'closed', pageSize: '100' });
    const closed = await backend.handle({
      method: 'POST',
      apiPath: `/api/v1/admin/privacy-requests/${open.items[0].id}/close`,
      query: new URLSearchParams(),
      body: { resolution: 'withdrawn' },
      persona: 'program_administrator',
      idempotencyKey: null,
      now,
    });
    expect(closed.status).toBe(200);
    expect(adminPrivacyRequestSchema.safeParse(await closed.json()).error?.issues ?? []).toEqual([]);
  });

  it('for safety alerts', async () => {
    const list = pagedSchema(adminSafetyAlertSchema);
    const all = await get('/api/v1/admin/safety-alerts', 'system_administrator', list, { pageSize: '100' });
    await get('/api/v1/admin/safety-alerts', 'system_administrator', list, { status: 'unresolved', category: 'abuse_or_danger' });
    await get('/api/v1/admin/safety-alerts/summary', 'system_administrator', safetyAlertSummarySchema);
    const post = (path: string, body: unknown) =>
      backend.handle({
        method: 'POST',
        apiPath: `/api/v1/admin/safety-alerts/${path}`,
        query: new URLSearchParams(),
        body,
        persona: 'system_administrator',
        idempotencyKey: null,
        now,
      });
    const open = all.items.find((item) => item.status === 'open');
    const acknowledged = await post(`${open?.id}/acknowledge`, null);
    expect(acknowledged.status).toBe(200);
    expect(adminSafetyAlertSchema.safeParse(await acknowledged.json()).error?.issues ?? []).toEqual([]);
    const resolved = await post(`${open?.id}/resolve`, { resolution: 'false_alarm' });
    expect(resolved.status).toBe(200);
    expect(adminSafetyAlertSchema.safeParse(await resolved.json()).error?.issues ?? []).toEqual([]);
  });

  it('for household results', async () => {
    const list = pagedSchema(householdResultSchema);
    const all = await get('/api/v1/admin/results', 'system_administrator', list, { pageSize: '100' });
    for (let page = 2; page <= Math.ceil(all.totalCount / 100); page++) {
      await get('/api/v1/admin/results', 'system_administrator', list, { page: String(page), pageSize: '100' });
    }
    await get('/api/v1/admin/results', 'system_administrator', list, { reference: all.items[0].householdReference });
  });

  it('for configuration', async () => {
    const instruments = await get('/api/v1/instruments', 'system_administrator', pagedSchema(instrumentSchema));
    for (const instrument of instruments.items) {
      await get(`/api/v1/instruments/${instrument.id}`, 'system_administrator', instrumentSchema);
      const versions = await get(`/api/v1/instruments/${instrument.id}/versions`, 'system_administrator', pagedSchema(instrumentVersionSummarySchema));
      for (const version of versions.items) {
        await get(`/api/v1/instrument-versions/${version.id}`, 'system_administrator', instrumentVersionSchema);
        const ruleSets = await get(`/api/v1/instrument-versions/${version.id}/rule-sets`, 'system_administrator', ruleSetSchema.array());
        for (const ruleSet of ruleSets) await get(`/api/v1/rule-sets/${ruleSet.id}`, 'system_administrator', ruleSetSchema);
      }
    }
  });
});
