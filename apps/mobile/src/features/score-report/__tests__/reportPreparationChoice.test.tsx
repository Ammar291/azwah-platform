import { screen, userEvent, waitFor } from '@testing-library/react-native';

import type { ParticipantApi } from '@/api/participant/participantApi';
import { SessionQuestionnaire } from '@/features/sessions/SessionQuestionnaire';
import { useSessionContent } from '@/features/sessions/useSessionContent';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { answerEverything, leadSession } from '@/test-utils/sessionWorlds';

import { createMemoryScoreReportStore } from '../reportStore';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { resetRouterMock, setSearchParams } = jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

function Questionnaire({ sessionId }: { sessionId: string }) {
  const content = useSessionContent(sessionId);
  return content.data ? <SessionQuestionnaire content={content.data} /> : null;
}

async function world(options: { eligible?: boolean; answered?: boolean } = {}) {
  const lead = await leadSession();
  if (options.answered) await answerEverything(lead.api, lead.sessionId);
  setSearchParams({ sessionId: lead.sessionId });
  const createScoreReportPart = jest.fn(lead.api.createScoreReportPart);
  const createScoreReportSummary = jest.fn(lead.api.createScoreReportSummary);
  const api: ParticipantApi = {
    ...lead.api,
    getScoreReportEligibility: async () => ({ eligible: options.eligible ?? true }),
    createScoreReportPart,
    createScoreReportSummary,
  };
  return { api, sessionId: lead.sessionId, createScoreReportPart, createScoreReportSummary, reports: createMemoryScoreReportStore() };
}

beforeEach(() => resetRouterMock());
afterEach(settleQueries);

describe('writing the report as the person answers', () => {
  it('asks first, without naming OpenAI, and sends nothing until the person agrees (D-142)', async () => {
    const { api, sessionId, createScoreReportPart, createScoreReportSummary, reports } = await world({ answered: true });
    await renderWithProviders(<Questionnaire sessionId={sessionId} />, { api, reports });

    expect(await screen.findByTestId('report-choice')).toBeOnTheScreen();
    expect(screen.getByText('Write your report as you answer?')).toBeOnTheScreen();
    expect(screen.queryByText(/OpenAI/)).toBeNull();
    expect(createScoreReportPart).not.toHaveBeenCalled();
    expect(createScoreReportSummary).not.toHaveBeenCalled();
  });

  it('does not ask anyone it does not apply to', async () => {
    const { api, sessionId, reports } = await world({ eligible: false });
    await renderWithProviders(<Questionnaire sessionId={sessionId} />, { api, reports });

    await screen.findByRole('button', { name: 'Next' });
    await settleQueries();
    expect(screen.queryByTestId('report-choice')).toBeNull();
  });

  it('writes what is answered once the person agrees, and remembers the choice', async () => {
    const user = userEvent.setup();
    const { api, sessionId, createScoreReportPart, createScoreReportSummary, reports } = await world({ answered: true });
    await renderWithProviders(<Questionnaire sessionId={sessionId} />, { api, reports });

    await user.press(await screen.findByRole('button', { name: 'Yes' }));

    expect(await screen.findByTestId('report-preparing')).toBeOnTheScreen();
    await waitFor(() => expect(createScoreReportPart).toHaveBeenCalled());
    await waitFor(() => expect(createScoreReportSummary).toHaveBeenCalledTimes(1));
    expect(await reports.getChoice(sessionId)).toBe('on');
    await waitFor(async () => expect((await reports.listParts(sessionId, 'en')).length).toBeGreaterThan(1));
  });

  it('sends nothing after "No thanks", and does not ask again', async () => {
    const user = userEvent.setup();
    const { api, sessionId, createScoreReportPart, reports } = await world({ answered: true });
    await renderWithProviders(<Questionnaire sessionId={sessionId} />, { api, reports });

    await user.press(await screen.findByRole('button', { name: 'No thanks' }));

    await waitFor(() => expect(screen.queryByTestId('report-choice')).toBeNull());
    expect(await reports.getChoice(sessionId)).toBe('off');
    expect(createScoreReportPart).not.toHaveBeenCalled();
  });

  it('can be stopped, which deletes what was written', async () => {
    const user = userEvent.setup();
    const { api, sessionId, reports } = await world({ answered: true });
    await reports.setChoice(sessionId, 'on');
    await renderWithProviders(<Questionnaire sessionId={sessionId} />, { api, reports });
    await waitFor(async () => expect((await reports.listParts(sessionId, 'en')).length).toBeGreaterThan(1));

    await user.press(await screen.findByRole('button', { name: 'Stop' }));

    await waitFor(() => expect(screen.queryByTestId('report-preparing')).toBeNull());
    expect(await reports.getChoice(sessionId)).toBe('off');
    await waitFor(async () => expect(await reports.listParts(sessionId, 'en')).toEqual([]));
  });
});
