import { useRouter } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';

import { useAuth } from '@/auth/AuthContext';
import { isDevelopmentMockAuth } from '@/auth/developmentMockAuth';
import { isOidcAuthProvider, isSignInError, type IssuerDescription, type SignInErrorKind } from '@/auth/types';
import { useLanguage } from '@/i18n/LanguageContext';

/** What the sign-in screen says when sign-in did not finish; cancelling says nothing. */
export type SignInFailureShown = Exclude<SignInErrorKind, 'cancelled'> | 'unexpected';

export interface UaePassSignIn {
  /** Sign-in is possible in this build (OpenID Connect configured, or the development fixtures). */
  available: boolean;
  /** The development fixtures' simulated UAE PASS stands in for the real one. */
  simulated: boolean;
  /**
   * The screen shows the UAE PASS button: an issuer the app sends to UAE PASS (`offersUaePass`). Otherwise, and for
   * the development fixtures' simulated sign-in, which nothing labels as simulated any more (D-142), it offers a plain
   * "Sign in" and never claims UAE PASS (CONTRIBUTING.md rule 8).
   */
  uaePass: boolean;
  /**
   * The screen also shows "Continue with Google": the issuer's discovery lists the Google sign-in (D-147). Never in the
   * development fixtures, whose sign-in is simulated, and never for an issuer that does not list it (rule 8).
   */
  google: boolean;
  /**
   * The provider's test accounts can be chosen too: a development build (or a demonstration build, D-148) against a
   * development provider, unless the build sends every sign-in to UAE PASS (EXPO_PUBLIC_OIDC_IDP_HINT set).
   */
  testAccounts: boolean;
  /** A sign-in is running (the browser is open or tokens are being exchanged). */
  busy: boolean;
  /** A UAE PASS sign-in is running: the "Connecting to UAE PASS" screen shows. */
  connecting: boolean;
  /** A Google sign-in is running: its button shows the spinner. */
  connectingGoogle: boolean;
  failure: SignInFailureShown | null;
  /** The UAE PASS button: straight to UAE PASS (the hint goes with this request only), or the simulated one. */
  start(): void;
  /** The Google button: straight to Google (`idp_hint=google`). */
  startGoogle(): void;
  /** The provider's own page with its test accounts, without the hint. */
  startTestAccounts(): void;
}

type Running = 'uae_pass' | 'google' | 'other';

/**
 * The sign-in buttons' behaviour (D-142, D-147). With OpenID Connect the UAE PASS button opens the issuer's sign-in in
 * the app's language with `idp_hint`, so the browser opens on UAE PASS itself (and the connecting screen shows
 * meanwhile); the Google button does the same with `idp_hint=google` when the issuer offers it; the test accounts open
 * the issuer's own page, without a hint. In development fixtures the plain "Sign in" opens the simulated account choice
 * instead.
 */
export function useUaePassSignIn(): UaePassSignIn {
  const { state, provider } = useAuth();
  const { language } = useLanguage();
  const router = useRouter();
  const issuer = useIssuerDescription();
  const [running, setRunning] = useState<Running | null>(null);
  const [failure, setFailure] = useState<SignInFailureShown | null>(null);
  const mounted = useRef(true);
  useEffect(
    () => () => {
      mounted.current = false;
    },
    [],
  );

  const simulated = isDevelopmentMockAuth(provider);
  const oidc = isOidcAuthProvider(provider) ? provider : null;
  const available = simulated || (oidc !== null && state.status !== 'unavailable');
  const uaePass = oidc?.offersUaePass === true;
  const google = oidc !== null && available && issuer?.offersGoogle === true;
  const testAccounts =
    oidc !== null &&
    oidc.acceptsDevelopmentIssuer &&
    issuer?.developmentOnly === true &&
    oidc.offersUaePass &&
    !oidc.uaePassOnly &&
    available;

  const run = useCallback(
    async (identityProvider: 'uae_pass' | 'google' | undefined) => {
      if (!oidc) return;
      setRunning(identityProvider ?? 'other');
      setFailure(null);
      try {
        await oidc.signIn({ method: 'oidc', identityProvider, language });
      } catch (error) {
        const kind = isSignInError(error) ? error.kind : 'unexpected';
        if (mounted.current) setFailure(kind === 'cancelled' ? null : kind);
      } finally {
        if (mounted.current) setRunning(null);
      }
    },
    [language, oidc],
  );

  const start = useCallback(() => {
    if (simulated) {
      router.push('/auth/uae-pass');
      return;
    }
    void run(oidc?.offersUaePass ? 'uae_pass' : undefined);
  }, [oidc, router, run, simulated]);

  const startGoogle = useCallback(() => void run('google'), [run]);

  const startTestAccounts = useCallback(() => void run(undefined), [run]);

  return {
    available,
    simulated,
    uaePass,
    google,
    testAccounts,
    busy: running !== null,
    connecting: running === 'uae_pass',
    connectingGoogle: running === 'google',
    failure,
    start,
    startGoogle,
    startTestAccounts,
  };
}

/**
 * What the configured issuer says about itself before anyone signs in: whether it is development-only (D-82; only
 * development and demonstration builds accept one, D-148) and whether it offers the Google sign-in (D-147). Null until
 * read, or when it cannot be.
 */
function useIssuerDescription(): IssuerDescription | null {
  const { provider } = useAuth();
  const [description, setDescription] = useState<IssuerDescription | null>(null);
  useEffect(() => {
    if (!isOidcAuthProvider(provider)) return;
    let current = true;
    void provider.describeIssuer().then((issuer) => {
      if (current) setDescription(issuer);
    });
    return () => {
      current = false;
    };
  }, [provider]);
  return description;
}
