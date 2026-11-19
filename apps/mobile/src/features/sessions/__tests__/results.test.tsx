import { screen, userEvent } from '@testing-library/react-native';

import ResultScreen from '../../../../app/(app)/sessions/[sessionId]/result';
import { ApiError } from '@/api/client';
import type { SessionResult } from '@/api/participant/schemas';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { answerEverything, leadSession } from '@/test-utils/sessionWorlds';

import { ResultView } from '../ResultView';
import { RESULT_DISCLOSURE } from '../resultDisclosure';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { router, resetRouterMock, setSearchParams } =
  jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

/** The household lead's earlier session, scored by the fixture as total 8, tier `dev_placeholder_tier`. */
async function scoredWorld() {
  const lead = await leadSession();
  const [scored] = await lead.api.listHistory();
  const sessionId = scored?.sessionId ?? '';
  setSearchParams({ sessionId });
  return { ...lead, scoredId: sessionId };
}

async function submittedWorld() {
  const lead = await leadSession();
  await answerEverything(lead.api, lead.sessionId);
  await lead.api.submitSession(lead.sessionId, 'key-1');
  setSearchParams({ sessionId: lead.sessionId });
  return lead;
}

const complete = (sessionId: string, overrides: Partial<SessionResult> = {}): SessionResult => ({
  sessionId,
  sessionStatus: 'scored',
  state: 'complete',
  incompleteReason: null,
  totalScore: 8,
  tierCode: 'dev_placeholder_tier',
  categoryScores: [{ categoryCode: 'dev_category', score: 3, itemCount: 2 }],
  computedAt: '2026-10-15T09:00:00Z',
  scoreVisibility: 'visible',
  scale: null,
  ...overrides,
});

beforeEach(() => resetRouterMock());
afterEach(async () => {
  await settleQueries();
  jest.restoreAllMocks();
});

describe('the result screen', () => {
  it('confirms the submission and says the result is not ready, without saying why (D-142)', async () => {
    const { api } = await submittedWorld();
    await renderWithProviders(<ResultScreen />, { api });

    expect(await screen.findByText('Thank you!')).toBeOnTheScreen();
    expect(screen.getByText(/^Submitted \d/)).toBeOnTheScreen();
    expect(screen.getByText('Awaiting result')).toBeOnTheScreen();
    expect(screen.getByText('Result not ready yet')).toBeOnTheScreen();
    expect(screen.queryByText(/approv/)).toBeNull();
    // Without demo tools there is no way to open another round from here (D-144).
    expect(screen.queryByTestId('new-check-in')).toBeNull();
  });

  it('leaves the finished questionnaire behind: its screens are dismissed before going on', async () => {
    const user = userEvent.setup();
    const { api } = await submittedWorld();
    await renderWithProviders(<ResultScreen />, { api });

    await user.press(await screen.findByRole('button', { name: 'Questionnaires' }));
    expect(router.dismissTo).toHaveBeenLastCalledWith('/home');
    expect(router.push).toHaveBeenLastCalledWith('/assessments');
    expect(router.dismissTo.mock.invocationCallOrder[0]).toBeLessThan(router.push.mock.invocationCallOrder[0] ?? 0);

    await user.press(screen.getByRole('button', { name: 'Go to Home' }));
    expect(router.dismissTo).toHaveBeenCalledTimes(2);
    expect(router.push).toHaveBeenCalledTimes(1);
    expect(router.replace).not.toHaveBeenCalled();
  });

  it('shows the head of household the total and band as recorded, but no area scores or support', async () => {
    const { api, scoredId } = await scoredWorld();
    const recommendations = jest.spyOn(api, 'listRecommendations');
    await renderWithProviders(<ResultScreen />, { api });

    expect(await screen.findByText("Your family's result")).toBeOnTheScreen();
    expect(screen.getByLabelText('Total score: 8')).toBeOnTheScreen();
    // A code without approved wording is shown exactly as recorded.
    expect(screen.getByLabelText('Band: dev_placeholder_tier')).toBeOnTheScreen();
    expect(screen.queryByText(/dev_category/)).toBeNull();
    expect(RESULT_DISCLOSURE).toEqual({ score: true, tier: true, categoryScores: false, recommendations: false });
    // Not even fetched: nothing sensitive is read that the screen may not show.
    expect(recommendations).not.toHaveBeenCalled();
    expect(scoredId).not.toBe('');
  });

  it('shows the head of household the grading scale and the report offer once the server sends a scale', async () => {
    const { api, scoredId } = await scoredWorld();
    jest.spyOn(api, 'getResult').mockResolvedValue(
      complete(scoredId, {
        totalScore: 96,
        tierCode: 'transitional',
        scale: {
          minimum: 39,
          maximum: 156,
          bands: [
            { code: 'vulnerable', min: 39, max: 78 },
            { code: 'transitional', min: 79, max: 117 },
            { code: 'flourishing', min: 118, max: 156 },
          ],
        },
      }),
    );
    await renderWithProviders(<ResultScreen />, { api });

    expect(await screen.findByTestId('result-scale')).toBeOnTheScreen();
    expect(screen.getByText('Your family: 96')).toBeOnTheScreen();
    expect(await screen.findByRole('button', { name: 'Create my report' })).toBeOnTheScreen();
    expect(screen.queryByText(/aren't written up yet/)).toBeNull();
  });

  it('shows the recorded values, and no scale or report, when the rule set has no scale', async () => {
    const { api } = await scoredWorld();
    await renderWithProviders(<ResultScreen />, { api });

    expect(await screen.findByTestId('result-values')).toBeOnTheScreen();
    expect(screen.queryByText(/written up/)).toBeNull();
    expect(screen.queryByTestId('result-scale')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Create my report' })).toBeNull();
  });

  it('gives anyone but the head of household no scale and no report', async () => {
    const { api, scoredId } = await scoredWorld();
    const createScoreReport = jest.spyOn(api, 'createScoreReport');
    jest.spyOn(api, 'getResult').mockResolvedValue(
      complete(scoredId, { totalScore: null, tierCode: null, categoryScores: [], scoreVisibility: 'head_of_household_only' }),
    );
    await renderWithProviders(<ResultScreen />, { api });

    expect(await screen.findByTestId('result-head-only')).toBeOnTheScreen();
    expect(screen.queryByTestId('result-scale')).toBeNull();
    expect(screen.queryByTestId('report-offer')).toBeNull();
    expect(createScoreReport).not.toHaveBeenCalled();
  });

  it('tells anyone but the head of household that the score is shown to the head only', async () => {
    const { api, scoredId } = await scoredWorld();
    jest.spyOn(api, 'getResult').mockResolvedValue(
      complete(scoredId, { totalScore: null, tierCode: null, categoryScores: [], scoreVisibility: 'head_of_household_only' }),
    );
    await renderWithProviders(<ResultScreen />, { api });

    expect(await screen.findByTestId('result-head-only')).toBeOnTheScreen();
    expect(screen.getByText("Only the head sees the score.")).toBeOnTheScreen();
    expect(screen.queryByText('Total score')).toBeNull();
    expect(screen.queryByTestId('result-values')).toBeNull();
  });

  it("names the band in the reader's language", async () => {
    const { api, scoredId } = await scoredWorld();
    jest.spyOn(api, 'getResult').mockResolvedValue(complete(scoredId, { totalScore: 130, tierCode: 'flourishing' }));
    await renderWithProviders(<ResultScreen />, { api, language: 'ar' });

    expect(await screen.findByLabelText('المستوى: مزدهر')).toBeOnTheScreen();
    expect(screen.getByLabelText('مجموع الدرجات: 130')).toBeOnTheScreen();
  });

  it("reads the result only from the server and never from the person's answers", async () => {
    const { api } = await scoredWorld();
    const answers = jest.spyOn(api, 'getAnswers');
    const result = jest.spyOn(api, 'getResult');
    await renderWithProviders(<ResultScreen />, { api });

    await screen.findByText('Result recorded');

    expect(result).toHaveBeenCalled();
    expect(answers).not.toHaveBeenCalled();
  });

  it('explains an incomplete result with a translated reason', async () => {
    const { api, sessionId } = await submittedWorld();
    jest.spyOn(api, 'getResult').mockResolvedValue(
      complete(sessionId, { state: 'incomplete', incompleteReason: 'non_substantive_share_exceeded', totalScore: null, tierCode: null, categoryScores: [] }),
    );
    await renderWithProviders(<ResultScreen />, { api });

    expect(await screen.findByText("No result")).toBeOnTheScreen();
    expect(screen.getByText(/Too many answers outside the scale/)).toBeOnTheScreen();
  });

  it('says the answers are safe when scoring is temporarily unavailable, and checks again', async () => {
    const user = userEvent.setup();
    const { api, fail } = await submittedWorld();
    fail('getResult', new ApiError('server', 503));
    await renderWithProviders(<ResultScreen />, { api });

    expect(await screen.findByText('Answers submitted')).toBeOnTheScreen();
    expect(screen.getByText("Result can't load now. Try again soon.")).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Check again' }));

    expect(await screen.findByText('Awaiting result')).toBeOnTheScreen();
  });

  it('refuses to show a result for a session that was not submitted', async () => {
    const lead = await leadSession();
    setSearchParams({ sessionId: lead.sessionId });
    await renderWithProviders(<ResultScreen />, { api: lead.api });

    expect(await screen.findByText("Not submitted yet.")).toBeOnTheScreen();
  });
});

describe('ResultView once display is approved', () => {
  const approved = { score: true, tier: true, categoryScores: true, recommendations: true };

  it("shows exactly the API's values, area scores included, and the support it routed this session to", async () => {
    const { api, scoredId } = await scoredWorld();
    const session = await api.getSession(scoredId);
    jest.spyOn(api, 'listRecommendations').mockResolvedValue([
      {
        id: '0199e0a1-0000-7000-9000-0000000000f1',
        memberId: session.memberId,
        sessionId: scoredId,
        categoryCode: 'dev_support_category',
        createdAt: '2026-10-15T09:00:00Z',
        referralId: null,
        referralStatus: null,
      },
      {
        id: '0199e0a1-0000-7000-9000-0000000000f2',
        memberId: session.memberId,
        sessionId: '0199e0a1-0000-7000-9000-0000000000f9',
        categoryCode: 'another_sessions_category',
        createdAt: '2026-10-15T09:00:00Z',
        referralId: null,
        referralStatus: null,
      },
    ]);

    await renderWithProviders(<ResultView result={complete(scoredId)} session={session} disclosure={approved} />, { api });

    expect(screen.getByLabelText('Total score: 8')).toBeOnTheScreen();
    expect(screen.getByLabelText('Band: dev_placeholder_tier')).toBeOnTheScreen();
    expect(screen.getByLabelText('Category dev_category: 3')).toBeOnTheScreen();
    expect(await screen.findByLabelText('Support category: dev_support_category')).toBeOnTheScreen();
    expect(screen.queryByText('another_sessions_category')).toBeNull();
  });

  it('shows only what is approved: a tier without the score', async () => {
    const { api, scoredId } = await scoredWorld();
    const session = await api.getSession(scoredId);

    await renderWithProviders(
      <ResultView
        result={complete(scoredId)}
        session={session}
        disclosure={{ score: false, tier: true, categoryScores: false, recommendations: false }}
      />,
      { api },
    );

    expect(screen.getByLabelText('Band: dev_placeholder_tier')).toBeOnTheScreen();
    expect(screen.queryByText('Total score')).toBeNull();
    expect(screen.queryByText(/dev_category/)).toBeNull();
  });

  it('says how many changes from this phone were not included when it had already been submitted', async () => {
    const { api, scoredId } = await scoredWorld();
    const session = await api.getSession(scoredId);

    await renderWithProviders(<ResultView result={complete(scoredId)} session={session} changesNotIncluded={2} />, { api });

    expect(screen.getByText(/Made after submitting: 2\./)).toBeOnTheScreen();
  });
});
