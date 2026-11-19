import { assistantReplySchema, type AssistantMessage } from '@/api/participant/schemas';

import { simulatedAssistantAnswer } from '../fixtureAssistant';

const ask = (text: string, before = 0): AssistantMessage[] => [
  ...Array.from({ length: before }, (_, index): AssistantMessage => ({ role: index % 2 ? 'assistant' : 'user', text: `Earlier ${index}` })),
  { role: 'user', text },
];

describe('the simulated assistant (development fixtures)', () => {
  it('answers with a labelled simulation, no screens, no sources and no memory for an ordinary question', () => {
    const answer = simulatedAssistantAnswer({ language: 'en', messages: ask('Ideas for family time?') });

    expect(answer).toEqual({
      reply: expect.stringMatching(/^This is a simulated reply: no AI model is connected\./),
      actions: [],
      sources: [],
      memory: null,
      safetyNotice: null,
    });
    // It is what the real API would send: it passes the app's own response schema.
    expect(assistantReplySchema.safeParse({ ...answer, generatedAt: '2026-11-09T10:00:00Z' }).success).toBe(true);
  });

  it('simulates the crisis support notice for one phrase, with SOS first', () => {
    const answer = simulatedAssistantAnswer({ language: 'en', messages: ask('I want to kill myself') });

    expect(answer.safetyNotice).toBe('crisis_support');
    expect(answer.actions[0]).toBe('sos');
  });

  it('suggests SOS for an obvious danger word, in either language', () => {
    expect(simulatedAssistantAnswer({ language: 'en', messages: ask('I feel unsafe at home') }).actions).toEqual(['sos']);
    expect(simulatedAssistantAnswer({ language: 'ar', messages: ask('زوجي يضربني') }).actions).toEqual(['sos']);
    expect(simulatedAssistantAnswer({ language: 'en', messages: ask('Parenting skills, please') }).actions).toEqual([]);
  });

  it("names the sample debt-advice entry when debts or the budget come up, in the request's language", () => {
    const english = simulatedAssistantAnswer({ language: 'en', messages: ask('Where can I get help with debts?') });
    const arabic = simulatedAssistantAnswer({ language: 'ar', messages: ask('كيف أخطط ميزانية الأسرة؟') });

    expect(english.sources).toEqual([
      {
        id: '0199e0a1-0000-7000-9000-000000002002',
        name: 'Example household budget and debt advice (sample entry)',
        provider: 'Sample provider B',
        url: 'https://example.org/azwah-dev/dev_debt_advice',
      },
    ]);
    expect(arabic.sources.map((source) => source.name)).toEqual(['استشارات ميزانية الأسرة والديون (مثال توضيحي)']);
  });

  it('writes a memory marked as simulated once the conversation holds 30 messages', () => {
    expect(simulatedAssistantAnswer({ language: 'en', messages: ask('Hello', 28) }).memory).toBeNull();
    expect(simulatedAssistantAnswer({ language: 'en', messages: ask('Hello', 29) }).memory).toBe(
      '[Simulated summary for development] The family exchanged 30 messages with the assistant.',
    );
    expect(simulatedAssistantAnswer({ language: 'ar', messages: ask('مرحبا', 39) }).memory).toMatch(/^\[ملخص محاكى للتطوير\]/);
  });
});
