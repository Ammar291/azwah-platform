import { createAuthStateStore } from './observable';
import { SignInUnavailableError, type AuthProvider } from './types';

/**
 * Used in API mode when this build has no OpenID Connect settings (EXPO_PUBLIC_OIDC_ISSUER and _CLIENT_ID, see
 * src/auth/oidc) or cannot load the sign-in modules: it honestly reports that sign-in is not configured. UAE PASS
 * is not configured either; nothing here pretends otherwise.
 */
export function createOidcPlaceholderAuth(): AuthProvider {
  const store = createAuthStateStore({ status: 'unavailable', reason: 'not_configured' });
  return {
    kind: 'oidc',
    getState: store.get,
    subscribe: store.subscribe,
    async restore() {},
    async signIn() {
      throw new SignInUnavailableError();
    },
    async signOut() {},
    async getAccessToken() {
      return null;
    },
    reportUnauthorized() {},
  };
}
