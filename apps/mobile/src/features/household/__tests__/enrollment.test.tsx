import { fireEvent, screen, userEvent, waitFor, within } from '@testing-library/react-native';

import { fixtureApi, NEW_PARTICIPANT_SUBJECT } from '@/test-utils/householdWorlds';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { AboutYouForm } from '../AboutYouForm';
import { HouseholdStart } from '../HouseholdStart';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
/** EXPO_PUBLIC_DEMONSTRATION_BUILD, as each test sets it (D-148); off unless a test turns it on. */
let mockDemonstrationBuild = false;
// A getter defined after the copy: the compiled object spread would read a getter written inside it once, too early.
jest.mock('@/config', () =>
  Object.defineProperty(
    { __esModule: true, ...jest.requireActual<typeof import('@/config')>('@/config') },
    'demonstrationBuild',
    { get: () => mockDemonstrationBuild, enumerable: true },
  ),
);
const { router, navigation, emitBeforeRemove, resetRouterMock } =
  jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

beforeEach(() => {
  resetRouterMock();
  mockDemonstrationBuild = false;
});
afterEach(() => settleQueries());

/** The new participant (no household yet), with a scriptable API. */
async function renderAboutYou() {
  const faults = fixtureApi(NEW_PARTICIPANT_SUBJECT);
  const user = userEvent.setup();
  const rendered = await renderWithProviders(<AboutYouForm />, { persona: 'new_participant', api: faults.api });
  await screen.findByLabelText('Year of birth');
  return { ...rendered, ...faults, user };
}

/** Fills in a valid adult head of household who is also the main earner. */
async function fillValidSelf(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Year of birth'), '1990');
  await user.type(screen.getByLabelText('Month of birth'), '6');
  await user.press(screen.getByRole('switch', { name: 'I\'m the head of household' }));
  await user.press(screen.getByRole('radio', { name: 'Employed' }));
  await user.press(screen.getByRole('switch', { name: 'I\'m the main earner' }));
}

describe('household creation', () => {
  it('says what is never asked and lists the steps', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<HouseholdStart />, { persona: 'new_participant' });

    expect(await screen.findByRole('header', { name: 'Who lives in your home?' })).toBeOnTheScreen();
    expect(screen.getByText("Never names or Emirates ID numbers.")).toBeOnTheScreen();
    expect(screen.getByText('3. Check and confirm')).toBeOnTheScreen();
    expect(screen.getByText('1. About you')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Start' }));

    expect(router.push).toHaveBeenCalledWith('/household/create/about-you');
  });

  it('sends an account that already has a household to it instead of starting another', async () => {
    const user = userEvent.setup();
    const { api } = await renderWithProviders(<HouseholdStart />);
    const open = await screen.findByRole('button', { name: 'Open household' });
    const me = await api.getMe();

    await user.press(open);

    expect(screen.getByText('You already have a household')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Start' })).toBeNull();
    expect(router.replace).toHaveBeenCalledWith({
      pathname: '/household/[householdId]',
      params: { householdId: me.household?.householdId },
    });
  });
});

describe('the demo family button (development builds only)', () => {
  /** A quiet text button with a neutral name and no explanation (D-142). */
  const DEMO = 'Auto-fill';

  /** The new participant (no household yet) on the start screen, with a scriptable API. */
  async function renderStart() {
    const faults = fixtureApi(NEW_PARTICIPANT_SUBJECT);
    const user = userEvent.setup();
    const rendered = await renderWithProviders(<HouseholdStart />, { persona: 'new_participant', api: faults.api });
    const button = await screen.findByRole('button', { name: DEMO });
    return { ...rendered, ...faults, user, button };
  }

  /** The household the account now has on the server, with everyone in it. */
  async function createdHousehold(api: Awaited<ReturnType<typeof renderStart>>['api']) {
    const me = await api.getMe();
    const householdId = me.household?.householdId ?? '';
    return { me, householdId, members: await api.listMembers(householdId) };
  }

  it('sits under Start as a quiet button, with no notice or explanation (D-142)', async () => {
    await renderStart();

    expect(screen.getByRole('button', { name: 'Start' })).toBeOnTheScreen();
    expect(screen.queryByText('Demo household')).toBeNull();
    expect(screen.queryByText(/demo|made-up|development/i)).toBeNull();
  });

  it('creates the household with the whole family in one request, then opens its roster', async () => {
    const { user, api, button, count } = await renderStart();

    await user.press(button);

    await waitFor(() => expect(router.replace).toHaveBeenCalled());
    expect(count('createHousehold')).toBe(1);
    const { me, householdId, members } = await createdHousehold(api);
    expect(me.household).toMatchObject({ role: 'manager', rosterConfirmedAt: null });
    expect(members.map((member) => member.relationshipToHousehold)).toEqual([
      null,
      'spouse',
      'child',
      'child',
      'child',
      'parent',
    ]);
    expect(members.filter((member) => member.isCaller)).toEqual([
      expect.objectContaining({ isHeadOfHousehold: true, isPrimaryIncomeEarner: true }),
    ]);
    expect(router.replace).toHaveBeenCalledWith({
      pathname: '/household/[householdId]/members',
      params: { householdId, notice: 'demo_created' },
    });
  });

  it('sends one request however many times it is pressed', async () => {
    const { button, count } = await renderStart();

    await fireEvent.press(button);
    await fireEvent.press(button);
    await fireEvent.press(button);

    await waitFor(() => expect(router.replace).toHaveBeenCalledTimes(1));
    expect(count('createHousehold')).toBe(1);
  });

  it('after a lost answer, retrying opens the household that was created instead of creating another', async () => {
    const { user, api, button, lose, count } = await renderStart();
    lose('createHousehold');

    await user.press(button);
    expect(await screen.findByText("Can't connect")).toBeOnTheScreen();
    expect(router.replace).not.toHaveBeenCalled();

    await user.press(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(router.replace).toHaveBeenCalled());
    expect(count('createHousehold')).toBe(1);
    const { householdId, members } = await createdHousehold(api);
    expect(members).toHaveLength(6);
    expect(router.replace).toHaveBeenCalledWith({
      pathname: '/household/[householdId]/members',
      params: { householdId, notice: 'demo_created' },
    });
  });

  it('is not offered to an account that already has a household', async () => {
    await renderWithProviders(<HouseholdStart />);

    expect(await screen.findByRole('button', { name: 'Open household' })).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: DEMO })).toBeNull();
    expect(screen.queryByText('Demo household')).toBeNull();
  });

  it('is not part of a release build', async () => {
    Reflect.set(globalThis, '__DEV__', false);
    try {
      await renderWithProviders(<HouseholdStart />, { persona: 'new_participant' });

      expect(await screen.findByRole('button', { name: 'Start' })).toBeOnTheScreen();
      expect(screen.queryByRole('button', { name: DEMO })).toBeNull();
      expect(screen.queryByText('Demo household')).toBeNull();
    } finally {
      Reflect.set(globalThis, '__DEV__', true);
    }
  });

  it('is offered by a release build made as a demonstration build, with no explanation (D-148)', async () => {
    mockDemonstrationBuild = true;
    Reflect.set(globalThis, '__DEV__', false);
    try {
      await renderWithProviders(<HouseholdStart />, { persona: 'new_participant' });

      expect(await screen.findByRole('button', { name: DEMO })).toBeOnTheScreen();
      expect(screen.queryByText(/demonstration|development|synthetic/i)).toBeNull();
    } finally {
      Reflect.set(globalThis, '__DEV__', true);
    }
  });
});

describe('the household details form (about you)', () => {
  it('shows each problem under its field and sends nothing', async () => {
    const { user, count } = await renderAboutYou();

    await user.press(screen.getByRole('button', { name: 'Continue' }));

    const alerts = await screen.findAllByRole('alert');
    expect(alerts.map((alert) => alert.props.children)).toEqual([
      'Enter a year from 1900 to now.',
      'Enter 1 to 12.',
      'Choose your relationship.',
    ]);
    expect(count('createHousehold')).toBe(0);
  });

  it('checks a field as soon as the person leaves it', async () => {
    const { user } = await renderAboutYou();

    await user.type(screen.getByLabelText('Year of birth'), '85');

    expect(await screen.findByText('Enter a year from 1900 to now.')).toBeOnTheScreen();
  });

  it('asks the relationship only of someone who is not the head of household', async () => {
    const { user } = await renderAboutYou();
    const question = 'Your relationship to the head';
    expect(screen.getByLabelText(question)).toHaveProp('accessibilityRole', 'radiogroup');

    await user.press(screen.getByRole('switch', { name: 'I\'m the head of household' }));

    expect(screen.queryByLabelText(question)).toBeNull();
  });

  it('does not offer the household roles to someone under 18, and says why they cannot set up a household', async () => {
    const { user } = await renderAboutYou();

    await user.type(screen.getByLabelText('Year of birth'), '2012');
    await user.type(screen.getByLabelText('Month of birth'), '3');

    expect(screen.queryByRole('switch', { name: 'I\'m the head of household' })).toBeNull();
    expect(screen.queryByRole('switch', { name: 'I\'m the main earner' })).toBeNull();
    expect(screen.getByText("Under 18s can't be head or main earner.")).toBeOnTheScreen();
    expect(await screen.findByText('You must be 18 or older.')).toBeOnTheScreen();
  });

  it('creates the household with the caller as its first member, then opens the roster', async () => {
    const { user, api, argsOf } = await renderAboutYou();
    await fillValidSelf(user);

    await user.press(screen.getByRole('button', { name: 'Continue' }));

    await waitFor(() => expect(router.replace).toHaveBeenCalled());
    expect(argsOf('createHousehold')).toEqual([
      [
        {
          members: [
            {
              birthYear: 1990,
              birthMonth: 6,
              relationshipToHousehold: null,
              employmentStatus: 'employed',
              isHeadOfHousehold: true,
              isPrimaryIncomeEarner: true,
              isSelf: true,
            },
          ],
        },
      ],
    ]);
    expect(router.replace).toHaveBeenCalledWith({
      pathname: '/household/[householdId]/members',
      params: { householdId: (await api.getMe()).household?.householdId, notice: 'created' },
    });
  });

  it('sends one request however many times Save is pressed', async () => {
    const { user, count } = await renderAboutYou();
    await fillValidSelf(user);
    const save = screen.getByRole('button', { name: 'Continue' });

    await fireEvent.press(save);
    await fireEvent.press(save);
    await fireEvent.press(save);

    await waitFor(() => expect(router.replace).toHaveBeenCalledTimes(1));
    expect(count('createHousehold')).toBe(1);
  });

  it('after a lost answer, retrying opens the household that was created instead of creating another', async () => {
    const { user, api, lose, count } = await renderAboutYou();
    await fillValidSelf(user);
    lose('createHousehold');

    await user.press(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByText("Can't connect")).toBeOnTheScreen();
    expect(router.replace).not.toHaveBeenCalled();

    await user.press(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(router.replace).toHaveBeenCalled());
    expect(count('createHousehold')).toBe(1);
    expect(router.replace).toHaveBeenCalledWith({
      pathname: '/household/[householdId]/members',
      params: { householdId: (await api.getMe()).household?.householdId, notice: 'created' },
    });
  });

  it('asks before leaving with unsaved changes', async () => {
    const { user } = await renderAboutYou();
    await user.type(screen.getByLabelText('Year of birth'), '1990');

    await user.press(screen.getByRole('button', { name: 'Back' }));
    const dialog = await screen.findByTestId('confirmation-dialog');
    expect(within(dialog).getByRole('header', { name: 'Discard changes?' })).toBeOnTheScreen();

    await user.press(within(dialog).getByRole('button', { name: 'Keep editing' }));
    expect(navigation.dispatch).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Year of birth').props.value).toBe('1990');

    await user.press(screen.getByRole('button', { name: 'Back' }));
    await user.press(within(await screen.findByTestId('confirmation-dialog')).getByRole('button', { name: 'Discard' }));
    expect(navigation.dispatch).toHaveBeenCalledWith({ type: 'GO_BACK' });
  });

  it('leaves without asking when nothing was changed', async () => {
    await renderAboutYou();

    expect(emitBeforeRemove()).toBe(false);
    expect(screen.queryByTestId('confirmation-dialog')).toBeNull();
  });
});
