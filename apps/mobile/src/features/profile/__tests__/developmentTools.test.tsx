import { act, render, screen, userEvent, waitFor, within } from '@testing-library/react-native';

import { demonstrationDataOf, type DemonstrationData } from '@/api/participant/demonstrationData';
import type { ParticipantApi } from '@/api/participant/participantApi';
import { useAuth } from '@/auth/AuthContext';
import { SignInPanel } from '@/features/auth/SignInPanel';
import { useUaePassSignIn } from '@/features/auth/useUaePassSignIn';
import { AppLockProvider } from '@/security/AppLockContext';
import { createFakeAppState } from '@/security/testing/fakeAppState';
import { createFakeDeviceAuthentication } from '@/security/testing/fakeDeviceAuthentication';
import { resetRouterMock } from '@/test-utils/expoRouterMock';
import {
  createTestProviders,
  queriesIdle,
  settleQueries,
  type RenderOptions,
} from '@/test-utils/renderWithProviders';

import { ProfileView } from '../ProfileView';

jest.mock('expo-file-system', () => jest.requireActual('@/test-utils/fakeExpoFileSystem'));
jest.mock('expo-sharing', () => ({ isAvailableAsync: jest.fn(), shareAsync: jest.fn() }));
jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));

beforeEach(() => resetRouterMock());
afterEach(settleQueries);

/** Settings while signed in, and the sign-in panel the person lands on once signed out, as the app's guards do. */
function SettingsOrSignIn() {
  const { state } = useAuth();
  const signIn = useUaePassSignIn();
  return state.status === 'signed_in' ? <ProfileView /> : <SignInPanel signIn={signIn} />;
}

async function renderSettings(options: RenderOptions = {}) {
  const { wrap, ...handles } = await createTestProviders(options);
  const result = await render(
    wrap(
      <AppLockProvider
        dependencies={{ appState: createFakeAppState(), authentication: createFakeDeviceAuthentication() }}
        lockRestoredSession={false}
      >
        <SettingsOrSignIn />
      </AppLockProvider>,
    ),
  );
  await queriesIdle();
  return { ...result, ...handles };
}

/** The fixtures' reset, as a quiet control with a neutral name (D-142). */
const RESET = 'Reset data';

const resettable = (api: ParticipantApi): DemonstrationData => {
  const demonstration = demonstrationDataOf(api);
  if (!demonstration) {
    throw new Error('The fixture API can reset its data.');
  }
  return demonstration;
};

const confirmReset = async (user: ReturnType<typeof userEvent.setup>) => {
  await user.press(screen.getByRole('button', { name: RESET }));
  await user.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: RESET }));
};

describe('the data reset in Settings (development fixtures)', () => {
  it('is a quiet "Reset data" with no development section or labels, which resets and signs out (D-142)', async () => {
    const user = userEvent.setup();
    const { api, auth } = await renderSettings();
    const reset = jest.spyOn(resettable(api), 'resetDemonstration');

    expect(screen.queryByText(/Development|Simulat|demo/i)).toBeNull();
    expect(screen.queryByRole('button', { name: 'Component gallery' })).toBeNull();

    await user.press(screen.getByRole('button', { name: RESET }));
    const dialog = screen.getByTestId('confirmation-dialog');
    expect(within(dialog).getByText('Reset all data?')).toBeOnTheScreen();
    expect(within(dialog).queryByText(/demo|simulated/i)).toBeNull();
    await user.press(within(dialog).getByRole('button', { name: RESET }));

    await waitFor(() => expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' }));
    expect(reset).toHaveBeenCalledTimes(1);
    expect(await screen.findByText('Data reset.')).toBeOnTheScreen();
    expect(screen.queryByText(/demo/i)).toBeNull();
  });

  it('has no development tools against the real API, even with an API that could reset', async () => {
    const { api } = await renderSettings();
    screen.unmount();

    await render(
      (await createTestProviders({ mode: 'api', api })).wrap(
        <AppLockProvider
          dependencies={{ appState: createFakeAppState(), authentication: createFakeDeviceAuthentication() }}
          lockRestoredSession={false}
        >
          <ProfileView />
        </AppLockProvider>,
      ),
    );
    await queriesIdle();

    expect(resettable(api)).toBeDefined();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeOnTheScreen();
    expect(screen.queryByText('Development tools')).toBeNull();
    expect(screen.queryByRole('button', { name: RESET })).toBeNull();
  });

  it('asks first, and changes nothing when the person keeps the data', async () => {
    const user = userEvent.setup();
    const { api, auth } = await renderSettings();
    const reset = jest.spyOn(resettable(api), 'resetDemonstration');

    await user.press(screen.getByRole('button', { name: RESET }));
    const dialog = screen.getByTestId('confirmation-dialog');
    expect(within(dialog).getByText('Reset all data?')).toBeOnTheScreen();
    expect(within(dialog).getByText(/Everything on this phone is deleted/)).toBeOnTheScreen();
    await user.press(within(dialog).getByRole('button', { name: 'Cancel' }));

    expect(reset).not.toHaveBeenCalled();
    expect(auth.getState()).toMatchObject({ status: 'signed_in' });
  });

  it('resets every simulated account, signs out and says so on the sign-in screen', async () => {
    const user = userEvent.setup();
    const { api, auth } = await renderSettings();
    const me = await api.getMe();
    const consent = (await api.listMyConsents())[0];
    await api.recordConsent({
      memberId: me.household?.memberId ?? '',
      policyVersionId: consent?.policyVersionId ?? '',
      decision: 'withdraw',
    });

    await confirmReset(user);

    await waitFor(() => expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' }));
    expect(await screen.findByText('Data reset.')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeOnTheScreen();

    // Signing in again finds the household lead as seeded, with consent given.
    await act(() => auth.signIn({ method: 'development_persona', personaCode: 'household_lead' }));
    await waitFor(() => expect(screen.getByRole('button', { name: RESET })).toBeOnTheScreen());
    await queriesIdle();
    expect((await api.listMyConsents()).filter((record) => record.isActive)).toHaveLength(1);
  });

  it('says so and stays signed in when the reset fails', async () => {
    const user = userEvent.setup();
    const { api, auth } = await renderSettings();
    jest.spyOn(resettable(api), 'resetDemonstration').mockRejectedValue(new Error('storage'));

    await confirmReset(user);

    expect(await screen.findByText('Something went wrong')).toBeOnTheScreen();
    expect(screen.queryByTestId('confirmation-dialog')).toBeNull();
    expect(auth.getState()).toMatchObject({ status: 'signed_in' });
  });
});
