import { act, screen, userEvent, within } from '@testing-library/react-native';
import { I18nManager, StyleSheet } from 'react-native';

import ResultScreen from '../../../../app/(app)/sessions/[sessionId]/result';
import ReviewScreen from '../../../../app/(app)/sessions/[sessionId]/review';
import SessionScreen from '../../../../app/(app)/sessions/[sessionId]/index';
import { AssessmentDetail } from '@/features/assessments/AssessmentDetail';
import { createDeviceDrafts } from '@/test-utils/deviceDrafts';
import { createFakeConnectivity } from '@/test-utils/fakeConnectivity';
import { PLACEHOLDER_VERSION } from '@/test-utils/placeholderContent';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { answerEverything, leadSession, optionOf, SECTION_A } from '@/test-utils/sessionWorlds';

import { requestQuestion } from '../questionRequests';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { resetRouterMock, setSearchParams } = jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

const ARABIC_INDIC_DIGITS = /[٠-٩۰-۹]/;
const SLOW = { timeout: 3000 };

const isMirrored = (icon: ReturnType<typeof screen.getByTestId>) =>
  JSON.stringify((StyleSheet.flatten(icon.props.style) as { transform?: object[] }).transform ?? []).includes(
    '"scaleX":-1',
  );

async function world() {
  const lead = await leadSession();
  setSearchParams({ sessionId: lead.sessionId });
  return lead;
}

beforeEach(() => {
  resetRouterMock();
  // The layout direction a development build uses for Arabic.
  jest.replaceProperty(I18nManager, 'isRTL', true);
});
afterEach(async () => {
  await settleQueries();
  jest.restoreAllMocks();
});

describe('taking a questionnaire in Arabic, right to left', () => {
  it('shows the question, its Arabic options, progress with Western digits and a forward chevron pointing left', async () => {
    const { api } = await world();
    await renderWithProviders(<SessionScreen />, { api, language: 'ar' });

    expect(await screen.findByText('السؤال 3 من 4')).toBeOnTheScreen();
    expect(screen.getByRole('header', { name: /العبارة A3/ })).toBeOnTheScreen();
    expect(screen.getByText('مطلوب')).toBeOnTheScreen();
    expect(screen.getByRole('radio', { name: '[تجريبي] الخيار 1' })).toBeOnTheScreen();
    expect(screen.getByRole('radio', { name: '[تجريبي] خيار خارج المقياس' })).toBeOnTheScreen();
    const next = screen.getByRole('button', { name: 'التالي' });
    expect(isMirrored(within(next).getByTestId('icon-chevron-forward', { includeHiddenElements: true }))).toBe(true);
    expect(JSON.stringify(screen.toJSON())).not.toMatch(ARABIC_INDIC_DIGITS);
  });

  it('says in Arabic that answers wait on the phone while offline', async () => {
    const user = userEvent.setup();
    const { api } = await world();
    const phone = await createDeviceDrafts();
    const connectivity = createFakeConnectivity();
    await renderWithProviders(<SessionScreen />, { api, language: 'ar', drafts: phone.drafts, connectivity });
    await screen.findByText('السؤال 3 من 4');
    await act(async () => connectivity.set('offline'));

    await user.press(screen.getByRole('radio', { name: '[تجريبي] الخيار 2' }));

    expect(
      await screen.findByText('غير متصل. حُفظت على هذا الهاتف.', {}, SLOW),
    ).toBeOnTheScreen();
    phone.close();
  });

  it('names both answers of a conflict in Arabic', async () => {
    const { api, sessionId } = await world();
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

    await renderWithProviders(<SessionScreen />, { api, language: 'ar', drafts: phone.drafts });

    expect(await screen.findByText('غُيّرت من مكان آخر', {}, SLOW)).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'إبقاء المحفوظة: [تجريبي] الخيار 1' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'إبقاء إجابة الهاتف: [تجريبي] الخيار 4' })).toBeOnTheScreen();
    phone.close();
  });

  it('reviews in Arabic, with the summary in Western digits and the finality explained', async () => {
    const { api } = await world();
    await renderWithProviders(<ReviewScreen />, { api, language: 'ar' });

    expect(await screen.findByText('أُجيب عن 2 من 4')).toBeOnTheScreen();
    expect(screen.getByText('مطلوبة بلا إجابة: 2.')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'إرسال' })).toBeDisabled();
    expect(JSON.stringify(screen.toJSON())).not.toMatch(ARABIC_INDIC_DIGITS);
  });

  it('confirms a submission in Arabic', async () => {
    const { api, sessionId } = await world();
    await answerEverything(api, sessionId);
    await api.submitSession(sessionId, 'key-1');
    await renderWithProviders(<ResultScreen />, { api, language: 'ar' });

    expect(await screen.findByText('شكرًا لك!')).toBeOnTheScreen();
    expect(screen.getByText('بانتظار النتيجة')).toBeOnTheScreen();
    expect(JSON.stringify(screen.toJSON())).not.toMatch(ARABIC_INDIC_DIGITS);
  });

  it('introduces a questionnaire in Arabic, with the privacy notice', async () => {
    const { api, assignmentId } = await world();
    const assignment = await api.getAssignment(assignmentId);
    // A draft's description is not shown (AssessmentDetail); an approved version's is.
    jest.spyOn(api, 'getPublishedVersion').mockResolvedValue({ ...PLACEHOLDER_VERSION, status: 'approved' });
    await renderWithProviders(<AssessmentDetail assignment={assignment} />, { api, language: 'ar' });

    expect(await screen.findByText('نبذة')).toBeOnTheScreen();
    expect(screen.getByText('[عنصر نائب للتطوير] محتوى مصطنع لبناء التطبيق.')).toBeOnTheScreen();
    expect(screen.getByLabelText('عدد الأسئلة: 4')).toBeOnTheScreen();
    expect(screen.getByLabelText('الجولة: 2')).toBeOnTheScreen();
    expect(screen.getByTestId('privacy-notice')).toHaveTextContent(/تُرسل إجاباتك إلى عزوة/);
  });
});
