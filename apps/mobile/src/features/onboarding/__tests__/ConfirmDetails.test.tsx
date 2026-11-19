import { act, screen, userEvent, waitFor, within } from '@testing-library/react-native';

import { ApiError } from '@/api/client';
import type { ParticipantApi } from '@/api/participant/participantApi';
import { createDevelopmentMockAuth } from '@/auth/developmentMockAuth';
import type { IdentityProfile } from '@/auth/identityProfile';
import { createAuthStateStore } from '@/auth/observable';
import type { AuthProvider } from '@/auth/types';
import { createFixtureParticipantApi } from '@/dev-fixtures/fixtureParticipantApi';
import { createSettingsRepository } from '@/storage/settings';
import { FakeSqlDatabase } from '@/storage/testing/FakeSqlDatabase';
import { resetRouterMock, router } from '@/test-utils/expoRouterMock';
import { PLACEHOLDER_CATALOG } from '@/test-utils/placeholderContent';
import { renderWithProviders } from '@/test-utils/renderWithProviders';

import { ConfirmDetails } from '../ConfirmDetails';
import { OnboardingProvider, type OnboardingState } from '../OnboardingContext';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));

const STAGING_PROFILE: IdentityProfile = {
  source: 'uae_pass_staging',
  nameEn: 'Mariam Ahmed Al Mansoori',
  nameAr: 'مريم أحمد المنصوري',
  givenNameEn: 'Mariam',
  givenNameAr: 'مريم',
  nationalityEn: 'United Arab Emirates',
  nationalityAr: 'الإمارات العربية المتحدة',
  gender: 'female',
  birthdate: '1988-03-14',
  emiratesIdMasked: '784-••••-•••••42-1',
  mobileMasked: '+971 •• ••• ••67',
  emailMasked: 'm•••@example.ae',
};

/** A signed-in person with these released details (or none), on the fixture API unless one is given. */
async function renderConfirm({
  profile = STAGING_PROFILE,
  language = 'en' as const,
  api,
}: { profile?: IdentityProfile | null; language?: 'en' | 'ar'; api?: (subject: () => string | null) => ParticipantApi } = {}) {
  const auth = createDevelopmentMockAuth({
    settings: createSettingsRepository(new FakeSqlDatabase()),
    personas: [{ code: 'new_participant', subject: 'uaepass-stg|1', ...(profile ? { profile } : {}) }],
  });
  await auth.restore();
  await auth.signIn({ method: 'development_persona', personaCode: 'new_participant' });
  const subject = () => {
    const state = auth.getState();
    return state.status === 'signed_in' ? state.session.subject : null;
  };
  const onboarding: OnboardingState = {
    active: true,
    finishing: false,
    registration: 'required',
    beginFinishing: jest.fn(),
    finish: jest.fn(),
  };
  const rendered = await renderWithProviders(
    <OnboardingProvider value={onboarding}>
      <ConfirmDetails />
    </OnboardingProvider>,
    {
      mode: 'api',
      auth,
      language,
      api: api?.(subject) ?? createFixtureParticipantApi({ getSubject: subject, latencyMs: 0, catalog: PLACEHOLDER_CATALOG }),
    },
  );
  return { ...rendered, onboarding };
}

beforeEach(() => resetRouterMock());

describe('ConfirmDetails', () => {
  it('shows the released details, masked, with where they came from and what Azwah keeps', async () => {
    await renderConfirm();

    expect(screen.getByText('Step 1 of 2')).toBeOnTheScreen();
    expect(screen.getByText('Mariam Ahmed Al Mansoori')).toBeOnTheScreen();
    expect(screen.getByText('مريم أحمد المنصوري')).toBeOnTheScreen();
    expect(screen.getByText('MM')).toBeOnTheScreen();
    // A staging account reads "UAE PASS": no "(staging)" label in the app (D-142).
    expect(screen.getByText('UAE PASS')).toBeOnTheScreen();
    expect(screen.queryByText(/staging/i)).toBeNull();
    expect(screen.getByLabelText('Emirates ID: 784-••••-•••••42-1')).toBeOnTheScreen();
    expect(screen.getByLabelText('Date of birth: 14 March 1988')).toBeOnTheScreen();
    expect(screen.getByLabelText('Gender: Female')).toBeOnTheScreen();
    expect(screen.getByLabelText('Mobile: +971 •• ••• ••67')).toBeOnTheScreen();
    expect(screen.getByText(/keeps only a private reference, not these details/)).toBeOnTheScreen();
  });

  it('registers only once the person says the details are theirs, then opens account ready', async () => {
    const user = userEvent.setup();
    const { onboarding, api } = await renderConfirm();
    const confirm = screen.getByRole('button', { name: 'Confirm' });
    expect(confirm).toBeDisabled();

    await user.press(screen.getByRole('switch', { name: 'These details are mine and correct' }));
    await user.press(confirm);

    await waitFor(() => expect(onboarding.beginFinishing).toHaveBeenCalledTimes(1));
    expect(router.replace).toHaveBeenCalledWith('/account-ready');
    expect((await api.getMe()).registeredAt).toEqual(expect.any(String));
  });

  it('sends nothing about the details when registering', async () => {
    const user = userEvent.setup();
    const registerMe = jest.fn(async () => ({ registeredAt: '2026-10-24T08:00:00Z' }));
    await renderConfirm({
      api: (subject) => ({
        ...createFixtureParticipantApi({ getSubject: subject, latencyMs: 0, catalog: PLACEHOLDER_CATALOG }),
        registerMe,
      }),
    });

    await user.press(screen.getByRole('switch', { name: 'These details are mine and correct' }));
    await user.press(screen.getByRole('button', { name: 'Confirm' }));

    expect(registerMe).toHaveBeenCalledWith();
  });

  it('explains being offline and keeps the person on the screen', async () => {
    const user = userEvent.setup();
    const { onboarding } = await renderConfirm({
      api: (subject) => ({
        ...createFixtureParticipantApi({ getSubject: subject, latencyMs: 0, catalog: PLACEHOLDER_CATALOG }),
        registerMe: async () => {
          throw new ApiError('network');
        },
      }),
    });

    await user.press(screen.getByRole('switch', { name: 'These details are mine and correct' }));
    await user.press(screen.getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/You’re offline/);
    expect(onboarding.beginFinishing).not.toHaveBeenCalled();
    expect(router.replace).not.toHaveBeenCalled();
  });

  it('says so in the dialog when "these aren’t my details" cannot sign out, and signs out on another try', async () => {
    const user = userEvent.setup();
    const { auth } = await renderConfirm();
    const signOut = jest.spyOn(auth, 'signOut').mockRejectedValueOnce(new Error('storage'));

    await user.press(screen.getByRole('button', { name: 'Not my details' }));
    const dialog = within(screen.getByTestId('confirmation-dialog'));
    await user.press(dialog.getByRole('button', { name: 'Sign out' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(/Something went wrong/);
    expect(auth.getState()).toMatchObject({ status: 'signed_in' });
    expect(dialog.getByRole('header', { name: 'Not your details?' })).toBeOnTheScreen();

    await user.press(dialog.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => expect(auth.getState()).toMatchObject({ status: 'signed_out' }));
    expect(signOut).toHaveBeenCalledTimes(2);
  });

  it('lets a person whose provider released no details confirm the account is theirs', async () => {
    await renderConfirm({ profile: null });

    expect(screen.getByText(/No details shared/)).toBeOnTheScreen();
    expect(screen.getByRole('switch', { name: 'This account is mine' })).toBeOnTheScreen();
    expect(screen.queryByText('Emirates ID')).toBeNull();
  });

  it('asks again when the details arrive after the person agreed (a renewal after a restart)', async () => {
    const user = userEvent.setup();
    const store = createAuthStateStore({
      status: 'signed_in',
      session: { subject: 'uaepass-stg|1', personaCode: null, profile: null },
    });
    const auth: AuthProvider = {
      kind: 'oidc',
      getState: store.get,
      subscribe: store.subscribe,
      restore: async () => undefined,
      signIn: async () => undefined,
      signOut: async () => undefined,
      getAccessToken: async () => null,
      reportUnauthorized: () => undefined,
    };
    await renderWithProviders(<ConfirmDetails />, {
      mode: 'api',
      auth,
      api: createFixtureParticipantApi({ getSubject: () => 'uaepass-stg|1', latencyMs: 0, catalog: PLACEHOLDER_CATALOG }),
    });
    await user.press(screen.getByRole('switch', { name: 'This account is mine' }));
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeEnabled();

    await act(async () =>
      store.set({ status: 'signed_in', session: { subject: 'uaepass-stg|1', personaCode: null, profile: STAGING_PROFILE } }),
    );

    expect(screen.getByRole('switch', { name: 'These details are mine and correct' })).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeDisabled();
  });

  it('shows details from an unrecognised source without claiming UAE PASS', async () => {
    await renderConfirm({ profile: { ...STAGING_PROFILE, source: 'unverified' } });

    expect(screen.getByText('Your sign-in')).toBeOnTheScreen();
    expect(screen.queryByText(/From UAE PASS/)).toBeNull();
  });

  it('shows a Google account’s details as from Google, never UAE PASS (D-147)', async () => {
    await renderConfirm({
      profile: {
        ...STAGING_PROFILE,
        source: 'google',
        nameAr: null,
        givenNameAr: null,
        emiratesIdMasked: null,
        nationalityEn: null,
        nationalityAr: null,
        mobileMasked: null,
      },
    });

    expect(screen.getByText('Google account')).toBeOnTheScreen();
    expect(screen.queryByText(/UAE PASS/)).toBeNull();
  });

  it('speaks Arabic, with the Arabic name first', async () => {
    await renderConfirm({ language: 'ar' });

    expect(screen.getByRole('header', { name: 'أكّد بياناتك' })).toBeOnTheScreen();
    expect(screen.getByText('مريم أحمد المنصوري')).toBeOnTheScreen();
    expect(screen.getByLabelText('الجنسية: الإمارات العربية المتحدة')).toBeOnTheScreen();
    expect(screen.getByText('الخطوة 1 من 2')).toBeOnTheScreen();
  });
});
