import { describe, expect, it } from 'vitest';

import { householdResult, privacyRequest, safetyAlert } from '@/test/fixtures';

import { adminPrivacyRequestSchema, adminSafetyAlertSchema, householdResultSchema, pagedSchema } from './schemas';

const closed = privacyRequest({ status: 'closed', closedAt: '2026-10-12T11:00:00+00:00', resolution: 'completed' });

describe('adminPrivacyRequestSchema', () => {
  it('accepts an open request and a closed one with its outcome', () => {
    expect(adminPrivacyRequestSchema.safeParse(privacyRequest()).success).toBe(true);
    expect(adminPrivacyRequestSchema.safeParse(closed).success).toBe(true);
    expect(pagedSchema(adminPrivacyRequestSchema).safeParse({ items: [privacyRequest(), closed], page: 1, pageSize: 20, totalCount: 2 }).success).toBe(true);
  });

  it.each([
    ['a type the platform does not have', { type: 'portability' }],
    ['a status the platform does not have', { status: 'pending' }],
    ['an outcome the platform does not have', { status: 'closed', closedAt: '2026-10-12T11:00:00+00:00', resolution: 'erased' }],
    ['a member ID that is not a UUID', { memberId: 'member-1' }],
    ['a request ID that is not a UUID', { id: '42' }],
    ['a created time that is not ISO 8601', { createdAt: '12/10/2026' }],
    ['a missing household reference', { householdReference: undefined }],
    ['an open request with an outcome', { resolution: 'completed' }],
    ['an open request with a closing time', { closedAt: '2026-10-12T11:00:00+00:00' }],
    ['a closed request without an outcome', { status: 'closed', closedAt: '2026-10-12T11:00:00+00:00' }],
    ['a closed request without a closing time', { status: 'closed', resolution: 'declined' }],
  ])('refuses %s', (_name, overrides) => {
    expect(adminPrivacyRequestSchema.safeParse({ ...privacyRequest(), ...overrides }).success).toBe(false);
  });

  it('refuses a page whose items are malformed', () => {
    const page = { items: [privacyRequest(), { ...privacyRequest(), type: 'unknown' }], page: 1, pageSize: 20, totalCount: 2 };

    expect(pagedSchema(adminPrivacyRequestSchema).safeParse(page).success).toBe(false);
  });
});

describe('householdResultSchema', () => {
  /** The example of the `GET /api/v1/admin/results` contract, as JSON. */
  const contractExample = {
    items: [
      {
        sessionId: '0190f0e2-7777-7000-8000-000000000001',
        householdId: '0190f0e2-1111-7000-8000-000000000001',
        householdReference: 'HH-7K2M-9QXA',
        waveCode: 'wave_2026_h2',
        instrumentCode: 'household_assessment',
        versionNumber: 1,
        sectionCode: null,
        state: 'complete',
        totalScore: 112,
        tierCode: 'transitional',
        computedAt: '2026-10-20T08:15:00Z',
      },
    ],
    page: 1,
    pageSize: 20,
    totalCount: 1,
  };

  it('accepts the contract example', () => {
    const parsed = pagedSchema(householdResultSchema).safeParse(contractExample);

    expect(parsed.error?.issues ?? []).toEqual([]);
    expect(parsed.data?.items[0]).toMatchObject({ totalScore: 112, tierCode: 'transitional', state: 'complete' });
  });

  it.each([
    ['an incomplete result with neither total nor band', { state: 'incomplete', totalScore: null, tierCode: null }],
    ['a band code the dashboard has no name for', { tierCode: 'tier_unnamed' }],
    ['a fractional total', { totalScore: 2.75 }],
    ['a complete result without a band', { tierCode: null }],
    ['a complete result with neither total nor band', { totalScore: null, tierCode: null }],
    ['a module result', { instrumentCode: 'family_voices', sectionCode: 'module_a' }],
  ])('accepts %s', (_name, overrides) => {
    expect(householdResultSchema.safeParse({ ...householdResult(), ...overrides }).success).toBe(true);
  });

  it.each([
    ['a state the platform does not have', { state: 'partial' }],
    ['an incomplete result with a total', { state: 'incomplete', tierCode: null }],
    ['an incomplete result with a band', { state: 'incomplete', totalScore: null }],
    ['a band without a total', { totalScore: null }],
    ['a total that is not a number', { totalScore: '112' }],
    ['an empty band code', { tierCode: '' }],
    ['a session ID that is not a UUID', { sessionId: 'session-1' }],
    ['a computed time that is not ISO 8601', { computedAt: '20/10/2026' }],
    ['a missing household reference', { householdReference: undefined }],
  ])('refuses %s', (_name, overrides) => {
    expect(householdResultSchema.safeParse({ ...householdResult(), ...overrides }).success).toBe(false);
  });
});

describe('adminSafetyAlertSchema', () => {
  it('accepts a source and a detector this dashboard does not know yet, as codes', () => {
    expect(adminSafetyAlertSchema.safeParse(safetyAlert({ source: 'sms_line', detector: 'new_detector.v2' })).success).toBe(true);
    expect(adminSafetyAlertSchema.safeParse(safetyAlert({ source: 'assistant_chat', detector: 'score_band' })).success).toBe(true);
  });

  it.each([
    ['a source that is free text', { source: 'Some free text' }],
    ['a detector that is free text', { detector: 'a detector, with words' }],
    ['a signal code that is free text', { signalCode: 'I want to die' }],
    ['a signal code that is too long', { signalCode: `a${'b'.repeat(64)}` }],
    ['a signal code that starts with a digit', { signalCode: '1abc' }],
    ['a relationship that is free text', { memberRelationship: 'Aunt Mary' }],
    ['an empty relationship', { memberRelationship: '' }],
  ])('refuses %s', (_name, overrides) => {
    expect(adminSafetyAlertSchema.safeParse(safetyAlert(overrides)).success).toBe(false);
  });
});
