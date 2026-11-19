import { userEvent } from '@testing-library/react-native';
import { router, Slot, useLocalSearchParams } from 'expo-router';
import { act, renderRouter, screen } from 'expo-router/testing-library';
import { Text } from 'react-native';

import MainPlacesLayout from '../../../app/(app)/(drawer)/_layout';
import SignedInLayout from '../../../app/(app)/_layout';
import { AppButton } from '@/design-system/AppButton';
import { createTestProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { useLeaveFlow } from '../useLeaveFlow';

/*
 * The real router with the app's signed-in layouts: a finished flow is left the way the screens leave it, and Back
 * from where it lands goes Home, never through the flow's screens.
 */
const text = (words: string) =>
  function StandIn() {
    return <Text>{words}</Text>;
  };

function Assignments() {
  const leaveFlow = useLeaveFlow();
  const { householdId } = useLocalSearchParams<{ householdId: string }>();
  return (
    <AppButton
      label="Back to household"
      onPress={() => leaveFlow({ pathname: '/household/[householdId]', params: { householdId } })}
    />
  );
}

function Review() {
  const leaveFlow = useLeaveFlow();
  const { sessionId } = useLocalSearchParams<{ sessionId: string }>();
  return (
    <AppButton
      label="Submitted"
      onPress={() => leaveFlow({ pathname: '/sessions/[sessionId]/result', params: { sessionId } })}
    />
  );
}

function Result() {
  const leaveFlow = useLeaveFlow();
  return <AppButton label="Go to home" onPress={() => leaveFlow()} />;
}

/** renderRouter switches Jest to fake timers, so providers are made first and userEvent advances those timers. */
async function renderApp() {
  const environment = await createTestProviders();
  const rendering = renderRouter(
    {
      _layout: () => environment.wrap(<Slot />),
      '(app)/_layout': SignedInLayout,
      '(app)/(drawer)/_layout': MainPlacesLayout,
      '(app)/(drawer)/home': text('Home place'),
      '(app)/(drawer)/assistant': text('Assistant place'),
      '(app)/(drawer)/consultant': text('Consultant place'),
      '(app)/(drawer)/resilience': text('Resilience place'),
      '(app)/(drawer)/services': text('Services place'),
      '(app)/(drawer)/settings': text('Settings place'),
      '(app)/household/[householdId]/index': text('Household'),
      '(app)/household/[householdId]/review': text('Review the household'),
      '(app)/household/[householdId]/assignments': Assignments,
      '(app)/sessions/[sessionId]/index': text('Question'),
      '(app)/sessions/[sessionId]/review': Review,
      '(app)/sessions/[sessionId]/result': Result,
      '(app)/emergency': text('Emergency numbers'),
    },
    { initialUrl: '/home' },
  );
  await rendering;
  const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });
  const open = (href: string) => act(() => router.push(href as never));
  return { user, open, pathname: () => rendering.getPathname() };
}

afterEach(async () => {
  await settleQueries();
  jest.useRealTimers();
});

describe('leaving a finished flow', () => {
  it('goes from the confirmed household to the household, and Back from there goes Home', async () => {
    const { user, open, pathname } = await renderApp();
    await open('/household/h1');
    await open('/household/h1/review');
    await open('/household/h1/assignments');

    await user.press(screen.getByRole('button', { name: 'Back to household' }));

    expect(pathname()).toBe('/household/h1');
    expect(screen.getByText('Household')).toBeOnTheScreen();
    await act(() => router.back());
    expect(pathname()).toBe('/home');
    expect(router.canGoBack()).toBe(false);
  });

  it('opens the result over Home once submitted, and Go to home leaves nothing behind', async () => {
    const { user, open, pathname } = await renderApp();
    await open('/sessions/s1');
    await open('/sessions/s1/review');

    await user.press(screen.getByRole('button', { name: 'Submitted' }));
    expect(pathname()).toBe('/sessions/s1/result');
    await act(() => router.back());
    expect(pathname()).toBe('/home');

    await open('/sessions/s1/result');
    await user.press(screen.getByRole('button', { name: 'Go to home' }));
    expect(pathname()).toBe('/home');
    expect(router.canGoBack()).toBe(false);
  });

  it('goes Home from a flow opened from another main place', async () => {
    const { user, open, pathname } = await renderApp();
    await act(() => router.navigate('/resilience'));
    await open('/sessions/s2/review');

    await user.press(screen.getByRole('button', { name: 'Submitted' }));
    await act(() => router.back());

    expect(pathname()).toBe('/home');
    expect(screen.getByText('Home place')).toBeOnTheScreen();
  });
});
