import { act, screen, userEvent, waitFor, within } from '@testing-library/react-native';

import ReviewScreen from '../../../../app/(app)/sessions/[sessionId]/review';
import { ApiError } from '@/api/client';
import { createMemoryDraftStore } from '@/drafts/memoryDraftStore';
import { createDeviceDrafts } from '@/test-utils/deviceDrafts';
import { createFakeConnectivity } from '@/test-utils/fakeConnectivity';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { answerEverything, leadSession, SECTION_A } from '@/test-utils/sessionWorlds';

import { useQuestionRequest } from '../questionRequests';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { router, resetRouterMock, setSearchParams } =
  jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

const SLOW = { timeout: 3000 };

async function world() {
  const lead = await leadSession();
  setSearchParams({ sessionId: lead.sessionId });
  return lead;
}

const submit = async (user: ReturnType<typeof userEvent.setup>) => {
  await user.press(await screen.findByRole('button', { name: 'Submit' }));
  await user.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Submit' }));
};

beforeEach(() => resetRouterMock());
afterEach(async () => {
  await settleQueries();
  jest.restoreAllMocks();
});

describe('reviewing answers', () => {
  it('summarises the answers, lists unanswered required questions and does not allow submitting yet', async () => {
    const { api } = await world();
    await renderWithProviders(<ReviewScreen />, { api });

    expect(await screen.findByText('Answered 2 of 4')).toBeOnTheScreen();
    expect(screen.getByText(/Required, unanswered: 2\./)).toBeOnTheScreen();
    expect(screen.getAllByText('Not answered')).toHaveLength(2);
    expect(screen.getAllByText('Required')).toHaveLength(2);
    expect(screen.getByRole('button', { name: 'Submit' })).toBeDisabled();
  });

  it('takes the person to the first unanswered required question', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    await renderWithProviders(<ReviewScreen />, { api });

    await user.press(await screen.findByRole('button', { name: 'Go to the first' }));

    expect(router.back).toHaveBeenCalledTimes(1);
    let request: ReturnType<typeof useQuestionRequest> = null;
    function Probe() {
      request = useQuestionRequest(sessionId);
      return null;
    }
    await renderWithProviders(<Probe />, { api });
    expect(request).toMatchObject({ index: 2 });
  });

  it('opens any question to change its answer, under the review rather than as a second copy', async () => {
    const user = userEvent.setup();
    const { api } = await world();
    await renderWithProviders(<ReviewScreen />, { api });

    await user.press(await screen.findByRole('button', { name: /^Question 2: .*Answer: \[DEV\] Option 3$/ }));

    expect(router.back).toHaveBeenCalledTimes(1);
    expect(router.push).not.toHaveBeenCalled();
  });

  it('opens the questionnaire in place of a review that was opened directly', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    router.canGoBack.mockImplementation(() => false);
    await renderWithProviders(<ReviewScreen />, { api });

    await user.press(await screen.findByRole('button', { name: /^Question 1: / }));

    expect(router.replace).toHaveBeenCalledWith({ pathname: '/sessions/[sessionId]', params: { sessionId } });
  });

  it('holds submission while an answer conflicts with a newer one saved elsewhere', async () => {
    const { api, sessionId } = await world();
    await answerEverything(api, sessionId);
    const phone = await createDeviceDrafts();
    const before = (await api.getAnswers(sessionId)).responses.find((r) => r.itemId === SECTION_A[0]?.id);
    await phone.drafts.saveAnswer({
      sessionId,
      itemId: SECTION_A[0]?.id ?? '',
      optionId: SECTION_A[0]?.options[2]?.id ?? '',
      baseModifiedAt: before?.lastModifiedAt ?? null,
      at: new Date(),
    });
    await api.saveAnswers(sessionId, [{ itemId: SECTION_A[0]?.id ?? '', optionId: SECTION_A[0]?.options[3]?.id ?? '' }]);

    await renderWithProviders(<ReviewScreen />, { api, drafts: phone.drafts });

    expect(await screen.findByText('Choose answers to keep', {}, SLOW)).toBeOnTheScreen();
    expect(screen.getByText('Changed elsewhere')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Submit' })).toBeDisabled();
    phone.close();
  });
});

describe('submitting', () => {
  it('asks first, explains that it is final, submits once with an Idempotency-Key and shows the result', async () => {
    const user = userEvent.setup();
    const { api, sessionId, argsOf } = await world();
    await answerEverything(api, sessionId);
    const phone = await createDeviceDrafts();
    await renderWithProviders(<ReviewScreen />, { api, drafts: phone.drafts });

    expect(await screen.findByText('Submitted answers are final.')).toBeOnTheScreen();
    await user.press(await screen.findByRole('button', { name: 'Submit' }));
    expect(
      screen.getByText(
        "They're final once submitted.",
      ),
    ).toBeOnTheScreen();
    await user.press(within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Submit' }));

    await waitFor(() =>
      expect(router.push).toHaveBeenCalledWith({ pathname: '/sessions/[sessionId]/result', params: { sessionId } }),
    );
    // The finished questionnaire's screens leave the stack first, so Back from the result goes Home.
    expect(router.dismissTo).toHaveBeenCalledWith('/home');
    expect(router.dismissTo.mock.invocationCallOrder[0]).toBeLessThan(router.push.mock.invocationCallOrder[0] ?? 0);
    expect(router.replace).not.toHaveBeenCalled();
    expect(argsOf('submitSession')).toEqual([[sessionId, expect.stringMatching(/^[0-9a-f-]{36}$/)]]);
    expect(await phone.drafts.listAnswers(sessionId)).toEqual([]);
    expect(await phone.drafts.loadCopy(sessionId)).toBeNull();
    phone.close();
  });

  it('submits once however quickly the button is pressed again', async () => {
    const user = userEvent.setup();
    const { api, sessionId, count } = await world();
    await answerEverything(api, sessionId);
    await renderWithProviders(<ReviewScreen />, { api });

    await user.press(await screen.findByRole('button', { name: 'Submit' }));
    const confirm = within(screen.getByTestId('confirmation-dialog')).getByRole('button', { name: 'Submit' });
    await Promise.all([user.press(confirm), user.press(confirm)]);

    await waitFor(() => expect(router.push).toHaveBeenCalled());
    expect(count('submitSession')).toBe(1);
  });

  it('shows the result even when forgetting the local data fails after the server accepted the answers', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    await answerEverything(api, sessionId);
    const memory = createMemoryDraftStore();
    const drafts = { ...memory, discardSession: jest.fn(async () => Promise.reject(new Error('Storage failed.'))) };
    await renderWithProviders(<ReviewScreen />, { api, drafts });

    await submit(user);

    await waitFor(() =>
      expect(router.push).toHaveBeenCalledWith({ pathname: '/sessions/[sessionId]/result', params: { sessionId } }),
    );
    expect(drafts.discardSession).toHaveBeenCalledWith(sessionId);
    expect(screen.queryByText('Something went wrong')).toBeNull();
  });

  it('retries a submission whose answer was lost, with the same key, and submits once', async () => {
    const user = userEvent.setup();
    const { api, sessionId, lose, argsOf } = await world();
    await answerEverything(api, sessionId);
    lose('submitSession');
    await renderWithProviders(<ReviewScreen />, { api });

    await submit(user);

    await waitFor(() => expect(router.push).toHaveBeenCalled(), SLOW);
    const [first, second] = argsOf('submitSession');
    expect(second?.[1]).toBe(first?.[1]);
    expect(await api.getSession(sessionId)).toMatchObject({ status: 'submitted' });
  });

  it('queues a submission made offline, says it is not submitted yet, and submits when back online', async () => {
    const user = userEvent.setup();
    const { api, sessionId, count } = await world();
    await answerEverything(api, sessionId);
    const phone = await createDeviceDrafts();
    const connectivity = createFakeConnectivity();
    await renderWithProviders(<ReviewScreen />, { api, drafts: phone.drafts, connectivity });
    await screen.findByText('Answered 4 of 4');
    await act(async () => connectivity.set('offline'));

    await submit(user);

    const queued = await screen.findByTestId('submission-queued');
    expect(within(queued).getByText('Not submitted yet')).toBeOnTheScreen();
    expect(
      within(queued).getByText(
        "Offline. Answers are saved on this phone.",
      ),
    ).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Cancel submission' })).toBeOnTheScreen();
    expect(router.push).not.toHaveBeenCalled();
    expect(count('submitSession')).toBe(0);
    expect(await phone.drafts.getSubmission(sessionId)).not.toBeNull();

    await act(async () => connectivity.set('online'));

    await waitFor(() =>
      expect(router.push).toHaveBeenCalledWith({ pathname: '/sessions/[sessionId]/result', params: { sessionId } }),
    SLOW);
    expect(count('submitSession')).toBe(1);
    phone.close();
  });

  it('lets a queued submission be cancelled so answers can change', async () => {
    const user = userEvent.setup();
    const { api, sessionId } = await world();
    await answerEverything(api, sessionId);
    const connectivity = createFakeConnectivity();
    await renderWithProviders(<ReviewScreen />, { api, connectivity });
    await screen.findByText('Answered 4 of 4');
    await act(async () => connectivity.set('offline'));

    await submit(user);
    await user.press(await screen.findByRole('button', { name: 'Cancel submission' }));

    expect(await screen.findByRole('button', { name: 'Submit' })).toBeEnabled();
    expect(screen.queryByTestId('submission-queued')).toBeNull();
  });

  it.each([
    ['missing consent', new ApiError('forbidden', 403, 'sessions.participation_consent_required'), 'Give consent to take part first.'],
    ['missing answers', new ApiError('conflict', 409, 'sessions.required_items_unanswered'), "Answer the required questions."],
    ['a closed session', new ApiError('conflict', 409, 'sessions.not_in_progress'), 'Closed before submission.'],
    ['an expired questionnaire', new ApiError('conflict', 409, 'assignments.assignment_expired'), 'The time to answer has passed.'],
  ])('explains a submission refused for %s, without claiming it was submitted', async (_name, error, message) => {
    const user = userEvent.setup();
    const { api, sessionId, fail } = await world();
    await answerEverything(api, sessionId);
    fail('submitSession', error);
    await renderWithProviders(<ReviewScreen />, { api });

    await submit(user);

    const blocked = await screen.findByTestId('submission-blocked');
    expect(within(blocked).getByText('Not submitted')).toBeOnTheScreen();
    expect(within(blocked).getByText(message)).toBeOnTheScreen();
    expect(router.push).not.toHaveBeenCalled();
  });
});
