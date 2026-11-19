import { act, fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';

import { ApiError } from '@/api/client';
import { PLACEHOLDER_VERSION } from '@/test-utils/placeholderContent';
import { createDeviceDrafts } from '@/test-utils/deviceDrafts';
import { createFakeConnectivity } from '@/test-utils/fakeConnectivity';
import { fixtureApi, LEAD_SUBJECT } from '@/test-utils/householdWorlds';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { answerEverything, leadSession } from '@/test-utils/sessionWorlds';

import { AssessmentDetail } from '../AssessmentDetail';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { router, resetRouterMock } = jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

beforeEach(() => resetRouterMock());
afterEach(async () => {
  await settleQueries();
  jest.restoreAllMocks();
});

/** The household lead's questionnaire that has not been started (the lead has given participation consent). */
async function notStarted() {
  const faults = fixtureApi(LEAD_SUBJECT);
  const assignment = (await faults.api.listAssignments()).find((candidate) => candidate.status === 'assigned');
  if (!assignment) throw new Error('The household lead fixture changed.');
  faults.calls.length = 0;
  return { ...faults, assignment };
}

describe('starting a questionnaire', () => {
  it('after a lost answer, opens the session the lost request created instead of refusing the retry', async () => {
    const user = userEvent.setup();
    const { api, lose, count, assignment } = await notStarted();
    lose('startSession');
    await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api });
    await screen.findByLabelText('Questions: 3');

    await user.press(screen.getByRole('button', { name: 'Start' }));
    expect(await screen.findByText("Can't connect")).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(router.push).toHaveBeenCalledTimes(1));
    const sessionId = (await api.getAssignment(assignment.id)).activeSessionId;
    expect(router.push).toHaveBeenCalledWith({ pathname: '/sessions/[sessionId]', params: { sessionId } });
    expect(count('startSession')).toBe(2);
  });

  it('starts one session however many times Start is pressed', async () => {
    const { api, count, assignment } = await notStarted();
    await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api });
    await screen.findByLabelText('Questions: 3');
    const start = screen.getByRole('button', { name: 'Start' });

    await fireEvent.press(start);
    await fireEvent.press(start);
    await fireEvent.press(start);

    await waitFor(() => expect(router.push).toHaveBeenCalledTimes(1));
    expect(count('startSession')).toBe(1);
  });
});

describe('the introduction', () => {
  it("shows the name, an approved version's own purpose, the number of questions and the round, and no duration", async () => {
    const { api, assignment } = await notStarted();
    jest.spyOn(api, 'getPublishedVersion').mockResolvedValue({ ...PLACEHOLDER_VERSION, status: 'approved' });
    await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api });

    expect(screen.getByRole('header', { name: '[DEV PLACEHOLDER] Sample questionnaire' })).toBeOnTheScreen();
    expect(await screen.findByText('[DEV PLACEHOLDER] Synthetic content for building the app.')).toBeOnTheScreen();
    expect(screen.getByText('About')).toBeOnTheScreen();
    expect(screen.getByLabelText('Questions: 3')).toBeOnTheScreen();
    expect(screen.getByLabelText('Optional: 1')).toBeOnTheScreen();
    expect(screen.getByLabelText('Round: 2')).toBeOnTheScreen();
    expect(JSON.stringify(screen.toJSON())).not.toMatch(/minute|duration/i);
    expect(screen.getByRole('button', { name: 'Start' })).toBeEnabled();
  });

  it("leaves out a draft version's description: nobody wrote it for participants", async () => {
    const { api, assignment } = await notStarted();
    await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api });

    expect(await screen.findByLabelText('Questions: 3')).toBeOnTheScreen();
    expect(screen.queryByText('About')).toBeNull();
    expect(screen.queryByText('[DEV PLACEHOLDER] Synthetic content for building the app.')).toBeNull();
    expect(screen.getByRole('button', { name: 'Start' })).toBeEnabled();
  });

  it('shows the round as recorded when its code carries no number', async () => {
    const { api, assignment } = await notStarted();
    await renderWithProviders(<AssessmentDetail assignment={{ ...assignment, waveCode: '2026_autumn' }} />, { api });

    expect(screen.getByLabelText('Round: 2026_autumn')).toBeOnTheScreen();
    await screen.findByLabelText('Questions: 3');
  });

  it('shows a questionnaire with parts lacking Arabic without any "awaiting approval" note (D-142)', async () => {
    const { api, assignment } = await notStarted();
    const complete = await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api, language: 'ar' });
    await screen.findByLabelText('عدد الأسئلة: 3');
    await settleQueries();
    await complete.unmount();

    // As the family survey's codebook: an answer option with no Arabic, shown in English.
    jest.spyOn(api, 'getPublishedVersion').mockResolvedValue({
      ...PLACEHOLDER_VERSION,
      sections: PLACEHOLDER_VERSION.sections.map((section) => ({
        ...section,
        items: section.items.map((item) => ({
          ...item,
          options: item.options.map((option, index) =>
            index === 0 ? { ...option, label: { ...option.label, ar: null } } : option,
          ),
        })),
      })),
    });
    const arabic = await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api, language: 'ar' });
    await screen.findByLabelText('عدد الأسئلة: 3');
    expect(screen.queryByText(/بانتظار الاعتماد|بالإنجليزية/)).toBeNull();
    await settleQueries();
    await arabic.unmount();

    await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api });
    await screen.findByLabelText('Questions: 3');
    expect(screen.queryByText(/approval|English/)).toBeNull();
  });

  it('says how answers are handled, and that they are encrypted only when they are kept on the phone', async () => {
    const { api, assignment } = await notStarted();
    const phone = await createDeviceDrafts();
    const onPhone = await renderWithProviders(<AssessmentDetail assignment={assignment} />, {
      api,
      drafts: phone.drafts,
      mode: 'api',
    });
    expect(screen.getByTestId('privacy-notice')).toHaveTextContent(/Staff never see them one by one/);
    expect(screen.getByTestId('privacy-notice')).toHaveTextContent(/Kept encrypted on this phone/);
    expect(screen.getByTestId('privacy-notice')).toHaveTextContent(/until sent or you sign out/);
    await onPhone.unmount();
    phone.close();

    await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api, mode: 'api' });
    expect(screen.getByTestId('privacy-notice')).toHaveTextContent(
      /Nothing is saved on this phone/,
    );
    expect(screen.getByTestId('privacy-notice')).not.toHaveTextContent(/encrypted/);
  });

  it('in development fixtures says how answers are handled, with no development note and no untrue word about the phone', async () => {
    const { api, assignment } = await notStarted();
    const phone = await createDeviceDrafts();
    const onPhone = await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api, drafts: phone.drafts });
    const sealed = screen.getByTestId('privacy-notice');
    expect(sealed).toHaveTextContent(/Staff never see them one by one/);
    // The fixtures keep the made-up data on the phone after sending: the real build's "until sent" would be untrue.
    expect(sealed).not.toHaveTextContent(/Development|until sent|encrypted/);
    await onPhone.unmount();
    phone.close();

    await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api });
    expect(screen.getByTestId('privacy-notice')).not.toHaveTextContent(/Development|Nothing is saved/);
  });

  it('cannot start a questionnaire with a question type this app cannot show', async () => {
    const { api, assignment } = await notStarted();
    const section = PLACEHOLDER_VERSION.sections.find((candidate) => candidate.code === 'B');
    const [first] = section?.items ?? [];
    jest.spyOn(api, 'getPublishedVersion').mockResolvedValue({
      ...PLACEHOLDER_VERSION,
      sections: section && first ? [{ ...section, items: [{ ...first, itemType: 'unsupported', options: [] }] }] : [],
    });
    await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api });

    expect(await screen.findByText('Update the app')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled();
  });

  it('cannot start when the version cannot be loaded, and says why', async () => {
    const { api, fail, assignment } = await notStarted();
    fail('getPublishedVersion', new ApiError('not_found', 404, 'questionnaires.version_not_found'));
    await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api });

    expect(await screen.findByText("Version not available.")).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled();
  });

  it('says a submission is waiting instead of offering to continue', async () => {
    const lead = await leadSession();
    await answerEverything(lead.api, lead.sessionId);
    const assignment = await lead.api.getAssignment(lead.assignmentId);
    const { engine } = await renderWithProviders(<AssessmentDetail assignment={assignment} />, {
      api: lead.api,
      connectivity: createFakeConnectivity('offline'),
    });
    expect(screen.getByRole('button', { name: 'Continue' })).toBeOnTheScreen();

    await act(() => engine.submissions.submit(lead.sessionId));

    expect(
      await screen.findByText("Answers will be submitted once you're online."),
    ).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Continue' })).toBeNull();
  });
});
