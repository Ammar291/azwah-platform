import { act, screen, userEvent, waitFor } from '@testing-library/react-native';

import { isDevelopmentMockAuth } from '@/auth/developmentMockAuth';
import { createOidcAuth } from '@/auth/oidc/oidcAuth';
import type { OidcConfig } from '@/auth/oidc/oidcConfig';
import {
  createFakeIdentityProvider,
  createRecordingCredentials,
  TEST_API,
  TEST_NOW,
  testConfig,
} from '@/auth/oidc/testing/fakeIdentityProvider';
import { nodeRandom, nodeSha256 } from '@/auth/oidc/testing/nodeCrypto';
import { createOidcPlaceholderAuth } from '@/auth/oidcPlaceholderAuth';
import type { OidcAuthProvider } from '@/auth/types';
import type { Language } from '@/i18n/languages';
import { resetRouterMock, router } from '@/test-utils/expoRouterMock';
import { renderWithProviders } from '@/test-utils/renderWithProviders';

import { SignInPanel } from '../SignInPanel';
import { UaePassConnecting } from '../UaePassConnecting';
import { useUaePassSignIn } from '../useUaePassSignIn';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));

const SIGNED_OUT = { status: 'signed_out', reason: 'none' } as const;
const SIGN_IN = 'Sign in with UAE PASS';
/** The development provider's own page, with its test accounts: a quiet link with a neutral name (D-142). */
const OTHER_OPTIONS = 'Other sign-in options';
/** No demonstration, development or staging wording on the sign-in screen (D-142). */
const LABELS = /staging|simulated|development|test account|made-up|demo/i;

/** The panel as a route shows it: with the connecting screen over everything while signing in. */
function SignInRoute() {
  const signIn = useUaePassSignIn();
  return (
    <>
      <SignInPanel signIn={signIn} />
      <UaePassConnecting visible={signIn.connecting} />
    </>
  );
}

async function oidcPanel(
  options: { language?: Language; developmentOnly?: boolean; config?: Partial<OidcConfig>; identityProviders?: unknown } = {},
) {
  const clock = { now: TEST_NOW };
  const idp = createFakeIdentityProvider({ clock: () => clock.now, developmentOnly: options.developmentOnly });
  if (options.identityProviders !== undefined) {
    idp.patchDiscovery({ azwah_identity_providers: options.identityProviders });
  }
  const auth = createOidcAuth({
    // The default build: the UAE PASS button sends idp_hint=uae_pass; EXPO_PUBLIC_OIDC_IDP_HINT is not set.
    config: testConfig({
      isDevelopmentBuild: options.developmentOnly === true,
      idpHint: 'uae_pass',
      uaePassOnly: false,
      ...options.config,
    }),
    browser: idp.browser,
    fetchImpl: idp.fetchImpl,
    random: nodeRandom,
    sha256: nodeSha256,
    credentials: createRecordingCredentials().credentials,
    clock: () => clock.now,
    apiBaseUrl: TEST_API,
  });
  await auth.restore();
  const rendered = await renderWithProviders(<SignInRoute />, { mode: 'api', auth, language: options.language });
  // The panel reads the issuer's discovery document once it is on screen.
  await waitFor(() => expect(idp.requestsTo('discovery')).toHaveLength(1));
  return { ...rendered, idp, auth };
}

const authorizeQuery = (idp: ReturnType<typeof createFakeIdentityProvider>, index = 0) =>
  new URLSearchParams(idp.opened[index]?.split('?')[1] ?? '');

beforeEach(() => resetRouterMock());

describe('SignInPanel with OpenID Connect (D-142)', () => {
  it('signs in with UAE PASS from the app: the hint goes with that request, in the app’s language', async () => {
    const user = userEvent.setup();
    const { auth, idp } = await oidcPanel({ language: 'en' });

    expect(screen.getByText('New here? This also creates your account.')).toBeOnTheScreen();
    expect(screen.queryByText(LABELS)).toBeNull();

    await user.press(screen.getByRole('button', { name: SIGN_IN }));

    await waitFor(() => expect(auth.getState()).toMatchObject({ status: 'signed_in' }));
    expect(authorizeQuery(idp).get('idp_hint')).toBe('uae_pass');
    expect(authorizeQuery(idp).get('ui_locales')).toBe('en');
  });

  it('offers no other sign-in when the provider is not a development one', async () => {
    await oidcPanel();

    expect(screen.queryByRole('button', { name: OTHER_OPTIONS })).toBeNull();
  });

  it('offers a development provider’s test accounts as a quiet, unlabelled link that sends no hint', async () => {
    const user = userEvent.setup();
    const { idp, auth } = await oidcPanel({ developmentOnly: true });

    const other = await screen.findByRole('button', { name: OTHER_OPTIONS });
    expect(screen.queryByText(LABELS)).toBeNull();
    await user.press(other);

    await waitFor(() => expect(auth.getState()).toMatchObject({ status: 'signed_in' }));
    expect(idp.opened[0]).not.toContain('idp_hint');
    expect(screen.queryByText('Connecting to UAE PASS')).toBeNull();
  });

  it('offers the other sign-in in a release build made as a demonstration build, still unlabelled (D-148)', async () => {
    Reflect.set(globalThis, '__DEV__', false);
    try {
      const user = userEvent.setup();
      const { idp, auth } = await oidcPanel({
        developmentOnly: true,
        config: { isDevelopmentBuild: false, demonstrationBuild: true },
      });

      const other = await screen.findByRole('button', { name: OTHER_OPTIONS });
      expect(screen.queryByText(LABELS)).toBeNull();
      await user.press(other);

      await waitFor(() => expect(auth.getState()).toMatchObject({ status: 'signed_in' }));
      expect(idp.opened[0]).not.toContain('idp_hint');
    } finally {
      Reflect.set(globalThis, '__DEV__', true);
    }
  });

  it('offers no other sign-in in a release build that is not a demonstration build (D-148)', async () => {
    Reflect.set(globalThis, '__DEV__', false);
    try {
      const { auth } = await oidcPanel({ developmentOnly: true, config: { isDevelopmentBuild: false, demonstrationBuild: false } });

      await expect(auth.describeIssuer()).resolves.toBeNull();
      expect(screen.queryByRole('button', { name: OTHER_OPTIONS })).toBeNull();
    } finally {
      Reflect.set(globalThis, '__DEV__', true);
    }
  });

  it('sends the hint only from the UAE PASS button, also when the other sign-in is offered', async () => {
    const user = userEvent.setup();
    const { idp, auth } = await oidcPanel({ developmentOnly: true });
    await screen.findByRole('button', { name: OTHER_OPTIONS });

    await user.press(screen.getByRole('button', { name: SIGN_IN }));

    await waitFor(() => expect(auth.getState()).toMatchObject({ status: 'signed_in' }));
    expect(authorizeQuery(idp).get('idp_hint')).toBe('uae_pass');
  });

  it('offers no other sign-in when the build sets EXPO_PUBLIC_OIDC_IDP_HINT (UAE PASS only)', async () => {
    const user = userEvent.setup();
    const { idp, auth } = await oidcPanel({ developmentOnly: true, config: { uaePassOnly: true } });

    await user.press(screen.getByRole('button', { name: SIGN_IN }));

    // By the time a sign-in has finished, the discovery answer the link waits for has long arrived.
    await waitFor(() => expect(auth.getState()).toMatchObject({ status: 'signed_in' }));
    expect(screen.queryByRole('button', { name: OTHER_OPTIONS })).toBeNull();
    expect(authorizeQuery(idp).get('idp_hint')).toBe('uae_pass');
  });

  it('never names UAE PASS when the build says none, and signs in plainly', async () => {
    const user = userEvent.setup();
    const { auth, idp } = await oidcPanel({ developmentOnly: true, config: { idpHint: null } });

    expect(screen.queryByText(/UAE PASS/)).toBeNull();
    await user.press(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(auth.getState()).toMatchObject({ status: 'signed_in' }));
    expect(idp.opened[0]).not.toContain('idp_hint');
    expect(screen.queryByRole('button', { name: OTHER_OPTIONS })).toBeNull();
  });

  it('shows the connecting screen and a busy button while UAE PASS is open', async () => {
    const user = userEvent.setup();
    const { idp } = await oidcPanel({ developmentOnly: true });
    await screen.findByRole('button', { name: OTHER_OPTIONS });
    let finish: () => void = () => undefined;
    idp.answerBrowser(() => ({ type: 'cancel' }));
    const opened = new Promise<void>((resolve) => {
      finish = resolve;
    });
    const original = idp.browser.openAuthSession;
    jest.spyOn(idp.browser, 'openAuthSession').mockImplementation(async (url) => {
      await opened;
      return original(url);
    });

    await user.press(screen.getByRole('button', { name: SIGN_IN }));

    expect(screen.getByRole('button', { name: SIGN_IN })).toBeBusy();
    expect(screen.getByRole('button', { name: OTHER_OPTIONS })).toBeDisabled();
    expect(screen.getByText('Connecting to UAE PASS')).toBeOnTheScreen();
    await act(async () => finish());
    await waitFor(() => expect(screen.queryByText('Connecting to UAE PASS')).toBeNull());
    expect(screen.getByRole('button', { name: SIGN_IN })).not.toBeBusy();
  });

  it('says nothing when the person cancels', async () => {
    const user = userEvent.setup();
    const { idp, auth } = await oidcPanel();
    idp.answerBrowser(() => ({ type: 'cancel' }));

    await user.press(screen.getByRole('button', { name: SIGN_IN }));

    await waitFor(() => expect(screen.getByRole('button', { name: SIGN_IN })).not.toBeBusy());
    expect(screen.queryByRole('alert')).toBeNull();
    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'none' });
  });

  it('says nothing when the person cancels the other sign-in either', async () => {
    const user = userEvent.setup();
    const { idp, auth } = await oidcPanel({ developmentOnly: true });
    idp.answerBrowser(() => ({ type: 'cancel' }));

    await user.press(await screen.findByRole('button', { name: OTHER_OPTIONS }));

    await waitFor(() => expect(screen.getByRole('button', { name: OTHER_OPTIONS })).not.toBeBusy());
    expect(screen.queryByRole('alert')).toBeNull();
    expect(auth.getState()).toEqual({ status: 'signed_out', reason: 'none' });
  });

  it.each([
    [
      'a reply that fails the safety checks',
      (idp: ReturnType<typeof createFakeIdentityProvider>) =>
        idp.answerBrowser((_authorize, approve) => ({ type: 'success', url: approve().replace(/state=[^&]+/, 'state=forged') })),
      /Sign-in stopped for safety/,
    ],
    [
      'an unreachable provider',
      (idp: ReturnType<typeof createFakeIdentityProvider>) => idp.fail('token', 'network'),
      /Can't reach sign-in/,
    ],
    [
      'a refusal',
      (idp: ReturnType<typeof createFakeIdentityProvider>) => idp.fail('token', { status: 400, body: { error: 'invalid_grant' } }),
      /Sign-in didn't work/,
    ],
  ])('explains %s without the provider’s text', async (_name, arrange, message) => {
    const user = userEvent.setup();
    const { idp } = await oidcPanel();
    arrange(idp);

    await user.press(screen.getByRole('button', { name: SIGN_IN }));

    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(screen.queryByText(/invalid_grant|forged/)).toBeNull();
  });

  it('explains an unexpected failure plainly', async () => {
    const user = userEvent.setup();
    const placeholder = createOidcPlaceholderAuth();
    const broken: OidcAuthProvider = {
      ...placeholder,
      kind: 'oidc',
      offersUaePass: true,
      uaePassOnly: false,
      acceptsDevelopmentIssuer: false,
      getState: () => SIGNED_OUT,
      signIn: async () => {
        throw new Error('bug');
      },
      describeIssuer: async () => null,
    };
    await renderWithProviders(<SignInRoute />, { mode: 'api', auth: broken });

    await user.press(screen.getByRole('button', { name: SIGN_IN }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/Sign-in couldn't finish/);
  });

  it('explains an expired session above the UAE PASS button', async () => {
    const { auth } = await oidcPanel();
    await act(async () => auth.signIn({ method: 'oidc' }));

    await act(async () => auth.reportUnauthorized());

    expect(await screen.findByRole('alert')).toHaveTextContent(/Session ended/);
    expect(screen.getByRole('button', { name: SIGN_IN })).toBeOnTheScreen();
  });

  it('speaks Arabic, with UAE PASS’s own button wording and translated failures', async () => {
    const user = userEvent.setup();
    const { idp } = await oidcPanel({ language: 'ar', developmentOnly: true });
    idp.fail('token', 'network');

    expect(await screen.findByRole('button', { name: 'خيارات أخرى لتسجيل الدخول' })).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'تسجيل الدخول بالهوية الرقمية' }));

    expect(await screen.findByRole('alert')).toBeOnTheScreen();
    expect(screen.queryByText(/Sign-in|sign-in/)).toBeNull();
  });
});

describe('SignInPanel with the Google sign-in (D-147)', () => {
  const GOOGLE = 'Continue with Google';

  it('offers Google next to UAE PASS when the provider lists it, and sends idp_hint=google without the UAE PASS screen', async () => {
    const user = userEvent.setup();
    const { auth, idp } = await oidcPanel({ language: 'en', identityProviders: ['uae_pass', 'google'] });

    const google = await screen.findByRole('button', { name: GOOGLE });
    expect(screen.getByRole('button', { name: SIGN_IN })).toBeOnTheScreen();
    expect(screen.queryByText(LABELS)).toBeNull();
    await user.press(google);

    await waitFor(() => expect(auth.getState()).toMatchObject({ status: 'signed_in' }));
    expect(authorizeQuery(idp).get('idp_hint')).toBe('google');
    expect(authorizeQuery(idp).get('ui_locales')).toBe('en');
    expect(screen.queryByText('Connecting to UAE PASS')).toBeNull();
  });

  it.each([
    ['lists no upstream sign-ins', undefined],
    ['lists UAE PASS only', ['uae_pass']],
    ['sends a list the app cannot read', 'google'],
  ])('offers no Google button when the provider %s', async (_name, identityProviders) => {
    const user = userEvent.setup();
    const { auth } = await oidcPanel({ identityProviders });

    // By the time a sign-in has finished, the discovery answer the button waits for has long arrived.
    await user.press(screen.getByRole('button', { name: SIGN_IN }));
    await waitFor(() => expect(auth.getState()).toMatchObject({ status: 'signed_in' }));
    expect(screen.queryByRole('button', { name: GOOGLE })).toBeNull();
  });

  it('keeps the other buttons still while Google is open', async () => {
    const user = userEvent.setup();
    const { idp } = await oidcPanel({ identityProviders: ['google'] });
    const google = await screen.findByRole('button', { name: GOOGLE });
    let finish: () => void = () => undefined;
    idp.answerBrowser(() => ({ type: 'cancel' }));
    const opened = new Promise<void>((resolve) => {
      finish = resolve;
    });
    const original = idp.browser.openAuthSession;
    jest.spyOn(idp.browser, 'openAuthSession').mockImplementation(async (url) => {
      await opened;
      return original(url);
    });

    await user.press(google);

    expect(screen.getByRole('button', { name: GOOGLE })).toBeBusy();
    expect(screen.getByRole('button', { name: SIGN_IN })).toBeDisabled();
    expect(screen.queryByText('Connecting to UAE PASS')).toBeNull();
    await act(async () => finish());
    await waitFor(() => expect(screen.getByRole('button', { name: GOOGLE })).not.toBeBusy());
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('explains a failed Google sign-in like any other', async () => {
    const user = userEvent.setup();
    const { idp } = await oidcPanel({ identityProviders: ['google'] });
    idp.fail('token', 'network');

    await user.press(await screen.findByRole('button', { name: GOOGLE }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/Can't reach sign-in/);
  });

  it('speaks Arabic', async () => {
    await oidcPanel({ language: 'ar', identityProviders: ['google'] });

    expect(await screen.findByRole('button', { name: 'المتابعة باستخدام Google' })).toBeOnTheScreen();
  });
});

describe('SignInPanel in development fixtures', () => {
  it('signs in plainly, with no simulated label and no UAE PASS claim, through the simulated account choice', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<SignInRoute />, { persona: null });

    expect(screen.queryByText(LABELS)).toBeNull();
    expect(screen.queryByText(/UAE PASS/)).toBeNull();
    await user.press(screen.getByRole('button', { name: 'Sign in' }));

    expect(router.push.mock.calls).toEqual([['/auth/uae-pass']]);
  });

  it('explains an expired session first', async () => {
    const { auth } = await renderWithProviders(<SignInRoute />);
    if (!isDevelopmentMockAuth(auth)) throw new Error('expected the development mock');

    await act(async () => auth.expireSession());

    expect(await screen.findByRole('alert')).toHaveTextContent(/Session ended/);
  });

  it('says plainly that sign-in is not available in API mode when the build names no issuer', async () => {
    await renderWithProviders(<SignInRoute />, { mode: 'api' });

    expect(screen.getByText("Sign-in isn't available")).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: SIGN_IN })).toBeNull();
    expect(screen.queryByText(/UAE PASS/)).toBeNull();
  });

  it('speaks Arabic', async () => {
    await renderWithProviders(<SignInRoute />, { persona: null, language: 'ar' });

    expect(screen.getByRole('button', { name: 'تسجيل الدخول' })).toBeOnTheScreen();
  });
});
