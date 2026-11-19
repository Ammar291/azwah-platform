import { userEvent } from '@testing-library/react-native';
import { Slot, useLocalSearchParams } from 'expo-router';
import { act, renderRouter, screen, within } from 'expo-router/testing-library';
import { Text } from 'react-native';

import MainPlacesLayout from '../../../../app/(app)/(drawer)/_layout';
import SignedInLayout from '../../../../app/(app)/_layout';
import { AppScreen } from '@/components/AppScreen';
import type { ParticipantApi } from '@/api/participant/participantApi';
import { createFixtureParticipantApi } from '@/dev-fixtures/fixtureParticipantApi';
import type { Language } from '@/i18n/languages';
import { createTestProviders, settleQueries } from '@/test-utils/renderWithProviders';

/*
 * The real router with the app's signed-in layouts: the side menu (D-106) over stand-in places, each framed by
 * AppScreen with its menu button as the real screens are.
 */
const place = (title: string) =>
  function Place() {
    return (
      <AppScreen title={title} menu>
        <Text>{`${title}: content`}</Text>
      </AppScreen>
    );
  };

function HouseholdStandIn() {
  const { householdId } = useLocalSearchParams<{ householdId: string }>();
  return <Text>{`Household ${householdId}`}</Text>;
}

/** renderRouter switches Jest to fake timers, so the household is read first and userEvent advances those timers. */
async function renderMenu({ language = 'en', api }: { language?: Language; api?: ParticipantApi } = {}) {
  const environment = await createTestProviders({ language, api });
  const { household } = await environment.api.getMe();
  const rendering = renderRouter(
    {
      _layout: () => environment.wrap(<Slot />),
      '(app)/_layout': SignedInLayout,
      '(app)/(drawer)/_layout': MainPlacesLayout,
      '(app)/(drawer)/home': place('Home'),
      '(app)/(drawer)/assistant': place('Azwah assistant'),
      '(app)/(drawer)/consultant': place('Azwah consultant'),
      '(app)/(drawer)/resilience': place('Your family resilience'),
      '(app)/(drawer)/services': place('All services'),
      '(app)/(drawer)/settings': place('Settings'),
      '(app)/household/[householdId]/index': HouseholdStandIn,
      '(app)/emergency': () => <Text>Emergency numbers</Text>,
    },
    { initialUrl: '/home' },
  );
  await rendering;
  const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });
  return { ...environment, household, user, pathname: () => rendering.getPathname() };
}

const menuItems = () => screen.getAllByRole('menuitem').map((item) => item.props.accessibilityLabel as string);

afterEach(async () => {
  await settleQueries();
  jest.useRealTimers();
});

describe('the side menu', () => {
  it('lists the main places in order, and ends with sign out', async () => {
    const { household, user } = await renderMenu();

    await user.press(await screen.findByRole('button', { name: 'Open the menu' }));

    expect(menuItems()).toEqual([
      'Home',
      'Azwah assistant',
      'Azwah consultant',
      'Your household',
      'Your family resilience',
      'All services',
      'Settings',
    ]);
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeOnTheScreen();
    expect(await screen.findByText(`Ref. ${household?.reference}`)).toBeOnTheScreen();
  });

  it('marks the open place, and opens the one chosen in its place', async () => {
    const { pathname, user } = await renderMenu();

    expect(await screen.findByRole('menuitem', { name: 'Home', selected: true })).toBeOnTheScreen();

    await user.press(screen.getByRole('menuitem', { name: 'Settings' }));

    expect(pathname()).toBe('/settings');
    expect(screen.getByRole('header', { name: 'Settings' })).toBeOnTheScreen();
    expect(screen.getByRole('menuitem', { name: 'Settings', selected: true })).toBeOnTheScreen();
    expect(screen.getByRole('menuitem', { name: 'Home', selected: false })).toBeOnTheScreen();

    await user.press(screen.getByRole('menuitem', { name: 'Your family resilience' }));
    expect(pathname()).toBe('/resilience');
    await user.press(screen.getByRole('menuitem', { name: 'All services' }));
    expect(pathname()).toBe('/services');
    await user.press(screen.getByRole('menuitem', { name: 'Azwah consultant' }));
    expect(pathname()).toBe('/consultant');
  });

  it('says which AI helpers are not switched on, when the server says no model is connected', async () => {
    const fixture = createFixtureParticipantApi({ getSubject: () => 'dev-mock|household_lead', latencyMs: 0 });
    await renderMenu({ api: { ...fixture, getAssistantAvailability: async () => ({ assistant: false, consultant: true, scoreReport: false }) } });

    expect(await screen.findByRole('menuitem', { name: 'Azwah assistant, Not available yet' })).toBeOnTheScreen();
    expect(screen.getByRole('menuitem', { name: 'Azwah consultant' })).toBeOnTheScreen();
  });

  it("opens the family's household over the main places", async () => {
    const { household, pathname, user } = await renderMenu();
    await screen.findByText(`Ref. ${household?.reference}`);

    await user.press(screen.getByRole('menuitem', { name: 'Your household' }));

    expect(await screen.findByText(`Household ${household?.householdId}`)).toBeOnTheScreen();
    expect(pathname()).toBe(`/household/${household?.householdId}`);
  });

  it('asks before signing out, and signs out only when confirmed', async () => {
    const { auth, user } = await renderMenu();

    await user.press(await screen.findByRole('button', { name: 'Sign out' }));
    const dialog = within(screen.getByTestId('confirmation-dialog'));
    expect(dialog.getByRole('header', { name: 'Sign out?' })).toBeOnTheScreen();
    await user.press(dialog.getByRole('button', { name: 'Cancel' }));
    expect(auth.getState().status).toBe('signed_in');

    await user.press(screen.getByRole('button', { name: 'Sign out' }));
    await act(async () => {
      await user.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Sign out' }));
    });

    expect(auth.getState().status).toBe('signed_out');
  });

  it('reads in Arabic', async () => {
    await renderMenu({ language: 'ar' });

    expect(await screen.findByRole('button', { name: 'افتح القائمة' })).toBeOnTheScreen();
    expect(menuItems()).toEqual([
      'الرئيسية',
      'مساعد عزوة',
      'مستشار عزوة',
      'أسرتك',
      'مرونة أسرتك',
      'جميع الخدمات',
      'الإعدادات',
    ]);
    expect(screen.getByRole('button', { name: 'تسجيل الخروج' })).toBeOnTheScreen();
  });
});
