import { act, screen, userEvent } from '@testing-library/react-native';

import SessionScreen from '../../../../app/(app)/sessions/[sessionId]/index';
import { AppLockProvider } from '@/security/AppLockContext';
import { createFakeAppState } from '@/security/testing/fakeAppState';
import { createFakeDeviceAuthentication } from '@/security/testing/fakeDeviceAuthentication';
import { createFakeLockTimers } from '@/security/testing/fakeLockTimers';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { leadSession } from '@/test-utils/sessionWorlds';

import { AppLockGate } from '../AppLockGate';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { resetRouterMock, setSearchParams } = jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

beforeEach(() => resetRouterMock());
afterEach(settleQueries);

describe('activity that is not a touch', () => {
  it('counts choosing an answer (a keyboard or screen reader does not touch), so the idle lock waits', async () => {
    // The test library presses controls directly, never through the gate's touch handler, as a screen reader does.
    const user = userEvent.setup();
    const lead = await leadSession();
    setSearchParams({ sessionId: lead.sessionId });
    const timers = createFakeLockTimers();
    await renderWithProviders(
      <AppLockProvider
        dependencies={{ appState: createFakeAppState(), authentication: createFakeDeviceAuthentication(), timers }}
        lockRestoredSession={false}
      >
        <AppLockGate>
          <SessionScreen />
        </AppLockGate>
      </AppLockProvider>,
      { api: lead.api },
    );
    await screen.findByText('Question 3 of 4');

    await act(async () => timers.advance(590_000));
    await user.press(screen.getAllByRole('radio')[0] ?? screen.getByText('Question 3 of 4'));
    await act(async () => timers.advance(590_000));
    expect(screen.queryByTestId('lock-screen')).toBeNull();
    await act(async () => timers.advance(10_000));

    expect(screen.getByTestId('lock-screen')).toBeOnTheScreen();
  });
});
