import { fireEvent, screen, userEvent, waitFor, within } from '@testing-library/react-native';

import { leadHousehold } from '@/test-utils/householdWorlds';
import { queriesIdle, renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { AddMemberScreen } from '../AddMemberScreen';
import { EditMemberScreen } from '../EditMemberScreen';
import { RosterScreen } from '../RosterScreen';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { router, navigation, resetRouterMock, setSearchParams } =
  jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

beforeEach(() => resetRouterMock());
afterEach(() => settleQueries());

const SPOUSE = 'Spouse, born September 1987';

async function renderRoster(options: Parameters<typeof leadHousehold>[0] = {}) {
  const world = await leadHousehold(options);
  setSearchParams({ householdId: world.householdId });
  const user = userEvent.setup();
  const rendered = await renderWithProviders(<RosterScreen />, { api: world.api });
  await screen.findByTestId(`member-${world.self.id}`);
  return { ...world, ...rendered, user };
}

describe('the member roster', () => {
  it('shows each member as a card whose facts are read together, with roles in words', async () => {
    const { self, spouse, child } = await renderRoster();

    expect(screen.getByTestId(`member-${self.id}`)).toHaveTextContent(/You/);
    expect(screen.getByLabelText(/^You, Born March 1984 · Age \d+, Employed, Head, Main earner$/)).toBeOnTheScreen();
    expect(screen.getByLabelText(/^Spouse, Born September 1987 · Age \d+, Homemaker$/)).toBeOnTheScreen();
    expect(within(screen.getByTestId(`member-${child.id}`)).getByText('Son or daughter')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: `Edit ${SPOUSE}` })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: `Remove ${SPOUSE}` })).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: /^Remove You/ })).toBeNull();
    expect(screen.getByLabelText(`Edit ${SPOUSE}`)).toBeOnTheScreen();
    expect(spouse.id).not.toBe(self.id);
  });

  it('names the head of household and the main earner separately, also when they are different people', async () => {
    const world = await leadHousehold();
    await world.api.updateMember(world.householdId, world.self.id, { isPrimaryIncomeEarner: false });
    await world.api.updateMember(world.householdId, world.spouse.id, {
      employmentStatus: 'employed',
      isPrimaryIncomeEarner: true,
    });
    setSearchParams({ householdId: world.householdId });

    await renderWithProviders(<RosterScreen />, { api: world.api });

    const summary = await screen.findByTestId('roster-summary');
    expect(within(summary).getByLabelText('Head of household: You')).toBeOnTheScreen();
    expect(within(summary).getByLabelText(`Main earner: ${SPOUSE}`)).toBeOnTheScreen();
  });

  it('names every control for screen readers', async () => {
    await renderRoster();

    for (const role of ['button', 'switch', 'radio'] as const) {
      for (const control of screen.queryAllByRole(role)) {
        expect(control.props.accessibilityLabel).toEqual(expect.stringMatching(/\S/));
      }
    }
  });

  it('asks before removing a member, and removes nobody when cancelled', async () => {
    const { user, count } = await renderRoster();

    await user.press(screen.getByRole('button', { name: `Remove ${SPOUSE}` }));
    const dialog = await screen.findByTestId('confirmation-dialog');
    expect(within(dialog).getByRole('header', { name: 'Remove this person?' })).toBeOnTheScreen();
    expect(within(dialog).getByText(new RegExp(`^${SPOUSE} will be removed`))).toBeOnTheScreen();

    await user.press(within(dialog).getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByTestId('confirmation-dialog')).toBeNull();
    expect(count('removeMember')).toBe(0);
  });

  it('removes a member after confirmation and keeps them listed apart', async () => {
    const { user, spouse, argsOf, householdId } = await renderRoster();

    await user.press(screen.getByRole('button', { name: `Remove ${SPOUSE}` }));
    await user.press(within(await screen.findByTestId('confirmation-dialog')).getByRole('button', { name: 'Remove' }));

    expect(await screen.findByText(new RegExp(`^${SPOUSE} removed`))).toBeOnTheScreen();
    expect(argsOf('removeMember')).toEqual([[householdId, spouse.id]]);
    await waitFor(() => expect(screen.getByRole('header', { name: 'Left the household' })).toBeOnTheScreen());
    expect(screen.queryByRole('button', { name: `Remove ${SPOUSE}` })).toBeNull();
  });

  it('after a lost answer, retrying a removal completes it without an error', async () => {
    const { user, lose, count } = await renderRoster();
    lose('removeMember');

    await user.press(screen.getByRole('button', { name: `Remove ${SPOUSE}` }));
    await user.press(within(await screen.findByTestId('confirmation-dialog')).getByRole('button', { name: 'Remove' }));
    expect(await screen.findByText("Can't connect")).toBeOnTheScreen();
    await queriesIdle();

    await user.press(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText(new RegExp(`^${SPOUSE} removed`))).toBeOnTheScreen();
    expect(screen.queryByText("Can't connect")).toBeNull();
    expect(count('removeMember')).toBe(2);
  });

  it('marks on the card what a member still needs before the household can be confirmed', async () => {
    const world = await leadHousehold();
    await world.api.updateMember(world.householdId, world.spouse.id, { employmentStatus: null });
    setSearchParams({ householdId: world.householdId });

    await renderWithProviders(<RosterScreen />, { api: world.api });

    const card = await screen.findByTestId(`member-${world.spouse.id}`);
    expect(within(card).getByText('Work status needed')).toBeOnTheScreen();
  });

  it('welcomes a household that was just set up', async () => {
    const world = await leadHousehold();
    setSearchParams({ householdId: world.householdId, notice: 'created' });

    await renderWithProviders(<RosterScreen />, { api: world.api });

    expect(await screen.findByTestId(`member-${world.self.id}`)).toBeOnTheScreen();
    expect(screen.getByText('Household set up')).toBeOnTheScreen();
  });

  it('after the auto-filled family, says only that the household is set up (D-142)', async () => {
    const world = await leadHousehold();
    setSearchParams({ householdId: world.householdId, notice: 'demo_created' });

    await renderWithProviders(<RosterScreen />, { api: world.api });

    expect(await screen.findByTestId(`member-${world.self.id}`)).toBeOnTheScreen();
    expect(screen.getByText('Household set up')).toBeOnTheScreen();
    expect(screen.getByText('Check, then confirm.')).toBeOnTheScreen();
    expect(screen.queryByText(/demo|made-up/i)).toBeNull();
  });

  it('opens the forms to add and to edit members, and the review', async () => {
    const { user, householdId, spouse } = await renderRoster();

    await user.press(screen.getByRole('button', { name: 'Add a person' }));
    await user.press(screen.getByRole('button', { name: `Edit ${SPOUSE}` }));
    await user.press(screen.getByRole('button', { name: 'Check and confirm' }));

    expect(router.push.mock.calls).toEqual([
      [{ pathname: '/household/[householdId]/members/add', params: { householdId } }],
      [{ pathname: '/household/[householdId]/members/[memberId]', params: { householdId, memberId: spouse.id } }],
      [{ pathname: '/household/[householdId]/review', params: { householdId } }],
    ]);
  });
});

async function renderAdd() {
  const world = await leadHousehold();
  setSearchParams({ householdId: world.householdId });
  const user = userEvent.setup();
  const rendered = await renderWithProviders(<AddMemberScreen />, { api: world.api });
  await screen.findByLabelText('Year of birth');
  return { ...world, ...rendered, user };
}

describe('adding a member', () => {
  it('adds the person and returns to the roster', async () => {
    const { user, argsOf, householdId } = await renderAdd();

    await user.type(screen.getByLabelText('Year of birth'), '1955');
    await user.type(screen.getByLabelText('Month of birth'), '2');
    await user.press(screen.getByRole('radio', { name: 'Parent' }));
    await user.press(screen.getByRole('radio', { name: 'Retired' }));
    await user.press(screen.getByRole('button', { name: 'Add' }));

    await waitFor(() => expect(router.back).toHaveBeenCalled());
    expect(argsOf('addMember')).toEqual([
      [
        householdId,
        {
          birthYear: 1955,
          birthMonth: 2,
          relationshipToHousehold: 'parent',
          employmentStatus: 'retired',
          isHeadOfHousehold: false,
          isPrimaryIncomeEarner: false,
        },
      ],
    ]);
    // Saved, so leaving does not ask about unsaved changes.
    expect(screen.queryByTestId('confirmation-dialog')).toBeNull();
  });

  it('shows each problem under its field and sends nothing', async () => {
    const { user, count } = await renderAdd();

    await user.press(screen.getByRole('button', { name: 'Add' }));

    const alerts = await screen.findAllByRole('alert');
    expect(alerts.map((alert) => alert.props.children)).toEqual([
      'Enter a year from 1900 to now.',
      'Enter 1 to 12.',
      "Choose a relationship.",
    ]);
    expect(count('addMember')).toBe(0);
  });

  it('asks an adult for their work status', async () => {
    const { user, count } = await renderAdd();

    await user.type(screen.getByLabelText('Year of birth'), '1955');
    await user.type(screen.getByLabelText('Month of birth'), '2');
    await user.press(screen.getByRole('radio', { name: 'Parent' }));
    await user.press(screen.getByRole('button', { name: 'Add' }));

    expect(await screen.findByText("Choose a work status.")).toBeOnTheScreen();
    expect(count('addMember')).toBe(0);
  });

  it('offers the head and main-earner roles only to adults, and makes work status optional under 18', async () => {
    const { user } = await renderAdd();
    expect(screen.getByRole('switch', { name: 'Head of household' })).toBeOnTheScreen();

    await user.type(screen.getByLabelText('Year of birth'), '2016');
    await user.type(screen.getByLabelText('Month of birth'), '3');

    expect(screen.queryByRole('switch', { name: 'Head of household' })).toBeNull();
    expect(screen.queryByRole('switch', { name: 'Main earner' })).toBeNull();
    expect(screen.getByText("Under 18s can't be head or main earner.")).toBeOnTheScreen();
    expect(screen.getByLabelText('Work status (optional under 18)')).toHaveProp('accessibilityRole', 'radiogroup');
  });

  it('explains taking over the head-of-household role and moves it from the current head', async () => {
    const { user, api, householdId, self, argsOf } = await renderAdd();

    await user.press(screen.getByRole('switch', { name: 'Head of household' }));
    expect(screen.getByRole('switch', { name: 'Head of household' }).props.accessibilityHint).toMatch(
      /^Replaces You as head\. Then check relationships\./,
    );
    await user.type(screen.getByLabelText('Year of birth'), '1955');
    await user.type(screen.getByLabelText('Month of birth'), '2');
    await user.press(screen.getByRole('radio', { name: 'Retired' }));
    await user.press(screen.getByRole('button', { name: 'Add' }));

    await waitFor(() => expect(router.back).toHaveBeenCalled());
    expect(argsOf('updateMember')).toEqual([[householdId, self.id, { isHeadOfHousehold: false }]]);
    const heads = (await api.listMembers(householdId)).filter((member) => member.status === 'active' && member.isHeadOfHousehold);
    expect(heads).toHaveLength(1);
    expect(heads[0]?.birthYear).toBe(1955);
  });

  it('sends one request however many times Add is pressed', async () => {
    const { user, count } = await renderAdd();
    await user.type(screen.getByLabelText('Year of birth'), '2016');
    await user.type(screen.getByLabelText('Month of birth'), '3');
    await user.press(screen.getByRole('radio', { name: 'Grandchild' }));
    const add = screen.getByRole('button', { name: 'Add' });

    await fireEvent.press(add);
    await fireEvent.press(add);
    await fireEvent.press(add);

    await waitFor(() => expect(router.back).toHaveBeenCalledTimes(1));
    expect(count('addMember')).toBe(1);
  });

  it('shows a refusal from the server in words, with a retry', async () => {
    const { user, fail } = await renderAdd();
    const { ApiError } = jest.requireActual<typeof import('@/api/client')>('@/api/client');
    fail('addMember', new ApiError('conflict', 409, 'households.roster_full'));

    await user.type(screen.getByLabelText('Year of birth'), '2016');
    await user.type(screen.getByLabelText('Month of birth'), '3');
    await user.press(screen.getByRole('radio', { name: 'Grandchild' }));
    await user.press(screen.getByRole('button', { name: 'Add' }));

    expect(await screen.findByText('This household is full.')).toBeOnTheScreen();
    expect(router.back).not.toHaveBeenCalled();
  });
});

async function renderEdit(memberOf: (world: Awaited<ReturnType<typeof leadHousehold>>) => string) {
  const world = await leadHousehold();
  setSearchParams({ householdId: world.householdId, memberId: memberOf(world) });
  const user = userEvent.setup();
  const rendered = await renderWithProviders(<EditMemberScreen />, { api: world.api });
  await screen.findByLabelText('Year of birth');
  return { ...world, ...rendered, user };
}

describe('editing a member', () => {
  it('starts from the saved details and sends only what changed', async () => {
    const { user, argsOf, householdId, spouse } = await renderEdit((world) => world.spouse.id);
    expect(screen.getByLabelText('Year of birth').props.value).toBe('1987');
    expect(screen.getByRole('radio', { name: 'Homemaker' })).toBeChecked();

    await user.press(screen.getByRole('radio', { name: 'Employed' }));
    await user.press(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(router.back).toHaveBeenCalled());
    expect(argsOf('updateMember')).toEqual([[householdId, spouse.id, { employmentStatus: 'employed' }]]);
  });

  it('asks before leaving with unsaved changes', async () => {
    const { user } = await renderEdit((world) => world.spouse.id);

    await user.press(screen.getByRole('radio', { name: 'Employed' }));
    await user.press(screen.getByRole('button', { name: 'Back' }));
    await user.press(within(await screen.findByTestId('confirmation-dialog')).getByRole('button', { name: 'Discard' }));

    expect(navigation.dispatch).toHaveBeenCalledWith({ type: 'GO_BACK' });
  });

  it('removes the member after a confirmation and returns to the roster', async () => {
    const { user, argsOf, householdId, spouse } = await renderEdit((world) => world.spouse.id);

    await user.press(screen.getByRole('button', { name: 'Remove from household' }));
    await user.press(within(await screen.findByTestId('confirmation-dialog')).getByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(router.back).toHaveBeenCalled());
    expect(argsOf('removeMember')).toEqual([[householdId, spouse.id]]);
  });

  it('does not offer to remove the person who manages the household', async () => {
    await renderEdit((world) => world.self.id);

    expect(screen.getByRole('switch', { name: 'I\'m the head of household' })).toBeChecked();
    expect(screen.queryByRole('button', { name: 'Remove from household' })).toBeNull();
  });
});
