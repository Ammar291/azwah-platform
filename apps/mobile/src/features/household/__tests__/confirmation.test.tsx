import { act, fireEvent, screen, userEvent, waitFor, within } from '@testing-library/react-native';

import { ApiError } from '@/api/client';
import type { ParticipantApi } from '@/api/participant/participantApi';
import { participantKeys } from '@/api/participant/queries';
import type { RosterConfirmation } from '@/api/participant/schemas';
import { leadHousehold } from '@/test-utils/householdWorlds';
import { queriesIdle, renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';

import { ConfirmRoster } from '../ConfirmRoster';
import { HouseholdAssignments } from '../HouseholdAssignments';
import { HouseholdProfile } from '../HouseholdProfile';
import { ReviewRoster } from '../ReviewRoster';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { router, resetRouterMock, setSearchParams } =
  jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

beforeEach(() => resetRouterMock());
afterEach(() => settleQueries());

const SPOUSE = 'Spouse, born September 1987';

type LeadWorld = Awaited<ReturnType<typeof leadHousehold>>;

describe('reviewing the roster', () => {
  it('lists what stops confirmation, with a way to fix each', async () => {
    const world = await leadHousehold();
    await world.api.updateMember(world.householdId, world.self.id, { isHeadOfHousehold: false });
    await world.api.updateMember(world.householdId, world.spouse.id, { employmentStatus: null });
    setSearchParams({ householdId: world.householdId });
    const user = userEvent.setup();

    await renderWithProviders(<ReviewRoster />, { api: world.api });

    const issues = await screen.findByTestId('roster-issues');
    expect(within(issues).getByText('Before you confirm')).toBeOnTheScreen();
    expect(within(issues).getByText('Choose the head of household.')).toBeOnTheScreen();
    expect(within(issues).getByText('You: add relationship.')).toBeOnTheScreen();
    expect(within(issues).getByText(`${SPOUSE}: add work status.`)).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Continue' })).toBeNull();

    await user.press(within(issues).getByRole('button', { name: `Fix: ${SPOUSE}: add work status.` }));
    await user.press(within(issues).getByRole('button', { name: 'Fix: Choose the head of household.' }));

    expect(router.push.mock.calls).toEqual([
      [
        {
          pathname: '/household/[householdId]/members/[memberId]',
          params: { householdId: world.householdId, memberId: world.spouse.id },
        },
      ],
      [{ pathname: '/household/[householdId]/members', params: { householdId: world.householdId } }],
    ]);
  });

  it('continues only once the person says everyone who lives with them is listed', async () => {
    const world = await leadHousehold({ householdUnconfirmed: true });
    setSearchParams({ householdId: world.householdId });
    const user = userEvent.setup();

    await renderWithProviders(<ReviewRoster />, { api: world.api });

    const next = await screen.findByRole('button', { name: 'Continue' });
    expect(next).toBeDisabled();
    expect(screen.queryByTestId('roster-issues')).toBeNull();

    await user.press(screen.getByRole('switch', { name: 'Everyone at home is listed' }));
    expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled();
    await user.press(screen.getByRole('button', { name: 'Continue' }));

    expect(router.push).toHaveBeenCalledWith({
      pathname: '/household/[householdId]/confirm',
      params: { householdId: world.householdId },
    });
  });

  it('shows a confirmed household as confirmed and lets it confirm again, for example for a new round', async () => {
    const world = await leadHousehold();
    setSearchParams({ householdId: world.householdId });
    const user = userEvent.setup();

    await renderWithProviders(<ReviewRoster />, { api: world.api });

    expect(await screen.findByText('Household confirmed')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Questionnaires' })).toBeOnTheScreen();
    expect(screen.getByText('Confirm again after any change.')).toBeOnTheScreen();

    await user.press(screen.getByRole('switch', { name: 'Everyone at home is listed' }));
    await user.press(screen.getByRole('button', { name: 'Continue' }));

    expect(router.push).toHaveBeenCalledWith({
      pathname: '/household/[householdId]/confirm',
      params: { householdId: world.householdId },
    });
  });
});

async function renderConfirm(prepare?: (world: LeadWorld) => Promise<unknown>) {
  const world = await leadHousehold({ householdUnconfirmed: true });
  await prepare?.(world);
  world.calls.length = 0;
  setSearchParams({ householdId: world.householdId });
  const user = userEvent.setup();
  const rendered = await renderWithProviders(<ConfirmRoster />, { api: world.api });
  await screen.findByTestId('roster-summary');
  return { ...world, ...rendered, user };
}

describe('confirming the roster', () => {
  it('confirms with the server, keeps its answer and opens the assignments over Home', async () => {
    const { user, count, queryClient, householdId } = await renderConfirm();
    expect(screen.getByText('Azwah assigns questionnaires from approved rules.')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Confirm household' }));

    await waitFor(() =>
      expect(router.push).toHaveBeenCalledWith({
        pathname: '/household/[householdId]/assignments',
        params: { householdId },
      }),
    );
    // The set-up and review screens leave the stack first, so Back from the assignments goes Home.
    expect(router.dismissTo).toHaveBeenCalledWith('/home');
    expect(router.dismissTo.mock.invocationCallOrder[0]).toBeLessThan(router.push.mock.invocationCallOrder[0] ?? 0);
    expect(router.replace).not.toHaveBeenCalled();
    expect(count('confirmRoster')).toBe(1);
    const kept = queryClient.getQueryData<RosterConfirmation>(participantKeys.rosterConfirmation(householdId));
    expect(kept?.assignments).toHaveLength(2);
  });

  it('sends one confirmation however many times the button is pressed', async () => {
    const { count } = await renderConfirm();
    const confirm = screen.getByRole('button', { name: 'Confirm household' });

    await fireEvent.press(confirm);
    await fireEvent.press(confirm);
    await fireEvent.press(confirm);

    await waitFor(() => expect(router.push).toHaveBeenCalledTimes(1));
    expect(router.dismissTo).toHaveBeenCalledTimes(1);
    expect(count('confirmRoster')).toBe(1);
  });

  it('explains missing information from the server and leads back to the review', async () => {
    const { user, fail, householdId } = await renderConfirm();
    fail('confirmRoster', new ApiError('conflict', 409, 'households.roster_missing_employment_status'));

    await user.press(screen.getByRole('button', { name: 'Confirm household' }));

    expect(await screen.findByText('Details missing')).toBeOnTheScreen();
    expect(screen.getByText('Add every adult\'s work status first.')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    await queriesIdle();
    await user.press(screen.getByRole('button', { name: 'Check household' }));
    expect(router.replace).toHaveBeenCalledWith({ pathname: '/household/[householdId]/review', params: { householdId } });
  });

  it('after a network failure, retries the same confirmation without assigning anything twice', async () => {
    const { user, lose, count, api, householdId } = await renderConfirm();
    const before = await api.listAssignments();
    lose('confirmRoster');

    await user.press(screen.getByRole('button', { name: 'Confirm household' }));
    expect(await screen.findByText("Can't connect")).toBeOnTheScreen();
    expect(router.push).not.toHaveBeenCalled();
    expect(router.dismissTo).not.toHaveBeenCalled();
    await queriesIdle();

    await user.press(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() =>
      expect(router.push).toHaveBeenCalledWith({
        pathname: '/household/[householdId]/assignments',
        params: { householdId },
      }),
    );
    expect(count('confirmRoster')).toBe(2);
    expect(await api.listAssignments()).toHaveLength(before.length);
  });

  it('does not offer to confirm while details are missing', async () => {
    const world = await leadHousehold();
    await world.api.updateMember(world.householdId, world.spouse.id, { employmentStatus: null });
    setSearchParams({ householdId: world.householdId });

    await renderWithProviders(<ConfirmRoster />, { api: world.api });

    expect(await screen.findByText('Details missing')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Confirm household' })).toBeNull();
  });
});

/** Renders the assignments screen as it is reached straight after confirming: the server's answer is in the cache. */
async function renderAssignments(
  options: {
    api?: (api: ParticipantApi, world: LeadWorld) => ParticipantApi;
    confirmation?: (confirmation: RosterConfirmation, world: LeadWorld) => RosterConfirmation;
  } = {},
) {
  const world = await leadHousehold();
  const answer = await world.api.confirmRoster(world.householdId);
  const confirmation = options.confirmation ? options.confirmation(answer, world) : answer;
  world.calls.length = 0;
  setSearchParams({ householdId: world.householdId });
  const user = userEvent.setup();
  const rendered = await renderWithProviders(<HouseholdAssignments />, {
    api: options.api ? options.api(world.api, world) : world.api,
  });
  await act(async () => {
    rendered.queryClient.setQueryData(participantKeys.rosterConfirmation(world.householdId), confirmation);
  });
  await screen.findByText('Household confirmed');
  return { ...world, ...rendered, user, confirmation };
}

describe('the assignments the server returned', () => {
  it('groups several questionnaires under the one member they were assigned to', async () => {
    const { confirmation } = await renderAssignments();

    const you = await screen.findByRole('header', { name: 'You' });
    expect(you).toBeOnTheScreen();
    expect(screen.getByText('Questionnaires: 2')).toBeOnTheScreen();
    for (const assignment of confirmation.assignments) {
      expect(await screen.findByTestId(`assessment-${assignment.assignmentId}`)).toBeOnTheScreen();
    }
  });

  it('groups questionnaires by member when several members are assigned', async () => {
    const extra = '0199e0a1-0000-7000-9000-00000000f001';
    await renderAssignments({
      api: (api, { spouse }) => ({
        ...api,
        listAssignments: async () => {
          const list = await api.listAssignments();
          const template = list.find((assignment) => assignment.status === 'assigned') ?? list[0];
          return template ? [...list, { ...template, id: extra, memberId: spouse.id }] : list;
        },
      }),
      confirmation: (value, { spouse }) => ({
        ...value,
        assignments: [
          ...value.assignments,
          { assignmentId: extra, memberId: spouse.id, instrumentCode: 'X', sectionCode: 'B', status: 'assigned' },
        ],
      }),
    });

    expect(await screen.findByRole('header', { name: 'Spouse' })).toBeOnTheScreen();
    expect(screen.getByText('Questionnaires: 1')).toBeOnTheScreen();
    expect(screen.getByText('Questionnaires: 2')).toBeOnTheScreen();
    expect(await screen.findByTestId(`assessment-${extra}`)).toBeOnTheScreen();
  });

  it('says so when nobody in the household is assigned a questionnaire this round', async () => {
    await renderAssignments({ confirmation: (value) => ({ ...value, waveCode: 'dev-wave-2', assignments: [] }) });

    expect(screen.getByRole('header', { name: 'Nothing this round' })).toBeOnTheScreen();
    expect(screen.getByText('Nobody answers in round 2.')).toBeOnTheScreen();
  });

  it('says so when no round is open', async () => {
    await renderAssignments({
      confirmation: (value) => ({ ...value, waveId: null, waveCode: null, assignments: [] }),
    });

    expect(screen.getByRole('header', { name: 'No round open' })).toBeOnTheScreen();
  });

  it('keeps showing a successful confirmation when refreshing the details afterwards fails', async () => {
    const { user, confirmation } = await renderAssignments({
      api: (api) => {
        let failures = 1;
        return {
          ...api,
          listAssignments: async () => {
            if (failures-- > 0) throw new ApiError('network');
            return api.listAssignments();
          },
        };
      },
    });

    expect(screen.getByText("Latest details didn't load.")).toBeOnTheScreen();
    expect(screen.getAllByText("Details didn't load.")).toHaveLength(2);
    expect(screen.getByText('Questionnaires: 2')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Try again' }));

    for (const assignment of confirmation.assignments) {
      expect(await screen.findByTestId(`assessment-${assignment.assignmentId}`)).toBeOnTheScreen();
    }
    expect(screen.queryByText("Can't connect")).toBeNull();
  });

  it("shows only the status of a questionnaire answered in another member's own account", async () => {
    const extra = '0199e0a1-0000-7000-9000-00000000f002';
    await renderAssignments({
      api: (api, { spouse }) => ({
        ...api,
        listMembers: async (householdId) =>
          (await api.listMembers(householdId)).map((member) =>
            member.id === spouse.id ? { ...member, hasAccount: true } : member,
          ),
      }),
      confirmation: (value, { spouse }) => ({
        ...value,
        assignments: [
          ...value.assignments,
          { assignmentId: extra, memberId: spouse.id, instrumentCode: 'X', sectionCode: null, status: 'in_progress' },
        ],
      }),
    });

    const card = await screen.findByTestId(`assignment-${extra}`);
    expect(within(card).getByText('Answers in their own account.')).toBeOnTheScreen();
    expect(within(card).getByText('In progress')).toBeOnTheScreen();
    expect(screen.queryByTestId(`assessment-${extra}`)).toBeNull();
  });

  it('offers no way to change what the server assigned, and reads no answers', async () => {
    const { count } = await renderAssignments();
    await screen.findByRole('header', { name: 'You' });

    const names = screen.getAllByRole('button').map((button) => String(button.props.accessibilityLabel));
    expect(names.filter((name) => /remove|add|edit|change|cancel|decline/i.test(name))).toEqual([]);
    expect(screen.queryAllByRole('switch')).toEqual([]);
    expect(screen.queryAllByRole('radio')).toEqual([]);
    expect(screen.getByText('Azwah assigns questionnaires from approved rules.')).toBeOnTheScreen();
    expect(count('getAnswers') + count('getResult') + count('getSession')).toBe(0);
  });

  it('goes back to the household over Home, leaving the set-up screens behind', async () => {
    const { user, householdId } = await renderAssignments();

    await user.press(screen.getByRole('button', { name: 'Back to household' }));

    // Dismissed down to Home first: Back from the household never walks through review, members or set-up.
    expect(router.dismissTo).toHaveBeenCalledWith('/home');
    expect(router.push).toHaveBeenCalledWith({ pathname: '/household/[householdId]', params: { householdId } });
    expect(router.dismissTo.mock.invocationCallOrder[0]).toBeLessThan(router.push.mock.invocationCallOrder[0] ?? 0);
    expect(router.replace).not.toHaveBeenCalled();
  });

  it('asks to confirm the household first when it is not confirmed', async () => {
    const world = await leadHousehold({ householdUnconfirmed: true });
    setSearchParams({ householdId: world.householdId });
    const user = userEvent.setup();

    await renderWithProviders(<HouseholdAssignments />, { api: world.api });

    expect(await screen.findByText("Not confirmed yet")).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Check household' }));
    expect(router.push).toHaveBeenCalledWith({
      pathname: '/household/[householdId]/review',
      params: { householdId: world.householdId },
    });
  });
});

describe('the household profile', () => {
  it("shows the household's status, who holds each role and the next step", async () => {
    const world = await leadHousehold({ householdUnconfirmed: true });
    setSearchParams({ householdId: world.householdId });
    const user = userEvent.setup();

    await renderWithProviders(<HouseholdProfile />, { api: world.api });

    expect(await screen.findByRole('header', { name: 'Ref. DEV-4821' })).toBeOnTheScreen();
    expect(screen.getByText('Not confirmed')).toBeOnTheScreen();
    expect(screen.getByLabelText('Head of household: You')).toBeOnTheScreen();
    expect(screen.getByLabelText('Main earner: You')).toBeOnTheScreen();
    expect(screen.getByLabelText('People: 3')).toBeOnTheScreen();

    expect(screen.queryByRole('button', { name: 'Confirm again' })).toBeNull();
    await user.press(screen.getByRole('button', { name: 'Check household' }));
    await user.press(screen.getByRole('button', { name: 'People' }));
    await user.press(screen.getByRole('button', { name: 'Questionnaires' }));

    const params = { householdId: world.householdId };
    expect(router.push.mock.calls).toEqual([
      [{ pathname: '/household/[householdId]/review', params }],
      [{ pathname: '/household/[householdId]/members', params }],
      [{ pathname: '/household/[householdId]/assignments', params }],
    ]);
  });
});
