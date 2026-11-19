import { act, screen, userEvent, within } from '@testing-library/react-native';

import Home from '../../../../app/(app)/(drawer)/home';
import { ApiError } from '@/api/client';
import { itemsOf } from '@/dev-fixtures/content';
import { createMemoryScoreReportStore } from '@/features/score-report/reportStore';
import { leadHousehold } from '@/test-utils/householdWorlds';
import { PLACEHOLDER_VERSION } from '@/test-utils/placeholderContent';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { leadSession } from '@/test-utils/sessionWorlds';

import { HomeDashboard } from '../HomeDashboard';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { navigation, router, resetRouterMock } =
  jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

beforeEach(() => resetRouterMock());
afterEach(settleQueries);

const step = (id: string) => within(screen.getByTestId(`path-step-${id}`));

describe('HomeDashboard', () => {
  it("shows the household lead's path: household and consent done, the family survey to continue", async () => {
    const user = userEvent.setup();
    const { api, assignmentId } = await leadSession();
    await renderWithProviders(<HomeDashboard />, { api });

    expect(await screen.findByRole('header', { name: "Your family's path" })).toBeOnTheScreen();
    expect(step('household').getByLabelText(/^Step 1 of 5\. Set up your household\. Done$/)).toBeOnTheScreen();
    expect(step('consent').getByLabelText(/^Step 2 of 5\. Give your consent\. Done$/)).toBeOnTheScreen();
    expect(step('checkIn').getByLabelText(/^Step 3 of 5\. Answer the family survey\. Now\./)).toBeOnTheScreen();
    expect(step('checkIn').getByText('To answer: 2')).toBeOnTheScreen();
    expect(step('nextSteps').getByLabelText(/^Step 4 of 5\. See your next steps\. Later$/)).toBeOnTheScreen();
    expect(step('nextCheckIn').getByLabelText(/^Step 5 of 5\. Next survey in about six months\. Later$/)).toBeOnTheScreen();

    await user.press(step('checkIn').getByRole('button', { name: 'Continue survey' }));
    expect(router.push).toHaveBeenLastCalledWith({
      pathname: '/assessments/[assignmentId]',
      params: { assignmentId },
    });

    await user.press(step('checkIn').getByRole('button', { name: 'All questionnaires' }));
    expect(router.push).toHaveBeenLastCalledWith('/assessments');
  });

  it('starts a new participant at setting up the household', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<HomeDashboard />, { persona: 'new_participant' });

    await user.press(await screen.findByRole('button', { name: 'Set up household' }));

    expect(router.push).toHaveBeenCalledWith('/household/create');
    expect(step('consent').getByLabelText(/Give your consent\. Later$/)).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Your household' })).toBeNull();
  });

  it('says where the next steps will appear before the server has routed any support', async () => {
    await renderWithProviders(<HomeDashboard />);

    expect(await screen.findByText('They appear after the survey.')).toBeOnTheScreen();
    // No support rule set is approved, and the screen does not say one is.
    expect(screen.queryByText(/support rules/)).toBeNull();
    expect(screen.queryByText(/approved support rules/)).toBeNull();
  });

  it('says plainly that no support was routed once the family survey is finished', async () => {
    const { api } = await leadSession();
    const finished = (await api.listAssignments()).map((assignment) => ({
      ...assignment,
      status: 'completed' as const,
      completedAt: '2026-10-15T09:00:00Z',
      activeSessionId: null,
    }));
    jest.spyOn(api, 'listAssignments').mockResolvedValue(finished);
    await renderWithProviders(<HomeDashboard />, { api });

    expect(
      await screen.findByText('No support routed yet.'),
    ).toBeOnTheScreen();
    expect(screen.queryByText('They appear after the survey.')).toBeNull();
    // Done, and no longer saying the next steps depend on it (D-146).
    expect(step('checkIn').getByLabelText(/Answer the family survey\. Done$/)).toBeOnTheScreen();
    // The questionnaires stay one press away, although the step has nothing left to do.
    expect(step('checkIn').getByRole('button', { name: 'All questionnaires' })).toBeOnTheScreen();
  });

  it('keeps the way to the questionnaires after a roster edit sends the path back to the household step', async () => {
    const user = userEvent.setup();
    const { api } = await leadHousehold({ householdUnconfirmed: true });
    await renderWithProviders(<HomeDashboard />, { api });

    expect(await screen.findByRole('button', { name: 'Check household' })).toBeOnTheScreen();
    expect(step('checkIn').queryByRole('button', { name: /the family survey$/ })).toBeNull();
    await user.press(step('checkIn').getByRole('button', { name: 'All questionnaires' }));

    expect(router.push).toHaveBeenLastCalledWith('/assessments');
  });

  it('offers no way to questionnaires before the family has any', async () => {
    await renderWithProviders(<HomeDashboard />, { persona: 'new_participant' });

    await screen.findByRole('button', { name: 'Set up household' });
    expect(screen.queryByRole('button', { name: 'All questionnaires' })).toBeNull();
  });

  it('shows the support the server routed, newest first, as recorded, with its referral status', async () => {
    const { api } = await leadSession();
    const [member] = await api.listMyConsents();
    const memberId = member?.memberId ?? '';
    jest.spyOn(api, 'listRecommendations').mockResolvedValue([
      {
        id: '0199e0a1-0000-7000-9000-0000000000f1',
        memberId,
        sessionId: '0199e0a1-0000-7000-9000-0000000000f8',
        categoryCode: 'dev_older_category',
        createdAt: '2026-10-01T09:00:00Z',
        referralId: null,
        referralStatus: null,
      },
      {
        id: '0199e0a1-0000-7000-9000-0000000000f2',
        memberId,
        sessionId: '0199e0a1-0000-7000-9000-0000000000f9',
        categoryCode: 'dev_newer_category',
        createdAt: '2026-10-15T09:00:00Z',
        referralId: '0199e0a1-0000-7000-9000-0000000000f3',
        referralStatus: 'in_progress',
      },
    ]);
    await renderWithProviders(<HomeDashboard />, { api });

    const newer = await screen.findByTestId('next-step-0199e0a1-0000-7000-9000-0000000000f2');
    expect(within(newer).getByText('dev_newer_category')).toBeOnTheScreen();
    expect(within(newer).getByText('Referral in progress')).toBeOnTheScreen();
    const codes = screen.getAllByText(/^dev_(newer|older)_category$/).map((node) => node.props.children);
    expect(codes).toEqual(['dev_newer_category', 'dev_older_category']);
    expect(screen.queryByTestId('next-steps-empty')).toBeNull();
    expect(step('nextSteps').getByLabelText(/See your next steps\. Done$/)).toBeOnTheScreen();
  });

  describe('after the family survey (D-146)', () => {
    const SESSION = '0199e0a1-0000-7000-9000-0000000000e1';
    const SCALE = { minimum: 4, maximum: 16, bands: [{ code: 'dev_placeholder_tier', min: 4, max: 16 }] };

    /** The lead with every questionnaire finished and the latest result on the trend, as the server sends them. */
    async function finished() {
      const { api } = await leadSession();
      const done = (await api.listAssignments()).map((assignment) => ({
        ...assignment,
        status: 'completed' as const,
        completedAt: '2026-11-11T09:00:00Z',
        activeSessionId: null,
      }));
      jest.spyOn(api, 'listAssignments').mockResolvedValue(done);
      jest.spyOn(api, 'getResilienceTrend').mockResolvedValue({
        scoreVisibility: 'visible',
        scale: SCALE,
        points: [{ sessionId: SESSION, waveCode: 'dev-wave-2', submittedAt: '2026-11-11T09:00:00Z', totalScore: 8, tierCode: 'dev_placeholder_tier', segment: 0 }],
        notShownCount: 0,
      });
      return api;
    }

    it("lists the report's next steps once it is written, and opens it", async () => {
      const user = userEvent.setup();
      const api = await finished();
      const reports = createMemoryScoreReportStore();
      await reports.save({
        sessionId: SESSION,
        language: 'en',
        totalScore: 8,
        tierCode: 'dev_placeholder_tier',
        scale: SCALE,
        summary: 'A summary.',
        strengths: [],
        weaknesses: [],
        nextSteps: [
          { sectionCode: 'A', area: 'Time together', reason: 'Shared time builds trust.', steps: ['Eat together.'], standing: 0.1, authorities: [] },
        ],
        modelName: 'synthetic-model',
        generatedAt: '2026-11-11T09:01:00Z',
      });
      await renderWithProviders(<HomeDashboard />, { api, reports });

      const listed = await screen.findByTestId('next-steps-report');
      // The report's words, with its mark.
      expect(within(listed).getByText('Written by AI')).toBeOnTheScreen();
      expect(within(listed).getByText('Time together')).toBeOnTheScreen();
      expect(within(listed).getByText('Shared time builds trust.')).toBeOnTheScreen();
      expect(step('checkIn').getByLabelText(/Answer the family survey\. Done$/)).toBeOnTheScreen();
      expect(step('nextSteps').getByLabelText(/See your next steps\. Done$/)).toBeOnTheScreen();
      expect(step('nextCheckIn').getByLabelText(/Next survey in about six months\. Now$/)).toBeOnTheScreen();
      expect(screen.queryByText('They appear after the survey.')).toBeNull();

      await user.press(within(listed).getByRole('button', { name: 'Open report' }));
      expect(router.push).toHaveBeenLastCalledWith({ pathname: '/sessions/[sessionId]/result', params: { sessionId: SESSION } });
    });

    it('points to the result for the next steps before a report is written', async () => {
      const user = userEvent.setup();
      const api = await finished();
      await renderWithProviders(<HomeDashboard />, { api });

      expect(await screen.findByText("They're with your result.")).toBeOnTheScreen();
      await user.press(step('nextSteps').getByRole('button', { name: 'See next steps' }));
      expect(router.push).toHaveBeenLastCalledWith({ pathname: '/sessions/[sessionId]/result', params: { sessionId: SESSION } });
      await user.press(screen.getByRole('button', { name: 'See result' }));
      expect(router.push).toHaveBeenLastCalledWith({ pathname: '/sessions/[sessionId]/result', params: { sessionId: SESSION } });
    });

    it('shows the survey done as soon as the server confirms it, with no refresh', async () => {
      const { api, sessionId } = await leadSession();
      const { engine } = await renderWithProviders(<HomeDashboard />, { api });
      expect(await screen.findByRole('button', { name: 'Continue survey' })).toBeOnTheScreen();

      // Every questionnaire finished on the server, as one tap of a demo button does (D-143)...
      for (const assignment of await api.listAssignments()) {
        if (assignment.status !== 'assigned' && assignment.status !== 'in_progress') continue;
        const id = assignment.activeSessionId ?? (await api.startSession(assignment.id)).id;
        const items = itemsOf(PLACEHOLDER_VERSION, assignment.sectionCode);
        await api.saveAnswers(id, items.map((item) => ({ itemId: item.id, optionId: item.options[0]?.id ?? '' })));
        await api.submitSession(id, `key-${id}`);
      }
      // ...and the phone told so, as the buttons tell it.
      await act(() => engine.submittedOnServer(sessionId));

      expect(await step('checkIn').findByLabelText(/Answer the family survey\. Done$/)).toBeOnTheScreen();
      expect(step('checkIn').queryByRole('button', { name: /survey$/ })).toBeNull();
    });
  });

  it('offers a retry when the path cannot load', async () => {
    const { api, fail } = await leadSession();
    fail('listAssignments', new ApiError('network'));
    await renderWithProviders(<HomeDashboard />, { api });

    expect(await screen.findByRole('button', { name: 'Try again' })).toBeOnTheScreen();
    expect(screen.queryByTestId('family-path')).toBeNull();
  });

  it('links to the household, past questionnaires and help', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<HomeDashboard />);

    await user.press(await screen.findByRole('button', { name: 'Your household' }));
    await user.press(screen.getByRole('button', { name: 'Past questionnaires' }));
    await user.press(screen.getByRole('button', { name: 'Help' }));

    expect(router.push.mock.calls.map(([href]) => href)).toEqual([
      { pathname: '/household/[householdId]', params: { householdId: expect.any(String) } },
      '/history',
      '/help',
    ]);
  });

  it('reads in Arabic', async () => {
    await renderWithProviders(<HomeDashboard />, { language: 'ar' });

    expect(await screen.findByRole('header', { name: 'مسار أسرتك' })).toBeOnTheScreen();
    expect(step('checkIn').getByRole('button', { name: 'تابع الاستبيان' })).toBeOnTheScreen();
    expect(screen.getByText('تظهر بعد الاستبيان.')).toBeOnTheScreen();
  });
});

describe('Home', () => {
  it('opens the side menu from the header, where Profile used to be', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Home />);

    await user.press(await screen.findByRole('button', { name: 'Open the menu' }));

    expect(navigation.dispatch).toHaveBeenCalledWith({ type: 'OPEN_DRAWER' });
    expect(screen.queryByRole('button', { name: 'Profile' })).toBeNull();
  });

  it('floats SOS and the assistant over the screen', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Home />);

    await user.press(await screen.findByRole('button', { name: 'SOS, emergency numbers' }));
    await user.press(screen.getByRole('button', { name: 'Ask Azwah' }));

    expect(router.push.mock.calls.map(([href]) => href)).toEqual(['/emergency', '/assistant']);
  });

  it('names both buttons in Arabic and keeps SOS as written', async () => {
    await renderWithProviders(<Home />, { language: 'ar' });

    const sos = await screen.findByRole('button', { name: 'SOS، أرقام الطوارئ' });
    expect(within(sos).getByText('SOS')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'اسأل عزوة' })).toBeOnTheScreen();
  });
});
