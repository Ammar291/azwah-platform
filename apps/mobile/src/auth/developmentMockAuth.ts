import type { SettingsRepository } from '@/storage/settings';

import type { IdentityProfile } from './identityProfile';
import { createAuthStateStore } from './observable';
import type { AuthProvider, AuthSession } from './types';

export interface DevelopmentPersona {
  code: string;
  subject: string;
  /** Made-up details the simulated UAE PASS releases, labelled `simulated`. */
  profile?: IdentityProfile;
}

export interface DevelopmentMockAuth extends AuthProvider {
  readonly kind: 'development_mock';
  readonly personas: readonly DevelopmentPersona[];
  /** Ends the session as if the API had answered 401, to exercise the expired-session path. */
  expireSession(): void;
  /**
   * Signs out because the demonstration data was reset (Settings, development fixtures only). An ordinary sign-out, so
   * local drafts and cached answers go the usual way; `demonstrationReset` then lets the sign-in screen say so.
   */
  signOutAfterDemonstrationReset(): Promise<void>;
  /** True from a demonstration reset until someone signs in again. In memory only. */
  readonly demonstrationReset: boolean;
}

/**
 * SIMULATED sign-in for development fixtures mode. No identity is verified and no token exists: it only chooses
 * which synthetic persona the fixture API serves. It remembers the persona code (not a credential) so the reload
 * after a language change keeps the developer signed in.
 */
export function createDevelopmentMockAuth({
  settings,
  personas,
}: {
  settings: Pick<SettingsRepository, 'get' | 'set' | 'remove'>;
  personas: readonly DevelopmentPersona[];
}): DevelopmentMockAuth {
  const store = createAuthStateStore({ status: 'initializing' });
  const sessionFor = (persona: DevelopmentPersona): AuthSession => ({
    subject: persona.subject,
    personaCode: persona.code,
    profile: persona.profile ?? null,
  });

  const endSession = () => {
    if (store.get().status !== 'signed_in') {
      return;
    }
    void settings.remove('dev_mock_persona');
    store.set({ status: 'signed_out', reason: 'session_expired' });
  };

  let demonstrationReset = false;

  const signOut = async () => {
    await settings.remove('dev_mock_persona');
    store.set({ status: 'signed_out', reason: 'signed_out' });
  };

  return {
    kind: 'development_mock',
    personas,
    getState: store.get,
    subscribe: store.subscribe,

    async restore() {
      const code = await settings.get('dev_mock_persona');
      const persona = personas.find((candidate) => candidate.code === code);
      store.set(persona ? { status: 'signed_in', session: sessionFor(persona) } : { status: 'signed_out', reason: 'none' });
    },

    async signIn(request) {
      if (request.method !== 'development_persona') {
        throw new Error('The development mock signs in with a development persona only.');
      }
      const persona = personas.find((candidate) => candidate.code === request.personaCode);
      if (!persona) {
        throw new Error(`Unknown development persona: ${request.personaCode}`);
      }
      await settings.set('dev_mock_persona', persona.code);
      demonstrationReset = false;
      store.set({ status: 'signed_in', session: sessionFor(persona) });
    },

    signOut,

    async signOutAfterDemonstrationReset() {
      // Before the state changes: the sign-in screen reads it when it appears.
      demonstrationReset = true;
      await signOut();
    },

    get demonstrationReset() {
      return demonstrationReset;
    },

    async getAccessToken() {
      return null;
    },

    reportUnauthorized: endSession,
    expireSession: endSession,
  };
}

export function isDevelopmentMockAuth(provider: AuthProvider): provider is DevelopmentMockAuth {
  return provider.kind === 'development_mock';
}
