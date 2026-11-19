import { ApiError, type FetchLike } from '../../client';
import { createHttpParticipantApi, SCORE_REPORT_TIMEOUT_MS } from '../httpParticipantApi';

const json = (status: number, body: unknown) => ({ status, json: async () => body }) as unknown as Response;

function harness(responses: (() => Response)[]) {
  const calls: { url: string; init?: RequestInit }[] = [];
  const fetchImpl: FetchLike = async (url, init) => {
    calls.push({ url, init });
    const next = responses.shift();
    if (!next) throw new Error(`Unexpected request ${url}`);
    return next();
  };
  const auth = { getAccessToken: jest.fn(async () => 'token-1'), reportUnauthorized: jest.fn() };
  return { api: createHttpParticipantApi({ baseUrl: 'http://api', auth, fetchImpl }), calls };
}

const resource = (code: string, url = `https://example.org/${code}`) => ({
  id: '0199e0a1-0000-7000-8000-0000000000d1',
  code,
  versionNumber: 2,
  nameAr: 'خط الإرشاد',
  nameEn: 'Counselling line',
  summaryAr: 'ملخص',
  summaryEn: 'Summary',
  providerNameAr: 'جهة',
  providerNameEn: 'Provider',
  url,
  categories: ['counselling'],
  audiences: ['families'],
  serviceLanguages: ['ar', 'en'],
});

describe('HTTP participant API: the assistant (D-108)', () => {
  it('reads whether each AI helper has a model connected', async () => {
    const { api, calls } = harness([() => json(200, { assistant: false, consultant: true, scoreReport: true })]);

    await expect(api.getAssistantAvailability()).resolves.toEqual({ assistant: false, consultant: true, scoreReport: true });
    expect(calls[0]?.url).toBe('http://api/api/v1/assistant/availability');
  });

  describe('a written score report', () => {
    const SESSION = '0199e0a1-0000-7000-8000-000000000001';
    const report = {
      sessionId: SESSION,
      language: 'en',
      totalScore: 96,
      tierCode: 'transitional',
      scale: { minimum: 39, maximum: 156, bands: [{ code: 'transitional', min: 39, max: 156 }] },
      summary: 'A summary.',
      strengths: [{ title: 'T', detail: 'D' }],
      weaknesses: [],
      nextSteps: [{ sectionCode: 'wealth_debt', area: 'A', reason: 'R', steps: ['S'], standing: 0.5, authorities: [] }],
      modelName: 'synthetic-model',
      generatedAt: '2026-11-05T09:00:00Z',
    };

    it('posts the language to the session and reads the report', async () => {
      const { api, calls } = harness([() => json(200, report)]);

      await expect(api.createScoreReport(SESSION, 'ar')).resolves.toEqual(report);

      expect(calls[0]?.url).toBe(`http://api/api/v1/sessions/${SESSION}/report`);
      expect(calls[0]?.init?.method).toBe('POST');
      expect(JSON.parse(String(calls[0]?.init?.body))).toEqual({ language: 'ar' });
    });

    it('refuses a report that does not match the contract, and passes on a 503', async () => {
      const { api } = harness([() => json(200, { ...report, scale: null }), () => json(503, { code: 'assistant.unavailable' })]);

      await expect(api.createScoreReport(SESSION, 'en')).rejects.toEqual(new ApiError('invalid_response', 200));
      await expect(api.createScoreReport(SESSION, 'en')).rejects.toMatchObject({ status: 503 });
    });

    it('waits longer than the server gives the model', () => {
      expect(SCORE_REPORT_TIMEOUT_MS).toBeGreaterThan(90_000);
    });
  });

  it('sends the language and the conversation, and reads the reply', async () => {
    const { api, calls } = harness([() => json(200, { reply: 'Try a short walk together.', generatedAt: '2026-10-29T09:00:00Z' })]);
    const messages = [
      { role: 'user' as const, text: 'Ideas for family time?' },
      { role: 'assistant' as const, text: 'What do you enjoy?' },
      { role: 'user' as const, text: 'Being outside.' },
    ];

    await expect(api.requestAssistantReply({ language: 'en', messages })).resolves.toEqual({
      reply: 'Try a short walk together.',
      generatedAt: '2026-10-29T09:00:00Z',
      actions: [],
      sources: [],
    });
    expect(calls[0]?.url).toBe('http://api/api/v1/assistant/replies');
    expect(calls[0]?.init?.method).toBe('POST');
    expect(JSON.parse(String(calls[0]?.init?.body))).toEqual({ language: 'en', messages });
  });

  it('sends the memory and each message channel, and reads the actions, sources and refreshed memory (v2)', async () => {
    const source = { id: '0199e0a1-0000-7000-8000-0000000000d1', name: 'Debt advice', provider: 'Provider', url: 'https://example.org/debt' };
    const { api, calls } = harness([
      () =>
        json(200, {
          reply: 'The debt advice service can help.',
          generatedAt: '2026-11-09T10:00:00Z',
          actions: ['services', 'sos'],
          sources: [source],
          memory: 'The family asked about debts.',
        }),
    ]);
    const messages = [
      { role: 'user' as const, text: 'We talked on the phone', channel: 'voice' as const },
      { role: 'user' as const, text: 'Where can I get help with debts?', channel: 'chat' as const },
    ];

    await expect(api.requestAssistantReply({ language: 'en', memory: 'Earlier: family time.', messages })).resolves.toEqual({
      reply: 'The debt advice service can help.',
      generatedAt: '2026-11-09T10:00:00Z',
      actions: ['services', 'sos'],
      sources: [source],
      memory: 'The family asked about debts.',
    });
    expect(JSON.parse(String(calls[0]?.init?.body))).toEqual({ language: 'en', memory: 'Earlier: family time.', messages });
  });

  it('leaves out unknown or repeated action codes and sources without an https link, rather than failing the reply', async () => {
    const { api } = harness([
      () =>
        json(200, {
          reply: 'Here are some ideas.',
          generatedAt: '2026-11-09T10:00:00Z',
          actions: ['help', 'teleport', 'help', 42, 'home', 'resilience', 'services'],
          sources: [
            { id: 'a', name: 'Plain link', provider: 'P', url: 'http://example.org/a' },
            { id: 'b', name: 'Secure link', provider: 'P', url: 'https://example.org/b' },
            { id: 'c', name: '', provider: 'P', url: 'https://example.org/c' },
          ],
          memory: null,
        }),
    ]);

    const reply = await api.requestAssistantReply({ language: 'en', memory: null, messages: [{ role: 'user', text: 'Ideas?' }] });

    expect(reply.actions).toEqual(['help', 'home', 'resilience']);
    expect(reply.sources.map((entry) => entry.id)).toEqual(['b']);
    expect(reply.memory).toBeNull();
  });

  it('reads the crisis support notice of a reply, an older server leaving it out, and reads any other string as the same notice', async () => {
    const reply = { reply: 'Please call 999.', generatedAt: '2026-11-09T10:00:00Z', actions: ['sos'] };
    const { api } = harness([
      () => json(200, { ...reply, safetyNotice: 'crisis_support' }),
      () => json(200, { ...reply, safetyNotice: null }),
      () => json(200, reply),
      () => json(200, { ...reply, safetyNotice: 'something_new' }),
    ]);
    const request = { language: 'en' as const, messages: [{ role: 'user' as const, text: 'Hello' }] };

    await expect(api.requestAssistantReply(request)).resolves.toMatchObject({ safetyNotice: 'crisis_support' });
    await expect(api.requestAssistantReply(request)).resolves.toMatchObject({ safetyNotice: null });
    expect((await api.requestAssistantReply(request)).safetyNotice).toBeUndefined();
    await expect(api.requestAssistantReply(request)).resolves.toMatchObject({ safetyNotice: 'crisis_support' });
  });

  it('waits up to 45 seconds for an assistant reply, longer than the usual call', async () => {
    const timers = jest.spyOn(globalThis, 'setTimeout');
    const { api } = harness([() => json(200, { reply: 'Hi', generatedAt: '2026-11-09T10:00:00Z' })]);

    await api.requestAssistantReply({ language: 'en', messages: [{ role: 'user', text: 'Hello' }] });

    expect(timers.mock.calls.map(([, delay]) => delay)).toContain(45_000);
    timers.mockRestore();
  });

  it('keeps only the fixed crisis support notice of a 502 or 503, never its text', async () => {
    const { api } = harness([
      () => json(502, { status: 502, code: 'assistant.failed', safetyNotice: 'crisis_support', detail: 'free text' }),
      () => json(503, { status: 503, code: 'assistant.unavailable', safetyNotice: 'crisis_support' }),
      () => json(503, { status: 503, code: 'assistant.unavailable', safetyNotice: 'anything else' }),
      () => json(503, { status: 503, code: 'assistant.unavailable', safetyNotice: null }),
      () => json(503, { status: 503, code: 'assistant.unavailable' }),
    ]);
    const request = { language: 'en' as const, messages: [{ role: 'user' as const, text: 'Hello' }] };

    await expect(api.requestAssistantReply(request)).rejects.toMatchObject({ status: 502, safetyNotice: 'crisis_support' });
    await expect(api.requestAssistantReply(request)).rejects.toMatchObject({ status: 503, safetyNotice: 'crisis_support' });
    await expect(api.requestAssistantReply(request)).rejects.toMatchObject({ status: 503, safetyNotice: 'crisis_support' });
    await expect(api.requestAssistantReply(request)).rejects.toMatchObject({ status: 503, safetyNotice: null });
    await expect(api.requestAssistantReply(request)).rejects.toMatchObject({ status: 503, safetyNotice: null });
  });

  it('says "no model connected" as a 503, and refuses an empty reply', async () => {
    const { api } = harness([
      () => json(503, { type: 'about:blank', title: 'Service Unavailable', status: 503, code: 'assistant.unavailable' }),
      () => json(200, { reply: '', generatedAt: '2026-10-29T09:00:00Z' }),
    ]);
    const request = { language: 'ar' as const, messages: [{ role: 'user' as const, text: 'مرحبا' }] };

    await expect(api.requestAssistantReply(request)).rejects.toMatchObject({ status: 503 });
    await expect(api.requestAssistantReply(request)).rejects.toEqual(new ApiError('invalid_response', 200));
  });
});

describe('HTTP participant API: the support directory (D-110)', () => {
  it('reads every page of the directory', async () => {
    const { api, calls } = harness([
      () => json(200, { items: [resource('a')], page: 1, pageSize: 100, totalCount: 2 }),
      () => json(200, { items: [resource('b')], page: 2, pageSize: 100, totalCount: 2 }),
    ]);

    expect((await api.listSupportResources()).map((entry) => entry.code)).toEqual(['a', 'b']);
    expect(calls.map((call) => call.url)).toEqual([
      'http://api/api/v1/support-resources?page=1&pageSize=100',
      'http://api/api/v1/support-resources?page=2&pageSize=100',
    ]);
  });

  it('refuses an entry whose link is not https', async () => {
    const { api } = harness([
      () => json(200, { items: [resource('a', 'http://example.org/a')], page: 1, pageSize: 100, totalCount: 1 }),
    ]);

    await expect(api.listSupportResources()).rejects.toMatchObject({ kind: 'invalid_response' });
  });

  describe('the organisations in the knowledge graph (D-115)', () => {
    const text = { en: 'Text', ar: 'نص' };
    const authority = (overrides: Record<string, unknown> = {}) => ({
      code: 'a',
      kind: 'government',
      name: text,
      purpose: text,
      emirates: ['abu_dhabi'],
      website: 'https://example.org/a',
      phone: '800 000',
      services: [],
      checkedOn: '2026-11-06',
      ...overrides,
    });

    it('reads them from the API', async () => {
      const { api, calls } = harness([() => json(200, [authority()])]);

      expect((await api.listSupportAuthorities()).map((entry) => entry.code)).toEqual(['a']);
      expect(calls[0]?.url).toBe('http://api/api/v1/support-authorities');
    });

    it.each([
      ['a link that is not https', { website: 'http://example.org/a' }],
      ['an emirate the app cannot name', { emirates: ['atlantis'] }],
      ['a kind it does not know', { kind: 'ministry' }],
    ])('refuses %s', async (_name, overrides) => {
      const { api } = harness([() => json(200, [authority(overrides)])]);

      await expect(api.listSupportAuthorities()).rejects.toMatchObject({ kind: 'invalid_response' });
    });
  });

  it("searches with the directory's own search, in the request body", async () => {
    const { api, calls } = harness([
      () =>
        json(200, {
          outcome: 'matched',
          mode: 'deterministic',
          fallback: null,
          notice: 'information_only',
          matches: [{ ...resource('a'), rank: 1 }],
        }),
    ]);

    const found = await api.searchSupportResources('debt advice');
    expect(found.resources.map((entry) => entry.code)).toEqual(['a']);
    expect(found.safetyNotice).toBeNull();
    expect(calls[0]?.url).toBe('http://api/api/v1/support-resources/search');
    expect(JSON.parse(String(calls[0]?.init?.body))).toEqual({ text: 'debt advice', limit: 10 });
  });

  it('reads the crisis support notice of a search', async () => {
    const body = { outcome: 'no_match', notice: 'information_only', matches: [] };
    const { api } = harness([
      () => json(200, { ...body, safetyNotice: 'crisis_support' }),
      () => json(200, { ...body, safetyNotice: null }),
    ]);

    await expect(api.searchSupportResources('kill myself')).resolves.toEqual({ resources: [], safetyNotice: 'crisis_support' });
    await expect(api.searchSupportResources('debt')).resolves.toEqual({ resources: [], safetyNotice: null });
  });
});
