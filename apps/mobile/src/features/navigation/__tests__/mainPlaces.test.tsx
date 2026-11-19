import { screen, userEvent, waitFor } from '@testing-library/react-native';

import Consultant from '../../../../app/(app)/(drawer)/consultant';
import Resilience from '../../../../app/(app)/(drawer)/resilience';
import Services from '../../../../app/(app)/(drawer)/services';
import Settings from '../../../../app/(app)/(drawer)/settings';
import { ApiError } from '@/api/client';
import { EMERGENCY_NUMBERS } from '@/features/emergency/emergencyNumbers';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { leadSession } from '@/test-utils/sessionWorlds';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
jest.mock('expo-web-browser', () => ({ openBrowserAsync: jest.fn(async () => ({ type: 'opened' })) }));
const { openBrowserAsync } = jest.requireMock<{ openBrowserAsync: jest.Mock }>('expo-web-browser');
const { navigation, router, resetRouterMock } =
  jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

beforeEach(() => resetRouterMock());
afterEach(settleQueries);

/** The places the side menu leads to sit side by side: their header opens the menu and has no back button. */
async function expectMenuHeader(title: string) {
  const user = userEvent.setup();
  expect(screen.getByRole('header', { name: title })).toBeOnTheScreen();
  expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();
  await user.press(screen.getByRole('button', { name: 'Open the menu' }));
  expect(navigation.dispatch).toHaveBeenCalledWith({ type: 'OPEN_DRAWER' });
}

describe('the Azwah consultant', () => {
  it('is a place of its own, ready to start a call, with no simulated notice (D-142)', async () => {
    await renderWithProviders(<Consultant />);

    await expectMenuHeader('Azwah consultant');
    expect(screen.getByText('Voice AI consultant')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Start talking' })).toBeEnabled();
    expect(screen.queryByText(/simulated|Development/)).toBeNull();
    expect(screen.getByText(/never scores or decides/)).toBeOnTheScreen();
  });

  it('reads in Arabic', async () => {
    await renderWithProviders(<Consultant />, { language: 'ar' });

    expect(screen.getByRole('header', { name: 'مستشار عزوة' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'ابدأ المحادثة' })).toBeOnTheScreen();
  });
});

describe('your family resilience', () => {
  it('gathers the family survey: what is still to answer, what was sent, and the next steps', async () => {
    const { api } = await leadSession();
    await renderWithProviders(<Resilience />, { api });

    await expectMenuHeader('Your family resilience');
    expect(await screen.findByRole('header', { name: 'To do' })).toBeOnTheScreen();
    expect(screen.getAllByTestId(/^assessment-/).map((card) => card.props.accessibilityLabel)).toEqual([
      '[DEV PLACEHOLDER] Sample questionnaire, [DEV PLACEHOLDER] Section A, In progress',
      '[DEV PLACEHOLDER] Sample questionnaire, [DEV PLACEHOLDER] Section B, Not started',
    ]);
    // Sent questionnaires appear once, by their results, rather than again as finished assignments.
    expect(screen.queryByRole('header', { name: 'Done or closed' })).toBeNull();
    expect(screen.getByRole('header', { name: 'Past questionnaires' })).toBeOnTheScreen();
    expect(
      screen.getByText("Only the head sees the score."),
    ).toBeOnTheScreen();
    expect(await screen.findByText('Result recorded')).toBeOnTheScreen();
    expect(await screen.findByRole('header', { name: 'Your next steps' })).toBeOnTheScreen();
  });

  it('opens a questionnaire to answer, and a past one at its result', async () => {
    const user = userEvent.setup();
    const { api, assignmentId } = await leadSession();
    const [sent] = await api.listHistory();
    await renderWithProviders(<Resilience />, { api });

    await user.press(await screen.findByRole('button', { name: /Section A, In progress/ }));
    await user.press(await screen.findByRole('button', { name: /Result recorded/ }));

    expect(router.push.mock.calls.map(([href]) => href)).toEqual([
      { pathname: '/assessments/[assignmentId]', params: { assignmentId } },
      { pathname: '/sessions/[sessionId]/result', params: { sessionId: sent?.sessionId } },
    ]);
  });

  it('reads in Arabic', async () => {
    const { api } = await leadSession();
    await renderWithProviders(<Resilience />, { api, language: 'ar' });

    expect(screen.getByRole('header', { name: 'مرونة أسرتك' })).toBeOnTheScreen();
    expect(await screen.findByRole('header', { name: 'الاستبيانات السابقة' })).toBeOnTheScreen();
  });
});

describe('all services', () => {
  it('lists every service with its search, as a place of its own', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Services />);

    await expectMenuHeader('All services');
    expect(screen.getAllByRole('button', { name: /^.+, [\d ]+$/ })).toHaveLength(EMERGENCY_NUMBERS.length);
    await user.type(screen.getByLabelText('Search services'), 'dubai');
    expect(screen.getByText('Found: 1')).toBeOnTheScreen();
  });

  it('lists the approved support directory under the numbers, and opens an entry in the browser', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Services />);

    expect(await screen.findByRole('header', { name: 'Support directory' })).toBeOnTheScreen();
    expect(await screen.findAllByTestId(/^directory-dev_/)).toHaveLength(3);
    await user.press(screen.getByRole('button', { name: "Visit Example parenting classes (sample entry)'s website" }));

    expect(openBrowserAsync).toHaveBeenCalledWith('https://example.org/azwah-dev/dev_parenting_classes');
  });

  it("searches the directory with the server's search once the person pauses typing", async () => {
    const user = userEvent.setup();
    const { api } = await renderWithProviders(<Services />);
    const search = jest.spyOn(api, 'searchSupportResources');
    await screen.findAllByTestId(/^directory-dev_/);

    await user.type(screen.getByLabelText('Search services'), 'debt');

    await waitFor(() => expect(screen.getAllByTestId(/^directory-dev_/)).toHaveLength(1));
    expect(screen.getByTestId('directory-dev_debt_advice')).toBeOnTheScreen();
    expect(search.mock.calls).toEqual([['debt']]);

    await user.clear(screen.getByLabelText('Search services'));
    await user.type(screen.getByLabelText('Search services'), 'zzz');
    expect(await screen.findByText('No match for "zzz".')).toBeOnTheScreen();
  });

  it('shows the crisis support card at the top of the screen while the search carries the notice', async () => {
    const user = userEvent.setup();
    const { api } = await renderWithProviders(<Services />);
    jest.spyOn(api, 'searchSupportResources').mockImplementation(async (text) => ({
      resources: [],
      safetyNotice: text === 'zzz' ? 'crisis_support' : null,
    }));
    await screen.findAllByTestId(/^directory-dev_/);
    expect(screen.queryByTestId('crisis-support-card')).toBeNull();

    await user.type(screen.getByLabelText('Search services'), 'zzz');
    expect(await screen.findByTestId('crisis-support-card')).toBeOnTheScreen();
    // At the top of the screen, above the emergency numbers and the search field.
    const page = JSON.stringify(screen.toJSON());
    expect(page.indexOf('crisis-support-card')).toBeLessThan(page.indexOf('emergency-danger'));

    await user.clear(screen.getByLabelText('Search services'));
    await user.type(screen.getByLabelText('Search services'), 'debt');
    await waitFor(() => expect(screen.queryByTestId('crisis-support-card')).toBeNull());
  });

  it('shows the card when a failed search carries the notice, and not when it carries none', async () => {
    const user = userEvent.setup();
    const { api } = await renderWithProviders(<Services />);
    jest.spyOn(api, 'searchSupportResources').mockImplementation(async (text) => {
      throw new ApiError('server', 500, undefined, text === 'zzz' ? 'crisis_support' : null);
    });
    await screen.findAllByTestId(/^directory-dev_/);

    await user.type(screen.getByLabelText('Search services'), 'zzz');
    expect(await screen.findByTestId('crisis-support-card')).toBeOnTheScreen();

    await user.clear(screen.getByLabelText('Search services'));
    await user.type(screen.getByLabelText('Search services'), 'debt');
    await waitFor(() => expect(screen.queryByTestId('crisis-support-card')).toBeNull());
  });

  it('shows the card again for the same crisis text typed again after a dismissal', async () => {
    const user = userEvent.setup();
    const { api } = await renderWithProviders(<Services />);
    jest.spyOn(api, 'searchSupportResources').mockImplementation(async (text) => ({
      resources: [],
      safetyNotice: text === 'zzz' ? 'crisis_support' : null,
    }));
    await screen.findAllByTestId(/^directory-dev_/);
    const field = screen.getByLabelText('Search services');

    await user.type(field, 'zzz');
    await user.press(await screen.findByRole('button', { name: 'Dismiss' }));
    expect(screen.queryByTestId('crisis-support-card')).toBeNull();

    await user.clear(field);
    await user.type(field, 'debt');
    await waitFor(() => expect(screen.queryByTestId('crisis-support-card')).toBeNull());
    await user.clear(field);
    await user.type(field, 'zzz');

    expect(await screen.findByTestId('crisis-support-card')).toBeOnTheScreen();
  });

  it('reads in Arabic', async () => {
    await renderWithProviders(<Services />, { language: 'ar' });

    expect(screen.getByRole('header', { name: 'جميع الخدمات' })).toBeOnTheScreen();
  });
});

describe('settings', () => {
  it('holds the account, language, data and signing out', async () => {
    await renderWithProviders(<Settings />);

    await expectMenuHeader('Settings');
    expect(screen.getByRole('header', { name: 'Language' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeOnTheScreen();
  });

  it('reads in Arabic', async () => {
    await renderWithProviders(<Settings />, { language: 'ar' });

    expect(screen.getByRole('header', { name: 'الإعدادات' })).toBeOnTheScreen();
  });
});
