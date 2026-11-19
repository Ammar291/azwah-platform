import { act, fireEvent, screen, userEvent, waitFor, within } from '@testing-library/react-native';

import SessionScreen from '../../../../app/(app)/sessions/[sessionId]/index';
import { ApiError } from '@/api/client';
import { PLACEHOLDER_VERSION } from '@/test-utils/placeholderContent';
import { createDeviceDrafts } from '@/test-utils/deviceDrafts';
import { createFakeConnectivity } from '@/test-utils/fakeConnectivity';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { answerEverything, leadSession, optionOf, SECTION_A } from '@/test-utils/sessionWorlds';

import { requestQuestion } from '../questionRequests';
import { QUESTION_SETTLE_MS } from '../SessionQuestionnaire';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { router, resetRouterMock, setSearchParams } =
  jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

const SLOW = { timeout: 3000 };

/** Long enough after a question changed for Previous and Next to answer again. */
const questionSettled = () => act(() => new Promise<void>((resolve) => setTimeout(resolve, QUESTION_SETTLE_MS + 20)));

async function world() {
  const lead = await leadSession();
  setSearchParams({ sessionId: lead.sessionId });
  return lead;
}

beforeEach(() => resetRouterMock());
afterEach(async () => {
  await settleQueries();
  jest.restoreAllMocks();
});

describe('the question screen', () => {
  it("renders the server's question and options, and resumes at the first unanswered question", async () => {
    const { api } = await world();
    await renderWithProviders(<SessionScreen />, { api });

    expect(await screen.findByText('Question 3 of 4')).toBeOnTheScreen();
    expect(screen.getByRole('progressbar')).toHaveAccessibilityValue({ min: 0, max: 4, now: 3 });
    expect(screen.getByRole('header', { name: /Statement A3/ })).toBeOnTheScreen();
    expect(screen.getByText('Required')).toBeOnTheScreen();
    const radios = screen.getAllByRole('radio');
    expect(radios.map((radio) => radio.props.accessibilityLabel)).toEqual(
      SECTION_A[2]?.options.map((option) => option.label.en),
    );
  });

  it('shows an Arabic reader answers that have no Arabic yet in English, with no note about it (D-142)', async () => {
    const { api } = await world();

    jest.spyOn(api, 'getPublishedVersion').mockResolvedValue({
      ...PLACEHOLDER_VERSION,
      sections: PLACEHOLDER_VERSION.sections.map((section) => ({
        ...section,
        items: section.items.map((item) => ({
          ...item,
          options: item.options.map((option) => ({ ...option, label: { ...option.label, ar: null } })),
        })),
      })),
    });
    await renderWithProviders(<SessionScreen />, { api, language: 'ar' });
    expect(await screen.findByRole('radio', { name: '[DEV] Option 1' })).toBeOnTheScreen();
    expect(screen.getByText('السؤال 3 من 4')).toBeOnTheScreen();
    expect(screen.queryByText(/بالإنجليزية|الاعتماد/)).toBeNull();
  });

  it('shows no demo buttons while demo tools are off (D-130)', async () => {
    const { api } = await world();
    await renderWithProviders(<SessionScreen />, { api });

    expect(await screen.findByText('Question 3 of 4')).toBeOnTheScreen();
    expect(screen.queryByText('Demo: fill in answers')).toBeNull();
  });

  it('marks an optional question as optional', async () => {
    const { api, notStartedId } = await world();
    const session = await api.startSession(notStartedId);
    setSearchParams({ sessionId: session.id });
    requestQuestion(session.id, 2);
    await renderWithProviders(<SessionScreen />, { api });

    expect(await screen.findByText('Question 3 of 3')).toBeOnTheScreen();
    expect(screen.getByText('Optional')).toBeOnTheScreen();
  });

  it('moves back and forward between questions and leads to the review', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    await renderWithProviders(<SessionScreen />, { api });

    await screen.findByText('Question 3 of 4');
    await user.press(screen.getByRole('button', { name: 'Previous' }));
    expect(screen.getByText('Question 2 of 4')).toBeOnTheScreen();
    expect(screen.getByRole('radio', { name: '[DEV] Option 3' })).toBeChecked();

    await questionSettled();
    await user.press(screen.getByRole('button', { name: 'Next' }));
    await questionSettled();
    await user.press(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByText('Question 4 of 4')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Next' })).toBeNull();
    await user.press(screen.getByRole('button', { name: 'Review' }));

    expect(router.push).toHaveBeenCalledWith({ pathname: '/sessions/[sessionId]/review', params: { sessionId } });
  });

  it('moves one question on a double tap of Next: the second tap lands on the new question and is ignored', async () => {
    const { api } = await world();
    await renderWithProviders(<SessionScreen />, { api });
    await screen.findByText('Question 3 of 4');
    await fireEvent.press(screen.getByRole('button', { name: 'Previous' }));
    await questionSettled();
    await fireEvent.press(screen.getByRole('button', { name: 'Previous' }));
    expect(screen.getByText('Question 1 of 4')).toBeOnTheScreen();
    await questionSettled();

    // The second tap arrives after the next question is on screen, as a double tap on a phone does.
    await fireEvent.press(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByText('Question 2 of 4')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Next' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Previous' }));

    expect(screen.getByText('Question 2 of 4')).toBeOnTheScreen();

    // A moment later the buttons answer again.
    await questionSettled();
    await fireEvent.press(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByText('Question 3 of 4')).toBeOnTheScreen();
  });

  it('opens the question the review asks for, and says a required question still needs an answer', async () => {
    const { api, sessionId } = await world();
    requestQuestion(sessionId, 3);
    await renderWithProviders(<SessionScreen />, { api });

    expect(await screen.findByText('Question 4 of 4')).toBeOnTheScreen();
    expect(screen.getByText('Answer this to submit.')).toBeOnTheScreen();

    await act(async () => requestQuestion(sessionId, 0));
    expect(screen.getByText('Question 1 of 4')).toBeOnTheScreen();
    expect(screen.queryByText('Answer this to submit.')).toBeNull();
  });

  it('explains a questionnaire with a question type this app cannot show, and offers no answers', async () => {
    const { api } = await world();
    const [first, ...rest] = PLACEHOLDER_VERSION.sections;
    jest.spyOn(api, 'getPublishedVersion').mockResolvedValue({
      ...PLACEHOLDER_VERSION,
      sections: [
        { ...first!, items: first!.items.map((item, i) => (i === 1 ? { ...item, itemType: 'unsupported' as const, options: [] } : item)) },
        ...rest,
      ],
    });
    await renderWithProviders(<SessionScreen />, { api });

    expect(await screen.findByText('Update the app')).toBeOnTheScreen();
    expect(screen.queryAllByRole('radio')).toEqual([]);
  });

  it('explains a section with no questions instead of loading forever', async () => {
    const { api } = await world();
    jest.spyOn(api, 'getPublishedVersion').mockResolvedValue({
      ...PLACEHOLDER_VERSION,
      sections: PLACEHOLDER_VERSION.sections.map((section) => ({ ...section, items: [] })),
    });
    await renderWithProviders(<SessionScreen />, { api });

    expect(await screen.findByRole('alert')).toHaveTextContent('No questions');
  });
});

describe('saving answers', () => {
  it('saves an answer on the phone at once, then on the server, and says when', async () => {
    const user = userEvent.setup();
    const { api, sessionId, argsOf } = await world();
    const phone = await createDeviceDrafts();
    await renderWithProviders(<SessionScreen />, { api, drafts: phone.drafts });

    await user.press(await screen.findByRole('radio', { name: '[DEV] Option 2' }));

    expect(await screen.findByText('Saved on this phone')).toBeOnTheScreen();
    expect(await screen.findByText('All answers saved', {}, SLOW)).toBeOnTheScreen();
    expect(screen.getByText(/^Saved to Azwah /)).toBeOnTheScreen();
    expect(argsOf('saveAnswers')).toEqual([[sessionId, [{ itemId: SECTION_A[2]?.id, optionId: optionOf(2, 1) }]]]);
    expect(await phone.drafts.listPending(sessionId)).toEqual([]);
    phone.close();
  });

  it('keeps an answer that could not be sent, retries by itself, and can be retried by hand', async () => {
    const user = userEvent.setup();
    const { api, sessionId, fail, count } = await world();
    const phone = await createDeviceDrafts();
    fail('saveAnswers', new ApiError('server', 503));
    fail('saveAnswers', new ApiError('server', 503));
    await renderWithProviders(<SessionScreen />, { api, drafts: phone.drafts, engine: { retry: { baseMs: 5_000, maxMs: 5_000, maxAttempts: 3 } } });

    await user.press(await screen.findByRole('radio', { name: '[DEV] Option 1' }));

    expect(await screen.findByText('Retrying…', {}, SLOW)).toBeOnTheScreen();
    expect(await phone.drafts.listPending(sessionId)).toHaveLength(1);
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await user.press(await screen.findByRole('button', { name: 'Try again' }, SLOW));

    expect(await screen.findByText('All answers saved', {}, SLOW)).toBeOnTheScreen();
    expect(count('saveAnswers')).toBe(3);
    phone.close();
  });

  it('keeps answers on the phone while offline, says so, and sends them when the connection returns', async () => {
    const user = userEvent.setup();
    const { api, sessionId, count } = await world();
    const phone = await createDeviceDrafts();
    const connectivity = createFakeConnectivity();
    await renderWithProviders(<SessionScreen />, { api, drafts: phone.drafts, connectivity });
    await screen.findByText('Question 3 of 4');

    await act(async () => connectivity.set('offline'));
    await user.press(screen.getByRole('radio', { name: '[DEV] Option 4' }));

    expect(
      await screen.findByText(
        "Offline. Saved on this phone.",
        {},
        SLOW,
      ),
    ).toBeOnTheScreen();
    expect(count('saveAnswers')).toBe(0);

    await act(async () => connectivity.set('online'));

    expect(await screen.findByText('All answers saved', {}, SLOW)).toBeOnTheScreen();
    expect(await phone.drafts.listPending(sessionId)).toEqual([]);
    phone.close();
  });

  it('resumes after a restart: drafts left on the phone are shown and sent', async () => {
    const { api, sessionId, argsOf } = await world();
    const phone = await createDeviceDrafts();
    await phone.drafts.saveAnswer({
      sessionId,
      itemId: SECTION_A[2]?.id ?? '',
      optionId: optionOf(2, 3),
      baseModifiedAt: null,
      at: new Date(),
    });

    await renderWithProviders(<SessionScreen />, { api, drafts: phone.restart().drafts });

    expect(await screen.findByText('Question 4 of 4')).toBeOnTheScreen();
    expect(await screen.findByText('All answers saved', {}, SLOW)).toBeOnTheScreen();
    expect(argsOf('saveAnswers')).toEqual([[sessionId, [{ itemId: SECTION_A[2]?.id, optionId: optionOf(2, 3) }]]]);
    phone.close();
  });

  it('opens offline after a restart from the copy kept on the phone, and says it is a copy', async () => {
    const { api, sessionId, fail } = await world();
    const phone = await createDeviceDrafts();
    const online = await renderWithProviders(<SessionScreen />, { api, drafts: phone.drafts });
    await screen.findByText('Question 3 of 4');
    await waitFor(async () => expect(await phone.drafts.loadCopy(sessionId)).not.toBeNull());
    await online.unmount();

    for (const method of ['getSession', 'getAnswers', 'getAssignment', 'getPublishedVersion'] as const) {
      fail(method);
    }
    await renderWithProviders(<SessionScreen />, { api, drafts: phone.restart().drafts });

    expect(await screen.findByText('Copy saved', { exact: false })).toBeOnTheScreen();
    expect(screen.getByText('Question 3 of 4')).toBeOnTheScreen();
    expect(screen.getByRole('radio', { name: '[DEV] Option 1' })).toBeOnTheScreen();
    phone.close();
  });

  it('says a questionnaire never opened on this phone cannot be shown offline', async () => {
    const { api } = await world();
    await renderWithProviders(<SessionScreen />, { api, connectivity: createFakeConnectivity('offline') });

    expect(await screen.findByText('Connect to open this questionnaire.', { exact: false })).toBeOnTheScreen();
  });
});

describe('conflicting edits', () => {
  it('shows both answers when the question changed elsewhere, and keeps the one the person picks', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    const phone = await createDeviceDrafts();
    // On this phone, earlier and offline: question 2 changed from Option 3 to Option 4.
    const before = (await api.getAnswers(sessionId)).responses.find((r) => r.itemId === SECTION_A[1]?.id);
    await phone.drafts.saveAnswer({
      sessionId,
      itemId: SECTION_A[1]?.id ?? '',
      optionId: optionOf(1, 3),
      baseModifiedAt: before?.lastModifiedAt ?? null,
      at: new Date(),
    });
    // Meanwhile another phone changed it to Option 1.
    await api.saveAnswers(sessionId, [{ itemId: SECTION_A[1]?.id ?? '', optionId: optionOf(1, 0) }]);
    requestQuestion(sessionId, 1);

    await renderWithProviders(<SessionScreen />, { api, drafts: phone.restart().drafts });

    expect(await screen.findByText('Changed elsewhere', {}, SLOW)).toBeOnTheScreen();
    expect(screen.getByText(/Saved: “\[DEV\] Option 1”\. This phone: “\[DEV\] Option 4”/)).toBeOnTheScreen();
    expect((await api.getAnswers(sessionId)).responses.find((r) => r.itemId === SECTION_A[1]?.id)?.optionId).toBe(optionOf(1, 0));

    await user.press(screen.getByRole('button', { name: "Keep this phone's: [DEV] Option 4" }));

    await waitFor(async () =>
      expect((await api.getAnswers(sessionId)).responses.find((r) => r.itemId === SECTION_A[1]?.id)?.optionId).toBe(
        optionOf(1, 3),
      ),
    SLOW);
    expect(screen.queryByText('Changed elsewhere')).toBeNull();
    phone.close();
  });

  it("keeps the saved answer and drops this phone's when the person picks it", async () => {
    const user = userEvent.setup();
    const { api, sessionId, count } = await world();
    const phone = await createDeviceDrafts();
    const before = (await api.getAnswers(sessionId)).responses.find((r) => r.itemId === SECTION_A[1]?.id);
    await phone.drafts.saveAnswer({
      sessionId,
      itemId: SECTION_A[1]?.id ?? '',
      optionId: optionOf(1, 3),
      baseModifiedAt: before?.lastModifiedAt ?? null,
      at: new Date(),
    });
    await api.saveAnswers(sessionId, [{ itemId: SECTION_A[1]?.id ?? '', optionId: optionOf(1, 0) }]);
    requestQuestion(sessionId, 1);
    await renderWithProviders(<SessionScreen />, { api, drafts: phone.restart().drafts });

    await user.press(await screen.findByRole('button', { name: 'Keep saved: [DEV] Option 1' }, SLOW));

    expect(await screen.findByRole('radio', { name: '[DEV] Option 1' })).toBeChecked();
    expect(await phone.drafts.listPending(sessionId)).toEqual([]);
    expect(count('saveAnswers')).toBe(1);
    phone.close();
  });
});

describe('sessions that cannot be answered', () => {
  it('never shows another version than the one the session was started on', async () => {
    const { api } = await world();
    jest
      .spyOn(api, 'getPublishedVersion')
      .mockResolvedValue({ ...PLACEHOLDER_VERSION, versionId: '0199e0a1-0000-7000-8000-00000000ffff' });
    await renderWithProviders(<SessionScreen />, { api });

    expect(await screen.findByText("Version not available")).toBeOnTheScreen();
    expect(screen.queryAllByRole('radio')).toEqual([]);
  });

  it('says the version is unavailable when the server no longer serves it', async () => {
    const { api, fail } = await world();
    fail('getPublishedVersion', new ApiError('not_found', 404, 'questionnaires.version_not_found'));
    await renderWithProviders(<SessionScreen />, { api });

    expect(await screen.findByText("Version not available")).toBeOnTheScreen();
  });

  it('removes unsent answers of a session no longer available to this person, and says how many', async () => {
    const { api, sessionId, fail } = await world();
    const phone = await createDeviceDrafts();
    await phone.drafts.saveAnswer({ sessionId, itemId: SECTION_A[2]?.id ?? '', optionId: optionOf(2, 0), baseModifiedAt: null, at: new Date() });
    fail('getSession', new ApiError('not_found', 404, 'sessions.session_not_found'));
    fail('getAnswers', new ApiError('not_found', 404, 'sessions.session_not_found'));
    await renderWithProviders(<SessionScreen />, { api, drafts: phone.drafts });

    expect(await screen.findByText('No longer available')).toBeOnTheScreen();
    expect(await screen.findByText(/Unsent when it closed: 1\./)).toBeOnTheScreen();
    expect(await phone.drafts.listPending(sessionId)).toEqual([]);
    phone.close();
  });

  it('says how many answers made offline could not be added once the session was submitted elsewhere', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    const phone = await createDeviceDrafts();
    const connectivity = createFakeConnectivity();
    await renderWithProviders(<SessionScreen />, { api, drafts: phone.drafts, connectivity });
    await screen.findByText('Question 3 of 4');
    await act(async () => connectivity.set('offline'));
    await user.press(screen.getByRole('radio', { name: '[DEV] Option 2' }));
    await screen.findByText(/^Offline\./, {}, SLOW);
    // Meanwhile, on another phone, everything is answered and submitted.
    await answerEverything(api, sessionId);
    await api.submitSession(sessionId, 'another-phone');

    await act(async () => connectivity.set('online'));

    expect(await screen.findByText('Already submitted', {}, SLOW)).toBeOnTheScreen();
    expect(await screen.findByText(/Unsent when it closed: 1\./)).toBeOnTheScreen();
    expect(await phone.drafts.listPending(sessionId)).toEqual([]);
    phone.close();
  });

  it('says the time to answer has passed when the server refuses answers, removes them and takes no more', async () => {
    const user = userEvent.setup();
    const { api, sessionId, fail, count } = await world();
    const phone = await createDeviceDrafts();
    fail('saveAnswers', new ApiError('conflict', 409, 'assignments.assignment_expired'));
    await renderWithProviders(<SessionScreen />, { api, drafts: phone.drafts });

    await user.press(await screen.findByRole('radio', { name: '[DEV] Option 2' }));

    const closed = await screen.findByTestId('session-expired', {}, SLOW);
    expect(within(closed).getByText('Questionnaire closed')).toBeOnTheScreen();
    expect(within(closed).getByText('The time to answer has passed.')).toBeOnTheScreen();
    expect(screen.getByText(/Unsent when it closed: 1\./)).toBeOnTheScreen();
    expect(await phone.drafts.listPending(sessionId)).toEqual([]);
    expect(screen.getByRole('radio', { name: '[DEV] Option 1' })).toBeDisabled();
    await act(() => new Promise<void>((resolve) => setTimeout(resolve, 400)));
    expect(count('saveAnswers')).toBe(1);
    phone.close();
  });

  it('explains an abandoned session without offering a result', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    const session = await api.getSession(sessionId);
    jest.spyOn(api, 'getSession').mockResolvedValue({ ...session, status: 'abandoned' });
    await renderWithProviders(<SessionScreen />, { api });

    expect(await screen.findByText('Questionnaire closed')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'View result' })).toBeNull();
    await user.press(screen.getByRole('button', { name: 'Questionnaires' }));
    expect(router.replace).toHaveBeenCalledWith('/assessments');
  });

  it('leaves a submitted session read-only', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    await answerEverything(api, sessionId);
    await api.submitSession(sessionId, 'key-1');
    await renderWithProviders(<SessionScreen />, { api });

    expect(await screen.findByText('Already submitted')).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'View result' }));
    expect(router.replace).toHaveBeenCalledWith({ pathname: '/sessions/[sessionId]/result', params: { sessionId } });
  });
});
