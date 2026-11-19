import { screen, userEvent, waitFor } from '@testing-library/react-native';

import SessionScreen from '../../../../app/(app)/sessions/[sessionId]/index';
import { ApiError } from '@/api/client';
import { createDeviceDrafts } from '@/test-utils/deviceDrafts';
import { createFakeConnectivity } from '@/test-utils/fakeConnectivity';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { leadSession } from '@/test-utils/sessionWorlds';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { router, resetRouterMock, setSearchParams } =
  jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

// Demo tools are off in tests by default (answering.test.tsx checks the buttons are then absent).
jest.mock('@/config', () => ({ ...jest.requireActual<typeof import('@/config')>('@/config'), demoToolsEnabled: true }));

const SLOW = { timeout: 3000 };

async function world() {
  const lead = await leadSession();
  setSearchParams({ sessionId: lead.sessionId });
  return lead;
}

const submittedBy = (sessionId: string) => ({
  sessionId,
  outcome: 'submitted' as const,
  status: 'scored' as const,
  submittedAt: '2026-11-11T09:00:00Z',
});

const resultOf = (sessionId: string) => ({ pathname: '/sessions/[sessionId]/result', params: { sessionId } });

beforeEach(() => resetRouterMock());
afterEach(async () => {
  await settleQueries();
  jest.restoreAllMocks();
});

describe('the demo buttons (D-130, D-143)', () => {
  it('finish the questionnaire in one tap: the server answers, submits and scores, and the result opens', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    const submit = jest.spyOn(api, 'submitDemoAnswers').mockResolvedValue(submittedBy(sessionId));
    const saveAnswers = jest.spyOn(api, 'saveAnswers');
    await renderWithProviders(<SessionScreen />, { api });

    // Three quiet buttons, Low, Medium and High, with no card or explanation around them (D-142).
    expect(await screen.findByRole('button', { name: 'Low: answer and send for a low resilience result' })).toHaveTextContent('Low');
    expect(screen.queryByText(/demo|made-up/i)).toBeNull();
    await user.press(screen.getByRole('button', { name: 'High: answer and send for a high resilience result' }));

    await waitFor(() => expect(router.push).toHaveBeenCalledWith(resultOf(sessionId)), SLOW);
    // Over Home, with the questionnaire and any review left behind.
    expect(router.dismissTo).toHaveBeenCalledWith('/home');
    expect(submit).toHaveBeenCalledWith(sessionId, 'high', expect.any(String));
    // The phone chose no answer and saved none itself: the review and its submit button are skipped too.
    expect(saveAnswers).not.toHaveBeenCalled();
    expect(router.push).not.toHaveBeenCalledWith(expect.objectContaining({ pathname: '/sessions/[sessionId]/review' }));
  });

  it('open the result when the server had already closed the session', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    jest.spyOn(api, 'submitDemoAnswers').mockRejectedValue(new ApiError('conflict', 409, 'sessions.not_in_progress'));
    await renderWithProviders(<SessionScreen />, { api });

    await user.press(await screen.findByRole('button', { name: /^Medium:/ }));

    await waitFor(() => expect(router.push).toHaveBeenCalledWith(resultOf(sessionId)), SLOW);
  });

  it('say so when the server refuses, and leave the answers as they were', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    const before = await api.getAnswers(sessionId);
    jest.spyOn(api, 'submitDemoAnswers').mockRejectedValue(new ApiError('not_found', 404, 'demo_tools.disabled'));
    await renderWithProviders(<SessionScreen />, { api });

    await user.press(await screen.findByRole('button', { name: /^Low:/ }));

    expect(await screen.findByText("Couldn't finish. Try again.")).toBeOnTheScreen();
    expect(router.push).not.toHaveBeenCalled();
    expect((await api.getAnswers(sessionId)).responses).toEqual(before.responses);
    // Ready for another try.
    expect(screen.getByRole('button', { name: /^Low:/ })).toBeEnabled();
  });

  it('say there is no connection, and send nothing, while offline', async () => {
    const user = userEvent.setup();
    const { api } = await world();
    const connectivity = createFakeConnectivity();
    const submit = jest.spyOn(api, 'submitDemoAnswers');
    await renderWithProviders(<SessionScreen />, { api, connectivity });
    await screen.findByRole('button', { name: /^High:/ });
    connectivity.set('offline');

    await user.press(screen.getByRole('button', { name: /^High:/ }));

    expect(await screen.findByText('No connection. Try again.')).toBeOnTheScreen();
    expect(submit).not.toHaveBeenCalled();
  });

  it('are switched off once the time to answer has passed, like the answers (D-139)', async () => {
    const user = userEvent.setup();
    const { api, fail } = await world();
    const phone = await createDeviceDrafts();
    fail('saveAnswers', new ApiError('conflict', 409, 'assignments.assignment_expired'));
    const submit = jest.spyOn(api, 'submitDemoAnswers');
    await renderWithProviders(<SessionScreen />, { api, drafts: phone.drafts });

    await user.press(await screen.findByRole('radio', { name: '[DEV] Option 2' }));
    await screen.findByTestId('session-expired', {}, SLOW);

    expect(screen.getByRole('button', { name: /^Low:/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: /^High:/ })).toBeDisabled();
    expect(submit).not.toHaveBeenCalled();
    phone.close();
  });
});
