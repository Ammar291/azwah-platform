import type { FetchLike } from '../../client';
import { createHttpParticipantApi } from '../httpParticipantApi';

const HOUSEHOLD = '0199e0a1-0000-7000-8000-000000000001';
const MEMBER = '0199e0a1-0000-7000-8000-000000000002';
const SESSION = '0199e0a1-0000-7000-8000-000000000003';

const json = (status: number, body: unknown) => ({ status, json: async () => body }) as unknown as Response;

function harness(responses: ((url: string, init?: RequestInit) => Response)[]) {
  const calls: { url: string; init?: RequestInit }[] = [];
  const fetchImpl: FetchLike = async (url, init) => {
    calls.push({ url, init });
    const next = responses.shift();
    if (!next) throw new Error(`Unexpected request ${url}`);
    return next(url, init);
  };
  const auth = { getAccessToken: jest.fn(async () => 'token-1'), reportUnauthorized: jest.fn() };
  return { api: createHttpParticipantApi({ baseUrl: 'http://api', auth, fetchImpl }), calls, auth };
}

const member = (id: string) => ({
  id,
  birthYear: 1985,
  birthMonth: 4,
  age: 41,
  relationshipToHousehold: null,
  employmentStatus: 'employed',
  isHeadOfHousehold: true,
  isPrimaryIncomeEarner: true,
  status: 'active',
  hasAccount: true,
  isCaller: true,
  createdAt: '2026-10-14T09:30:00.1234567+00:00',
  updatedAt: '2026-10-14T09:30:00Z',
});

describe('HTTP participant API', () => {
  it('registers with the confirmation only, sending no personal detail (D-97)', async () => {
    const { api, calls } = harness([
      () => json(201, { registeredAt: '2026-10-24T08:00:00+00:00' }),
      () => json(200, { subject: 'uaepass-stg|1', roles: ['participant'], household: null, registeredAt: '2026-10-24T08:00:00+00:00' }),
    ]);

    await expect(api.registerMe()).resolves.toEqual({ registeredAt: '2026-10-24T08:00:00+00:00' });
    await expect(api.getMe()).resolves.toMatchObject({ registeredAt: '2026-10-24T08:00:00+00:00' });

    expect(calls[0]?.url).toBe('http://api/api/v1/me/registration');
    expect(calls[0]?.init?.method).toBe('POST');
    expect(JSON.parse(String(calls[0]?.init?.body))).toEqual({ detailsConfirmed: true });
  });

  it('reads an unregistered person as registeredAt null, and a server without the field as unknown', async () => {
    const { api } = harness([
      () => json(200, { subject: 's', roles: ['participant'], household: null, registeredAt: null }),
      () => json(200, { subject: 's', roles: ['participant'], household: null }),
    ]);

    expect((await api.getMe()).registeredAt).toBeNull();
    expect((await api.getMe()).registeredAt).toBeUndefined();
  });

  it('reads /me with the bearer token and validates it', async () => {
    const { api, calls } = harness([
      () =>
        json(200, {
          subject: 'sub-1',
          roles: ['participant'],
          household: {
            householdId: HOUSEHOLD,
            reference: 'HH-7K2M',
            status: 'active',
            role: 'manager',
            memberId: MEMBER,
            rosterConfirmedAt: null,
            actableMemberIds: [MEMBER],
          },
        }),
    ]);

    const me = await api.getMe();

    expect(me.household?.reference).toBe('HH-7K2M');
    expect(calls[0]?.url).toBe('http://api/api/v1/me');
    expect((calls[0]?.init?.headers as Record<string, string>).Authorization).toBe('Bearer token-1');
  });

  it('collects every page of a collection', async () => {
    const { api, calls } = harness([
      () => json(200, { items: [member(MEMBER)], page: 1, pageSize: 100, totalCount: 2 }),
      () => json(200, { items: [member(HOUSEHOLD)], page: 2, pageSize: 100, totalCount: 2 }),
    ]);

    const members = await api.listMembers(HOUSEHOLD);

    expect(members).toHaveLength(2);
    expect(calls.map((call) => call.url)).toEqual([
      `http://api/api/v1/households/${HOUSEHOLD}/members?page=1&pageSize=100`,
      `http://api/api/v1/households/${HOUSEHOLD}/members?page=2&pageSize=100`,
    ]);
  });

  it('saves answers with PUT and submits with an Idempotency-Key', async () => {
    const { api, calls } = harness([
      () => json(200, { sessionId: SESSION, status: 'in_progress', responses: [] }),
      () =>
        json(200, { sessionId: SESSION, outcome: 'submitted', status: 'scored', submittedAt: '2026-10-14T10:00:00Z' }),
    ]);

    await api.saveAnswers(SESSION, [{ itemId: MEMBER, optionId: HOUSEHOLD }]);
    const submission = await api.submitSession(SESSION, 'key-123');

    expect(calls[0]?.init?.method).toBe('PUT');
    expect(calls[0]?.init?.body).toBe(JSON.stringify({ responses: [{ itemId: MEMBER, optionId: HOUSEHOLD }] }));
    expect(calls[1]?.url).toBe(`http://api/api/v1/sessions/${SESSION}/submit`);
    expect((calls[1]?.init?.headers as Record<string, string>)['Idempotency-Key']).toBe('key-123');
    expect(submission.outcome).toBe('submitted');
  });

  it('submits demonstration answers with the level and the key, and reads the submission (D-143)', async () => {
    const submitted = { sessionId: SESSION, outcome: 'submitted', status: 'scored', submittedAt: '2026-11-11T09:00:00Z' };
    const { api, calls } = harness([() => json(200, submitted), () => json(200, { ...submitted, outcome: 'maybe' })]);

    await expect(api.submitDemoAnswers(SESSION, 'high', 'key-7')).resolves.toEqual(submitted);
    expect(calls[0]?.url).toBe(`http://api/api/v1/sessions/${SESSION}/demo-submission`);
    expect(calls[0]?.init?.method).toBe('POST');
    expect(JSON.parse(String(calls[0]?.init?.body))).toEqual({ level: 'high' });
    expect((calls[0]?.init?.headers as Record<string, string>)['Idempotency-Key']).toBe('key-7');
    await expect(api.submitDemoAnswers(SESSION, 'low', 'key-8')).rejects.toBeDefined();
  });

  it('opens a new check-in and reads which assignment to answer (D-144)', async () => {
    const opened = { assignmentId: HOUSEHOLD, activeSessionId: null, waveCode: 'demo_wave_3', opened: true };
    const { api, calls } = harness([() => json(200, opened), () => json(200, { ...opened, assignmentId: 'nope' })]);

    await expect(api.openDemoCheckIn()).resolves.toEqual(opened);
    expect(calls[0]?.url).toBe('http://api/api/v1/assignments/demo-check-in');
    expect(calls[0]?.init?.method).toBe('POST');
    await expect(api.openDemoCheckIn()).rejects.toBeDefined();
  });

  it('merge-patches a member with only the fields given, and removes one with status inactive', async () => {
    const { api, calls } = harness([
      () => json(200, { ...member(MEMBER), employmentStatus: 'retired' }),
      () => json(200, { ...member(MEMBER), status: 'inactive' }),
    ]);

    const updated = await api.updateMember(HOUSEHOLD, MEMBER, { employmentStatus: 'retired' });
    const removed = await api.removeMember(HOUSEHOLD, MEMBER);

    expect(calls.map((call) => [call.init?.method, call.url, call.init?.body])).toEqual([
      ['PATCH', `http://api/api/v1/households/${HOUSEHOLD}/members/${MEMBER}`, '{"employmentStatus":"retired"}'],
      ['PATCH', `http://api/api/v1/households/${HOUSEHOLD}/members/${MEMBER}`, '{"status":"inactive"}'],
    ]);
    expect(updated.employmentStatus).toBe('retired');
    expect(removed.status).toBe('inactive');
  });

  it('confirms the roster and validates the assignments the server returns', async () => {
    const { api, calls } = harness([
      () =>
        json(200, {
          householdId: HOUSEHOLD,
          rosterConfirmedAt: '2026-10-14T10:00:00Z',
          waveId: SESSION,
          waveCode: 'W2026-2',
          assignments: [
            { assignmentId: SESSION, memberId: MEMBER, instrumentCode: 'family_voices', sectionCode: 'A', status: 'assigned' },
            { assignmentId: HOUSEHOLD, memberId: MEMBER, instrumentCode: 'family_voices', sectionCode: 'B', status: 'assigned' },
          ],
        }),
      () => json(409, { code: 'households.roster_missing_relationship' }),
    ]);

    const confirmation = await api.confirmRoster(HOUSEHOLD);

    expect(calls[0]?.init?.method).toBe('POST');
    expect(calls[0]?.url).toBe(`http://api/api/v1/households/${HOUSEHOLD}/confirm-roster`);
    expect(confirmation.assignments).toHaveLength(2);
    await expect(api.confirmRoster(HOUSEHOLD)).rejects.toMatchObject({
      kind: 'conflict',
      code: 'households.roster_missing_relationship',
    });
  });

  it('ends the session on 401 and still rejects', async () => {
    const { api, auth } = harness([() => json(401, { status: 401, title: 'Unauthorized' })]);

    await expect(api.getMe()).rejects.toMatchObject({ kind: 'unauthorized' });
    expect(auth.reportUnauthorized).toHaveBeenCalledTimes(1);
  });

  it('does not end the session on 403 or 404', async () => {
    const { api, auth } = harness([
      () => json(403, { code: 'sessions.participation_consent_required' }),
      () => json(404, { code: 'sessions.session_not_found' }),
    ]);

    await expect(api.startSession(SESSION)).rejects.toMatchObject({
      kind: 'forbidden',
      code: 'sessions.participation_consent_required',
    });
    await expect(api.getSession(SESSION)).rejects.toMatchObject({ kind: 'not_found' });
    expect(auth.reportUnauthorized).not.toHaveBeenCalled();
  });

  it('refuses a response that does not match the contract', async () => {
    const { api } = harness([() => json(200, { ...member(MEMBER), status: 'deleted' })]);

    await expect(api.addMember(HOUSEHOLD, {
      birthYear: 1985,
      birthMonth: 4,
      relationshipToHousehold: null,
      employmentStatus: null,
      isHeadOfHousehold: false,
      isPrimaryIncomeEarner: false,
    })).rejects.toMatchObject({ kind: 'invalid_response' });
  });

  it('reports a missing server address without calling anything', async () => {
    const fetchImpl = jest.fn();
    const api = createHttpParticipantApi({
      baseUrl: undefined,
      auth: { getAccessToken: async () => null, reportUnauthorized: jest.fn() },
      fetchImpl,
    });

    await expect(api.getMe()).rejects.toMatchObject({ kind: 'not_configured' });
    expect(fetchImpl).not.toHaveBeenCalled();
  });

  it('accepts a result with a decimal score and category scores', async () => {
    const { api } = harness([
      () =>
        json(200, {
          sessionId: SESSION,
          sessionStatus: 'scored',
          state: 'complete',
          incompleteReason: null,
          totalScore: 41.5,
          tierCode: 'tier_b',
          categoryScores: [{ categoryCode: 'cohesion', score: 12, itemCount: 4 }],
          computedAt: '2026-10-14T10:00:00Z',
          scoreVisibility: 'visible',
          scale: { minimum: 39, maximum: 156, bands: [{ code: 'band', min: 39, max: 156 }] },
        }),
    ]);

    await expect(api.getResult(SESSION)).resolves.toMatchObject({ state: 'complete', totalScore: 41.5 });
  });

  it('accepts a result the server withheld the score from, and refuses one that does not say who may see it', async () => {
    const withheld = {
      sessionId: SESSION,
      sessionStatus: 'scored',
      state: 'complete',
      incompleteReason: null,
      totalScore: null,
      tierCode: null,
      categoryScores: [],
      computedAt: '2026-10-14T10:00:00Z',
      scoreVisibility: 'head_of_household_only',
      scale: null,
    };
    const { scoreVisibility: _omitted, ...unlabelled } = withheld;
    const { api } = harness([() => json(200, withheld), () => json(200, unlabelled)]);

    await expect(api.getResult(SESSION)).resolves.toMatchObject({ scoreVisibility: 'head_of_household_only', totalScore: null });
    await expect(api.getResult(SESSION)).rejects.toBeDefined();
  });

  it('reads the resilience trend and refuses points without the scale they belong to (D-132)', async () => {
    const trend = {
      scoreVisibility: 'visible',
      scale: { minimum: 4, maximum: 12, bands: [{ code: 'band_low', min: 4, max: 12 }] },
      points: [
        { sessionId: SESSION, waveCode: 'wave_1', submittedAt: '2026-10-14T10:00:00Z', totalScore: 7.5, tierCode: null, segment: 0 },
      ],
      notShownCount: 1,
    };
    const { api, calls } = harness([
      () => json(200, trend),
      () => json(200, { ...trend, scale: null }),
      () => json(200, { ...trend, scale: { ...trend.scale, maximum: 4 } }),
    ]);

    await expect(api.getResilienceTrend()).resolves.toEqual(trend);
    expect(calls[0]?.url).toBe('http://api/api/v1/me/resilience-trend');
    expect(calls[0]?.init?.method).toBe('GET');
    await expect(api.getResilienceTrend()).rejects.toBeDefined();
    await expect(api.getResilienceTrend()).rejects.toBeDefined();
  });
});

describe('HTTP participant API: rights requests', () => {
  const REQUEST = '0199e0a1-0000-7000-8000-000000000004';
  const AT = '2026-10-17T08:00:00Z';
  const privacyRequest = (overrides: Record<string, unknown> = {}) => ({
    id: REQUEST,
    memberId: MEMBER,
    type: 'erasure',
    status: 'open',
    createdAt: AT,
    closedAt: null,
    resolution: null,
    ...overrides,
  });
  const dataExport = () => ({
    generatedAt: AT,
    subject: 'sub-1',
    household: {
      householdId: HOUSEHOLD,
      reference: 'HH-7K2M',
      status: 'active',
      role: 'manager',
      memberId: MEMBER,
      rosterConfirmedAt: AT,
      actableMemberIds: [MEMBER],
    },
    members: [member(MEMBER)],
    consents: [
      {
        id: REQUEST,
        memberId: MEMBER,
        consentType: 'participation',
        policyVersionId: HOUSEHOLD,
        policyVersionNumber: 1,
        captureMethod: 'in_app',
        grantedAt: AT,
        withdrawnAt: null,
        isActive: true,
      },
    ],
    assignments: [],
    sessions: [
      {
        session: {
          id: SESSION,
          assignmentId: HOUSEHOLD,
          memberId: MEMBER,
          instrumentVersionId: REQUEST,
          versionNumber: 1,
          sectionCode: 'A',
          status: 'submitted',
          startedAt: AT,
          submittedAt: AT,
          scoredAt: null,
          answeredCount: 1,
          requiredCount: 1,
        },
        answers: [{ itemId: REQUEST, optionId: HOUSEHOLD, answeredAt: AT, lastModifiedAt: AT }],
      },
    ],
    referrals: [
      {
        id: REQUEST,
        memberId: MEMBER,
        sessionId: SESSION,
        categoryCode: 'financial_support',
        status: 'in_progress',
        assignedServiceCode: null,
        resolutionCode: null,
        createdAt: AT,
        updatedAt: AT,
        resolvedAt: null,
        handOff: {
          partnerCode: 'partner_a',
          partnerNameAr: 'شريك',
          partnerNameEn: 'Partner',
          method: 'manual',
          handedOffAt: AT,
        },
      },
    ],
    privacyRequests: [privacyRequest()],
    notIncluded: ['score_results', 'support_recommendations'],
  });

  it('downloads the data export from /me/data-export and validates every part', async () => {
    const { api, calls } = harness([() => json(200, dataExport())]);

    const { data: exported } = await api.exportMyData();

    expect(calls[0]?.url).toBe('http://api/api/v1/me/data-export');
    expect(calls[0]?.init?.method).toBe('GET');
    expect(exported.sessions[0]?.answers).toHaveLength(1);
    expect(exported.referrals[0]?.handOff?.partnerCode).toBe('partner_a');
    expect(exported.notIncluded).toEqual(['score_results', 'support_recommendations']);
  });

  it('keeps the export body exactly as the server sent it, once it has passed validation', async () => {
    const body = { ...dataExport(), addedLater: 'kept', members: [{ ...member(MEMBER), alsoNew: 1 }] };
    const { api } = harness([() => json(200, body)]);

    const download = await api.exportMyData();

    expect(download.raw).toEqual(body);
    expect(download.data).not.toHaveProperty('addedLater');
    expect(download.data.members[0]).not.toHaveProperty('alsoNew');
  });

  it.each([
    ['an answer without its option', (body: ReturnType<typeof dataExport>) => ({
      ...body,
      sessions: [{ ...body.sessions[0], answers: [{ itemId: REQUEST, answeredAt: AT, lastModifiedAt: AT }] }],
    })],
    ['a request of an unknown type', (body: ReturnType<typeof dataExport>) => ({
      ...body,
      privacyRequests: [privacyRequest({ type: 'portability' })],
    })],
    ['no list of what is left out', ({ notIncluded: _left, ...body }: ReturnType<typeof dataExport>) => body],
  ])('refuses an export with %s', async (_case, change) => {
    const { api } = harness([() => json(200, change(dataExport()))]);

    await expect(api.exportMyData()).rejects.toMatchObject({ kind: 'invalid_response' });
  });

  it('reads one page of privacy requests', async () => {
    const closed = privacyRequest({ type: 'access', status: 'closed', closedAt: AT, resolution: 'completed' });
    const { api, calls } = harness([() => json(200, { items: [closed], page: 2, pageSize: 20, totalCount: 21 })]);

    const page = await api.listPrivacyRequests(2);

    expect(calls[0]?.url).toBe('http://api/api/v1/me/privacy-requests?page=2&pageSize=20');
    expect(page).toMatchObject({ page: 2, totalCount: 21, items: [{ resolution: 'completed' }] });
  });

  it('refuses a request whose status or resolution is not in the contract', async () => {
    const { api } = harness([
      () => json(200, { items: [privacyRequest({ status: 'pending' })], page: 1, pageSize: 20, totalCount: 1 }),
      () => json(201, privacyRequest({ resolution: 'ignored' })),
    ]);

    await expect(api.listPrivacyRequests(1)).rejects.toMatchObject({ kind: 'invalid_response' });
    await expect(api.createPrivacyRequest({ type: 'erasure' })).rejects.toMatchObject({ kind: 'invalid_response' });
  });

  it('sends the type, and the member only when one is chosen', async () => {
    const { api, calls } = harness([() => json(201, privacyRequest()), () => json(201, privacyRequest())]);

    const created = await api.createPrivacyRequest({ type: 'erasure', memberId: MEMBER });
    await api.createPrivacyRequest({ type: 'correction' });

    expect(calls.map((call) => [call.init?.method, call.url, call.init?.body])).toEqual([
      ['POST', 'http://api/api/v1/me/privacy-requests', JSON.stringify({ type: 'erasure', memberId: MEMBER })],
      ['POST', 'http://api/api/v1/me/privacy-requests', JSON.stringify({ type: 'correction' })],
    ]);
    expect(created).toMatchObject({ id: REQUEST, status: 'open' });
  });

  it.each([
    [409, 'privacy.request_already_open', 'conflict'],
    [404, 'households.member_not_found', 'not_found'],
    [429, 'requests.rate_limited', 'rate_limited'],
  ] as const)('keeps the problem code of a %i refusal', async (status, code, kind) => {
    const { api } = harness([() => json(status, { status, code })]);

    await expect(api.createPrivacyRequest({ type: 'access' })).rejects.toMatchObject({ kind, status, code });
  });
});
