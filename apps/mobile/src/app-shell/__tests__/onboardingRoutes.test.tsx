import { act, fireEvent, renderRouter, screen, waitFor, within } from 'expo-router/testing-library';
import { Text } from 'react-native';

import AccountReadyRoute from '../../../app/(onboarding)/account-ready';
import ConfirmDetailsRoute from '../../../app/(onboarding)/confirm-details';
import OnboardingLayout from '../../../app/(onboarding)/_layout';
import MainPlacesLayout from '../../../app/(app)/(drawer)/_layout';
import SignedInLayout from '../../../app/(app)/_layout';
import PublicLayout from '../../../app/(public)/_layout';
import Index from '../../../app/index';
import type { DevPersonaCode } from '@/dev-fixtures/personas';
import { createFakeAppState } from '@/security/testing/fakeAppState';
import { createFakeDeviceAuthentication } from '@/security/testing/fakeDeviceAuthentication';
import { createTestProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { AppShell } from '../AppShell';

/*
 * The first sign-in with the real router, the app's navigator and the real onboarding screens (D-97): a person the
 * server has not registered confirms their details once, sees "account ready", then Home; a returning person goes
 * straight to Home. The fixture API answers `registeredAt` as the server does.
 */
const HOME = 'Home: your household and answers';

// Each test walks several screens through the real router.
jest.setTimeout(20_000);

async function renderSignedOutApp() {
  const environment = await createTestProviders({ persona: null });
  function RootLayout() {
    return environment.wrap(
      <AppShell
        settings={environment.settings}
        lockDependencies={{ appState: createFakeAppState(), authentication: createFakeDeviceAuthentication() }}
        exportFiles={{ clearAll: jest.fn(async () => undefined) }}
      />,
    );
  }
  const rendering = renderRouter({
    _layout: RootLayout,
    index: Index,
    '(public)/_layout': PublicLayout,
    '(public)/welcome': () => <Text>Welcome</Text>,
    '(public)/auth/sign-in': () => <Text>Sign in</Text>,
    '(onboarding)/_layout': OnboardingLayout,
    '(onboarding)/confirm-details': ConfirmDetailsRoute,
    '(onboarding)/account-ready': AccountReadyRoute,
    '(app)/_layout': SignedInLayout,
    '(app)/(drawer)/_layout': MainPlacesLayout,
    '(app)/(drawer)/home': () => <Text>{HOME}</Text>,
  });
  await rendering;
  await screen.findByText('Welcome');
  const signInAs = (personaCode: DevPersonaCode) =>
    act(async () => environment.auth.signIn({ method: 'development_persona', personaCode }));
  return { ...environment, signInAs, pathname: () => rendering.getPathname() };
}

afterEach(async () => {
  await settleQueries();
  jest.useRealTimers();
});

describe('the first sign-in', () => {
  it('asks a new person to confirm their details once, shows account ready, then Home', async () => {
    const { signInAs, pathname } = await renderSignedOutApp();

    await signInAs('new_participant');

    expect(await screen.findByRole('header', { name: 'Confirm your details' })).toBeOnTheScreen();
    expect(pathname()).toBe('/confirm-details');
    expect(screen.getByText('Omar Khalid Al Suwaidi')).toBeOnTheScreen();
    expect(screen.getByText('784-••••-•••••78-9')).toBeOnTheScreen();
    // A simulated account's details carry no "Simulated" source in the app (D-142).
    expect(screen.queryByText('Simulated')).toBeNull();
    const confirm = screen.getByRole('button', { name: 'Confirm' });
    expect(confirm).toBeDisabled();

    await fireEvent.press(screen.getByRole('switch', { name: 'These details are mine and correct' }));
    await fireEvent.press(confirm);

    expect(await screen.findByRole('header', { name: 'Welcome, Omar' })).toBeOnTheScreen();
    expect(pathname()).toBe('/account-ready');

    await fireEvent.press(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByText(HOME)).toBeOnTheScreen();
    expect(pathname()).toBe('/home');
  });

  it('takes a returning person straight to Home, skipping the details', async () => {
    const { signInAs, pathname } = await renderSignedOutApp();

    await signInAs('household_lead');

    expect(await screen.findByText(HOME)).toBeOnTheScreen();
    expect(pathname()).toBe('/home');
    expect(screen.queryByText('Confirm your details')).toBeNull();
  });

  it('signs out a person who says the details are not theirs', async () => {
    const { signInAs, auth } = await renderSignedOutApp();
    await signInAs('new_participant');
    await screen.findByRole('header', { name: 'Confirm your details' });

    await fireEvent.press(screen.getByRole('button', { name: 'Not my details' }));
    const dialog = await screen.findByTestId('confirmation-dialog');
    expect(within(dialog).getByText(/Fix wrong details in UAE PASS/)).toBeOnTheScreen();
    await fireEvent.press(within(dialog).getByRole('button', { name: 'Sign out' }));

    expect(await screen.findByText('Welcome')).toBeOnTheScreen();
    await waitFor(() => expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' }));
  });
});
