import { act, fireEvent, screen, userEvent, waitFor, within } from '@testing-library/react-native';
import { AccessibilityInfo, Text } from 'react-native';

import type { Language } from '@/i18n/languages';
import { AppLockProvider } from '@/security/AppLockContext';
import { createFakeAppState } from '@/security/testing/fakeAppState';
import { createFakeDeviceAuthentication } from '@/security/testing/fakeDeviceAuthentication';
import { createFakeLockTimers } from '@/security/testing/fakeLockTimers';
import type { DevPersonaCode } from '@/dev-fixtures/personas';
import { renderWithProviders } from '@/test-utils/renderWithProviders';

import { AppLockGate } from '../AppLockGate';

const CONTENT = 'Answer 3: most of the time';

/** The gate around some signed-in content. The persona is signed in before rendering, as a restored session is. */
async function renderGate(
  options: { available?: boolean; persona?: DevPersonaCode | null; language?: Language } = {},
) {
  const appState = createFakeAppState();
  const authentication = createFakeDeviceAuthentication({ available: options.available ?? true });
  const timers = createFakeLockTimers();
  const rendered = await renderWithProviders(
    <AppLockProvider dependencies={{ appState, authentication, timers }}>
      <AppLockGate>
        <Text>{CONTENT}</Text>
      </AppLockGate>
    </AppLockProvider>,
    { persona: options.persona, language: options.language },
  );
  return { ...rendered, appState, authentication, timers };
}

async function unlockButton() {
  const button = await screen.findByRole('button', { name: 'Unlock' });
  await waitFor(() => expect(button).toBeEnabled());
  return button;
}

describe('the lock screen', () => {
  it('is all there is to see after a restored session starts, with no development banner (D-142)', async () => {
    await renderGate();

    const lock = screen.getByTestId('lock-screen');
    expect(within(lock).getByRole('header', { name: 'Azwah is locked' })).toBeOnTheScreen();
    expect(within(lock).queryByText(/Development mode/)).toBeNull();
    expect(within(lock).getByText(/Deletes unsent answers from this phone/)).toBeOnTheScreen();
    // Whatever is behind it is hidden from screen readers (the navigator also unmounts the signed-in screens).
    expect(screen.queryByText(CONTENT)).toBeNull();
    expect(screen.getByTestId('app-content', { includeHiddenElements: true })).toHaveProp(
      'importantForAccessibility',
      'no-hide-descendants',
    );
  });

  it("unlocks with the phone's own lock, asked in the person's words", async () => {
    const user = userEvent.setup();
    const { authentication } = await renderGate();

    await user.press(await unlockButton());

    expect(await screen.findByText(CONTENT)).toBeOnTheScreen();
    expect(screen.queryByTestId('lock-screen')).toBeNull();
    expect(authentication.prompts).toEqual([{ promptMessage: 'Unlock Azwah', cancelLabel: 'Cancel' }]);
  });

  it.each([
    ['cancelled', 'Still locked.'],
    ['failed', "Not confirmed. Try again or sign in again."],
  ] as const)('stays locked and says so quietly when unlocking is %s', async (answer, message) => {
    const user = userEvent.setup();
    const { authentication } = await renderGate();
    authentication.willAnswer(answer);

    await user.press(await unlockButton());

    expect(await screen.findByText(message)).toBeOnTheScreen();
    expect(screen.getByTestId('lock-screen')).toBeOnTheScreen();
    expect(screen.queryByText(CONTENT)).toBeNull();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('announces the outcome to screen readers on both platforms', async () => {
    const user = userEvent.setup();
    const announce = jest.spyOn(AccessibilityInfo, 'announceForAccessibility').mockImplementation(() => undefined);
    const { authentication } = await renderGate();
    authentication.willAnswer('cancelled');

    await user.press(await unlockButton());

    await waitFor(() =>
      expect(announce).toHaveBeenCalledWith('Still locked.'),
    );
    announce.mockRestore();
  });

  it('keeps the lock and says so when signing out fails', async () => {
    const user = userEvent.setup();
    const { auth } = await renderGate();
    jest.spyOn(auth, 'signOut').mockRejectedValueOnce(new Error('The keystore is unavailable'));

    await user.press(screen.getByRole('button', { name: 'Sign in again' }));
    await user.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Sign out' }));

    expect(await screen.findByText("Couldn't sign out. Try again.")).toBeOnTheScreen();
    expect(screen.getByTestId('lock-screen')).toBeOnTheScreen();
    expect(auth.getState().status).toBe('signed_in');
    expect(screen.queryByText(CONTENT)).toBeNull();
  });

  it('offers only "Sign in again" on a phone without a screen lock, and says why', async () => {
    await renderGate({ available: false });

    expect(await screen.findByText('No screen lock')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Unlock' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Sign in again' })).toBeEnabled();
  });

  it("switches to \"Sign in again\" when the phone says its lock has been removed", async () => {
    const user = userEvent.setup();
    const { authentication } = await renderGate();
    authentication.willAnswer('unavailable');

    await user.press(await unlockButton());

    expect(await screen.findByText('No screen lock')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Unlock' })).toBeNull();
  });

  it('asks before "Sign in again", warns that unsent answers are deleted, then signs out', async () => {
    const user = userEvent.setup();
    const { auth } = await renderGate();

    await user.press(screen.getByRole('button', { name: 'Sign in again' }));
    const dialog = screen.getByTestId('confirmation-dialog');
    expect(within(dialog).getByRole('header', { name: 'Sign out and in again?' })).toBeOnTheScreen();
    expect(within(dialog).getByText(/Unsent answers will be deleted/)).toBeOnTheScreen();
    await user.press(within(dialog).getByRole('button', { name: 'Sign out' }));

    await waitFor(() => expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'signed_out' }));
    expect(screen.queryByTestId('lock-screen')).toBeNull();
  });

  it('reads in Arabic', async () => {
    await renderGate({ language: 'ar' });

    expect(screen.getByRole('header', { name: 'عزوة مقفلة' })).toBeOnTheScreen();
    expect(await screen.findByRole('button', { name: 'فتح القفل' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'تسجيل الدخول من جديد' })).toBeOnTheScreen();
  });
});

describe('the gate', () => {
  it('lets every touch through while noting it', async () => {
    await renderGate({ persona: null });

    const gate = screen.getByTestId('app-lock-gate');

    expect(gate.props.onStartShouldSetResponderCapture()).toBe(false);
  });

  it('locks after ten minutes without a touch, and a touch anywhere starts the count again', async () => {
    const { timers } = await renderGate();
    const button = await unlockButton();
    await fireEvent.press(button);
    expect(await screen.findByText(CONTENT)).toBeOnTheScreen();

    await act(async () => timers.advance(599_000));
    await fireEvent(screen.getByTestId('app-lock-gate'), 'startShouldSetResponderCapture');
    await act(async () => timers.advance(599_000));
    expect(screen.queryByTestId('lock-screen')).toBeNull();
    await act(async () => timers.advance(1_000));

    expect(screen.getByTestId('lock-screen')).toBeOnTheScreen();
  });

  it('covers the screen while the app is inactive or in the background, signed in or not', async () => {
    const { appState } = await renderGate({ persona: null });
    expect(screen.queryByTestId('privacy-cover')).toBeNull();

    await act(async () => appState.change('inactive'));
    expect(screen.getByTestId('privacy-cover')).toBeOnTheScreen();
    expect(screen.queryByText(CONTENT)).toBeNull();
    await act(async () => appState.change('background'));
    expect(screen.getByTestId('privacy-cover')).toBeOnTheScreen();
    await act(async () => appState.change('active'));

    expect(screen.queryByTestId('privacy-cover')).toBeNull();
    expect(screen.getByText(CONTENT)).toBeOnTheScreen();
  });

  it('locks a signed-in person who comes back after two minutes away', async () => {
    const { appState, timers } = await renderGate();
    const button = await unlockButton();
    await fireEvent.press(button);
    await screen.findByText(CONTENT);

    await act(async () => appState.change('background'));
    await act(async () => timers.advance(120_000));
    await act(async () => appState.change('active'));

    expect(screen.getByTestId('lock-screen')).toBeOnTheScreen();
    expect(screen.queryByTestId('privacy-cover')).toBeNull();
  });

  it('never locks when nobody is signed in', async () => {
    await renderGate({ persona: null });

    expect(screen.queryByTestId('lock-screen')).toBeNull();
    expect(screen.getByText(CONTENT)).toBeOnTheScreen();
  });
});
