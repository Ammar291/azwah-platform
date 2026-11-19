import { screen, userEvent, waitFor } from '@testing-library/react-native';

import { ApiError } from '@/api/client';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { leadSession } from '@/test-utils/sessionWorlds';

import { NewCheckInButton } from '../NewCheckInButton';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { router, resetRouterMock } = jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

// Demo tools are off in tests by default; results.test.tsx checks the button is then absent.
jest.mock('@/config', () => ({ ...jest.requireActual<typeof import('@/config')>('@/config'), demoToolsEnabled: true }));

const ASSIGNMENT = '0199e0a1-0000-7000-9000-0000000000c1';
const SESSION = '0199e0a1-0000-7000-9000-0000000000c2';
const questionnaire = (sessionId: string) => ({ pathname: '/sessions/[sessionId]', params: { sessionId } });

beforeEach(() => resetRouterMock());
afterEach(async () => {
  await settleQueries();
  jest.restoreAllMocks();
});

describe('New survey (D-144)', () => {
  it('opens a new round on the server and goes straight to its questionnaire', async () => {
    const user = userEvent.setup();
    const { api } = await leadSession();
    const opened = jest
      .spyOn(api, 'openDemoCheckIn')
      .mockResolvedValue({ assignmentId: ASSIGNMENT, activeSessionId: null, waveCode: 'dev-wave-3', opened: true });
    const started = jest.spyOn(api, 'startSession').mockResolvedValue({
      id: SESSION,
      assignmentId: ASSIGNMENT,
      memberId: '0199e0a1-0000-7000-9000-0000000000c3',
      instrumentVersionId: '0199e0a1-0000-7000-9000-0000000000c4',
      versionNumber: 1,
      sectionCode: null,
      status: 'in_progress',
      startedAt: '2026-11-11T09:00:00Z',
      submittedAt: null,
      scoredAt: null,
      answeredCount: 0,
      requiredCount: 39,
    });
    await renderWithProviders(<NewCheckInButton />, { api });

    // A quiet text button with a short neutral label (D-142).
    await user.press(screen.getByRole('button', { name: 'Start a new survey now' }));

    await waitFor(() => expect(router.push).toHaveBeenCalledWith(questionnaire(SESSION)));
    expect(router.dismissTo).toHaveBeenCalledWith('/home');
    expect(opened).toHaveBeenCalledTimes(1);
    expect(started).toHaveBeenCalledWith(ASSIGNMENT);
    expect(screen.getByText('New survey')).toBeOnTheScreen();
  });

  it('opens the session already under way in a round still open', async () => {
    const user = userEvent.setup();
    const { api } = await leadSession();
    jest
      .spyOn(api, 'openDemoCheckIn')
      .mockResolvedValue({ assignmentId: ASSIGNMENT, activeSessionId: SESSION, waveCode: 'dev-wave-2', opened: false });
    const started = jest.spyOn(api, 'startSession');
    await renderWithProviders(<NewCheckInButton />, { api });

    await user.press(screen.getByRole('button', { name: 'Start a new survey now' }));

    await waitFor(() => expect(router.push).toHaveBeenCalledWith(questionnaire(SESSION)));
    expect(started).not.toHaveBeenCalled();
  });

  it('says so when the server refuses, and goes nowhere', async () => {
    const user = userEvent.setup();
    const { api } = await leadSession();
    jest.spyOn(api, 'openDemoCheckIn').mockRejectedValue(new ApiError('not_found', 404, 'demo_tools.disabled'));
    await renderWithProviders(<NewCheckInButton />, { api });

    await user.press(screen.getByRole('button', { name: 'Start a new survey now' }));

    expect(await screen.findByText("Couldn't start. Try again.")).toBeOnTheScreen();
    expect(router.push).not.toHaveBeenCalled();
  });
});
