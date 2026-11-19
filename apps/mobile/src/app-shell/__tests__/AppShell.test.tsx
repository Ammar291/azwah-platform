import { router } from 'expo-router';
import { act, fireEvent, renderRouter, screen, waitFor, within } from 'expo-router/testing-library';
import { Text } from 'react-native';

import OnboardingLayout from '../../../app/(onboarding)/_layout';
import MainPlacesLayout from '../../../app/(app)/(drawer)/_layout';
import SignedInLayout from '../../../app/(app)/_layout';
import PublicLayout from '../../../app/(public)/_layout';
import Index from '../../../app/index';
import { isDevelopmentMockAuth } from '@/auth/developmentMockAuth';
import type { DevPersonaCode } from '@/dev-fixtures/personas';
import { createFakeAppState } from '@/security/testing/fakeAppState';
import { createFakeDeviceAuthentication } from '@/security/testing/fakeDeviceAuthentication';
import { createTestProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { AppShell } from '../AppShell';

/*
 * The real router (expo-router/testing-library) with the app's own navigator, anchor route and group layouts. The
 * screens are stand-ins that show something private, so a test can prove nothing of them is mounted behind the lock
 * (Review Focus 4 of the security follow-ups plan).
 */
const HOME = 'Home: your household and answers';
const SESSION = 'Question 3: most of the time';
const SIGNED_IN_TEXT = /your household and answers|most of the time|Consent choices/;

async function renderApp({ persona = 'household_lead' }: { persona?: DevPersonaCode | null } = {}) {
  const environment = await createTestProviders({ persona });
  const appState = createFakeAppState();
  const authentication = createFakeDeviceAuthentication();
  const exportFiles = { clearAll: jest.fn(async () => undefined) };
  function RootLayout() {
    return environment.wrap(
      <AppShell
        settings={environment.settings}
        lockDependencies={{ appState, authentication }}
        exportFiles={exportFiles}
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
    '(onboarding)/confirm-details': () => <Text>Confirm your details</Text>,
    '(app)/_layout': SignedInLayout,
    '(app)/(drawer)/_layout': MainPlacesLayout,
    '(app)/(drawer)/home': () => <Text>{HOME}</Text>,
    '(app)/sessions/[sessionId]/index': () => <Text>{SESSION}</Text>,
    consent: () => <Text>Consent choices</Text>,
  });
  await rendering;
  return { ...environment, appState, authentication, exportFiles, pathname: () => rendering.getPathname() };
}

/** Nothing of a signed-in screen is in the tree, hidden or not. */
function expectNoSignedInScreen() {
  expect(screen.queryAllByText(SIGNED_IN_TEXT, { includeHiddenElements: true })).toEqual([]);
}

async function unlock() {
  const button = await screen.findByRole('button', { name: 'Unlock' });
  await waitFor(() => expect(button).toBeEnabled());
  await fireEvent.press(button);
}

afterEach(async () => {
  await settleQueries();
  jest.useRealTimers();
});

describe('the app shell and its lock', () => {
  it('starts locked when a session is restored, with no signed-in screen mounted', async () => {
    const { pathname } = await renderApp();

    expect(await screen.findByRole('header', { name: 'Azwah is locked' })).toBeOnTheScreen();
    expectNoSignedInScreen();
    expect(pathname()).toBe('/');
  });

  it('shows home after unlocking, and unmounts every signed-in screen when it locks again', async () => {
    await renderApp();

    await unlock();
    expect(await screen.findByText(HOME)).toBeOnTheScreen();
    await act(async () => jest.advanceTimersByTime(600_000));

    expect(await screen.findByRole('header', { name: 'Azwah is locked' })).toBeOnTheScreen();
    expectNoSignedInScreen();
  });

  it('returns to the screen the person was on when the app locked', async () => {
    const { appState, pathname } = await renderApp();
    await unlock();
    await screen.findByText(HOME);
    await act(async () => router.push('/sessions/s-1'));
    expect(await screen.findByText(SESSION)).toBeOnTheScreen();

    await act(async () => appState.change('background'));
    await act(async () => jest.advanceTimersByTime(120_000));
    await act(async () => appState.change('active'));
    expect(await screen.findByTestId('lock-screen')).toBeOnTheScreen();
    expectNoSignedInScreen();
    await unlock();

    expect(await screen.findByText(SESSION)).toBeOnTheScreen();
    expect(pathname()).toBe('/sessions/s-1');
  });

  it('"Sign in again" signs out, deletes answers not yet sent, and leads to the welcome screen', async () => {
    const { drafts, settings } = await renderApp();
    await waitFor(async () => expect(await settings.get('drafts_owner')).toBe('dev-mock|household_lead'));
    await drafts.saveAnswer({ sessionId: 's-1', itemId: 'i-1', optionId: 'o-1', baseModifiedAt: null, at: new Date() });

    const signInAgain = await screen.findByRole('button', { name: 'Sign in again' });
    await fireEvent.press(signInAgain);
    const dialog = screen.getByTestId('confirmation-dialog');
    await fireEvent.press(within(dialog).getByRole('button', { name: 'Sign out' }));

    expect(await screen.findByText('Welcome')).toBeOnTheScreen();
    await waitFor(async () => expect(await drafts.listSessionsWithPending()).toEqual([]));
    expect(screen.queryByTestId('lock-screen')).toBeNull();
  });

  it('does not lock someone who signs in on this run of the app', async () => {
    const { auth } = await renderApp({ persona: null });
    expect(await screen.findByText('Welcome')).toBeOnTheScreen();

    await act(async () => auth.signIn({ method: 'development_persona', personaCode: 'household_lead' }));

    expect(await screen.findByText(HOME)).toBeOnTheScreen();
    expect(screen.queryByTestId('lock-screen')).toBeNull();
  });

  it('goes back to the locked screen once only: the start route later leads home', async () => {
    const { appState, pathname } = await renderApp();
    await unlock();
    await screen.findByText(HOME);
    await act(async () => router.push('/sessions/s-1'));
    await screen.findByText(SESSION);
    await act(async () => appState.change('background'));
    await act(async () => jest.advanceTimersByTime(120_000));
    await act(async () => appState.change('active'));
    await unlock();
    await screen.findByText(SESSION);

    await act(async () => router.replace('/'));

    expect(await screen.findByText(HOME)).toBeOnTheScreen();
    expect(pathname()).toBe('/home');
  });

  it('empties the export folder at start, each time the lock engages, and at sign-out', async () => {
    const { exportFiles } = await renderApp();
    await waitFor(() => expect(exportFiles.clearAll).toHaveBeenCalled());
    const atStart = exportFiles.clearAll.mock.calls.length;

    await unlock();
    await screen.findByText(HOME);
    await act(async () => jest.advanceTimersByTime(600_000));
    await screen.findByRole('header', { name: 'Azwah is locked' });
    expect(exportFiles.clearAll).toHaveBeenCalledTimes(atStart + 1);

    const signInAgain = await screen.findByRole('button', { name: 'Sign in again' });
    await fireEvent.press(signInAgain);
    await fireEvent.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Sign out' }));
    await screen.findByText('Welcome');

    await waitFor(() => expect(exportFiles.clearAll).toHaveBeenCalledTimes(atStart + 2));
  });

  it('counts moving to another screen as activity, so the idle lock waits', async () => {
    await renderApp();
    await unlock();
    await screen.findByText(HOME);

    await act(async () => jest.advanceTimersByTime(590_000));
    await act(async () => router.push('/sessions/s-1'));
    await screen.findByText(SESSION);
    await act(async () => jest.advanceTimersByTime(590_000));
    expect(screen.queryByTestId('lock-screen')).toBeNull();
    await act(async () => jest.advanceTimersByTime(10_000));

    expect(await screen.findByTestId('lock-screen')).toBeOnTheScreen();
  });

  it('empties the export folder when a session expires too', async () => {
    const { auth, exportFiles } = await renderApp();
    await unlock();
    await screen.findByText(HOME);
    const before = exportFiles.clearAll.mock.calls.length;
    if (!isDevelopmentMockAuth(auth)) throw new Error('expected the development mock');

    await act(async () => auth.expireSession());

    expect(await screen.findByText('Sign in')).toBeOnTheScreen();
    await waitFor(() => expect(exportFiles.clearAll).toHaveBeenCalledTimes(before + 1));
  });
});
