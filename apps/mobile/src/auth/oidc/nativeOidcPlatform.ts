import type * as ExpoCrypto from 'expo-crypto';
import type * as ExpoLinking from 'expo-linking';
import type * as ExpoWebBrowser from 'expo-web-browser';

import type { OidcAuthDependencies } from './oidcAuth';
import { createExpoCredentialStore } from './secureCredentialStore';

/** What sign-in needs from the phone. Tests pass fakes. */
export type OidcPlatform = Pick<OidcAuthDependencies, 'browser' | 'fetchImpl' | 'random' | 'sha256' | 'credentials'>;

/** The deep links the identity provider sends the browser back to (app/+native-intent.tsx keeps the router off them). */
export const AUTH_CALLBACK_PATH = 'auth/callback';
export const AUTH_SIGNED_OUT_PATH = 'auth/signed-out';

/**
 * expo-web-browser, expo-crypto, expo-secure-store and expo-linking. The native modules are required here, when sign-in
 * is configured, not imported: a build without one then keeps the "sign-in isn't available" state instead of failing
 * at start-up (services.ts).
 */
export function createNativeOidcPlatform(): OidcPlatform {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- loaded lazily on purpose (see above)
  const webBrowser = require('expo-web-browser') as typeof ExpoWebBrowser;
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- as above
  const linking = require('expo-linking') as typeof ExpoLinking;
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- as above
  const crypto = require('expo-crypto') as typeof ExpoCrypto;
  // azwah://auth/callback in a development or release build; exp://<host>:<port>/--/auth/callback in Expo Go.
  const redirectUri = linking.createURL(AUTH_CALLBACK_PATH);
  const signedOutUri = linking.createURL(AUTH_SIGNED_OUT_PATH);

  return {
    browser: {
      redirectUri,
      signedOutUri,
      async openAuthSession(url, returnUri = redirectUri) {
        // ASWebAuthenticationSession on iOS, without sharing Safari's cookies; Custom Tabs on Android.
        const result = await webBrowser.openAuthSessionAsync(url, returnUri, { preferEphemeralSession: true });
        return result.type === 'success' ? { type: 'success', url: result.url } : { type: 'cancel' };
      },
    },
    fetchImpl: (input, init) => fetch(input, init),
    random: (byteCount) => crypto.getRandomBytes(byteCount),
    sha256: async (data) =>
      new Uint8Array(await crypto.digest(crypto.CryptoDigestAlgorithm.SHA256, new Uint8Array(data))),
    credentials: createExpoCredentialStore(),
  };
}
