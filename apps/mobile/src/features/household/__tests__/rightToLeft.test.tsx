import { act, screen, userEvent, within } from '@testing-library/react-native';
import { I18nManager, StyleSheet } from 'react-native';

import { participantKeys } from '@/api/participant/queries';
import { leadHousehold } from '@/test-utils/householdWorlds';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { AddMemberScreen } from '../AddMemberScreen';
import { HouseholdAssignments } from '../HouseholdAssignments';
import { RosterScreen } from '../RosterScreen';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { resetRouterMock, setSearchParams } = jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

beforeEach(() => {
  resetRouterMock();
  // The layout direction a development build uses for Arabic.
  jest.replaceProperty(I18nManager, 'isRTL', true);
});
afterEach(async () => {
  await settleQueries();
  jest.restoreAllMocks();
});

const ARABIC_INDIC_DIGITS = /[٠-٩۰-۹]/;

const isMirrored = (icon: ReturnType<typeof screen.getByTestId>) =>
  JSON.stringify((StyleSheet.flatten(icon.props.style) as { transform?: object[] }).transform ?? []).includes(
    '"scaleX":-1',
  );

describe('household screens in Arabic, right to left', () => {
  it('shows the roster with Arabic names for every control, forward chevrons pointing left and Western digits', async () => {
    const world = await leadHousehold();
    setSearchParams({ householdId: world.householdId });

    await renderWithProviders(<RosterScreen />, { api: world.api, language: 'ar' });

    expect(await screen.findByTestId(`member-${world.spouse.id}`)).toBeOnTheScreen();
    expect(screen.getByRole('header', { name: 'الأفراد' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: /^تعديل الزوج أو الزوجة، مواليد/ })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: /^إزالة الزوج أو الزوجة، مواليد/ })).toBeOnTheScreen();
    expect(within(screen.getByTestId('roster-summary')).getByLabelText('ربّ الأسرة: أنت')).toBeOnTheScreen();

    const review = screen.getByRole('button', { name: 'المراجعة والتأكيد' });
    expect(isMirrored(within(review).getByTestId('icon-chevron-forward', { includeHiddenElements: true }))).toBe(true);
    expect(screen.getByLabelText(/^أنت، مواليد .* 1984 · العمر \d+،/)).toBeOnTheScreen();
    expect(JSON.stringify(screen.toJSON())).not.toMatch(ARABIC_INDIC_DIGITS);
  });

  it('validates the member form in Arabic', async () => {
    const world = await leadHousehold();
    setSearchParams({ householdId: world.householdId });
    const user = userEvent.setup();

    await renderWithProviders(<AddMemberScreen />, { api: world.api, language: 'ar' });
    await user.press(await screen.findByRole('button', { name: 'إضافة' }));

    const alerts = await screen.findAllByRole('alert');
    expect(alerts.map((alert) => alert.props.children)).toEqual([
      'أدخل سنة من 1900 حتى الآن.',
      'أدخل من 1 إلى 12.',
      'اختر صلة القرابة.',
    ]);
    expect(screen.getByRole('switch', { name: 'ربّ الأسرة' })).toBeOnTheScreen();
    expect(screen.getByRole('radio', { name: 'الأب أو الأم' })).toBeOnTheScreen();
  });

  it("shows the server's assignments in Arabic", async () => {
    const world = await leadHousehold();
    const confirmation = await world.api.confirmRoster(world.householdId);
    setSearchParams({ householdId: world.householdId });

    const { queryClient } = await renderWithProviders(<HouseholdAssignments />, { api: world.api, language: 'ar' });
    await act(async () => {
      queryClient.setQueryData(participantKeys.rosterConfirmation(world.householdId), confirmation);
    });

    expect(await screen.findByText('أُكّدت بيانات الأسرة')).toBeOnTheScreen();
    expect(await screen.findByRole('header', { name: 'أنت' })).toBeOnTheScreen();
    expect(screen.getByText('الاستبيانات: 2')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'العودة إلى الأسرة' })).toBeOnTheScreen();
  });
});
