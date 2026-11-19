import { createHttpParticipantApi } from '@/api/participant/httpParticipantApi';
import type { ParticipantApi } from '@/api/participant/participantApi';
import { createDevelopmentMockAuth } from '@/auth/developmentMockAuth';
import type { OidcPlatform } from '@/auth/oidc/nativeOidcPlatform';
import { createOidcAuth } from '@/auth/oidc/oidcAuth';
import { resolveOidcConfig, type OidcEnvironment } from '@/auth/oidc/oidcConfig';
import { createOidcPlaceholderAuth } from '@/auth/oidcPlaceholderAuth';
import type { AuthProvider } from '@/auth/types';
import {
  apiBaseUrl as configuredApiBaseUrl,
  demonstrationBuild as configuredDemonstrationBuild,
  oidcEnvironment,
  type AppMode,
} from '@/config';
import { createDraftStore } from '@/drafts/selectDraftStore';
import type { DraftStore } from '@/drafts/types';
import { createChatStore, type ChatStore } from '@/features/assistant/chatStore';
import {
  createWebSocketConsultantConnector,
  type ConsultantConnector,
} from '@/features/consultant/consultantConnector';
import { createScoreReportStore, type ScoreReportStore } from '@/features/score-report/reportStore';
import type { AppActivity } from '@/features/sessions/assessmentEngine';
import type { Connectivity } from '@/network/connectivity';
import type { FieldCipher } from '@/storage/protection/fieldCipher';
import type { SettingsRepository } from '@/storage/settings';
import type { SqlDatabase } from '@/storage/sql';

export interface AppServices {
  mode: AppMode;
  auth: AuthProvider;
  api: ParticipantApi;
  drafts: DraftStore;
  /** The Azwah assistant conversation, kept on the phone only (D-108). */
  chat: ChatStore;
  /** Written score reports, kept on the phone only (D-112). */
  reports: ScoreReportStore;
  /** Opens a live conversation with the Azwah consultant (D-109). */
  consultant: ConsultantConnector;
  connectivity: Connectivity;
  activity?: AppActivity;
}

export interface ServiceDependencies {
  db: SqlDatabase;
  settings: SettingsRepository;
  /** The cipher for answers kept on the phone, or null to keep them in memory (decision M-35). */
  protection: FieldCipher | null;
  connectivity: Connectivity;
  activity?: AppActivity;
  /** The phone's sign-in parts (system browser, keystore, crypto); made only when OpenID Connect is configured. */
  oidcPlatform?: () => OidcPlatform;
  /**
   * This build's settings unless given (tests): EXPO_PUBLIC_OIDC_*, EXPO_PUBLIC_API_URL, __DEV__,
   * EXPO_PUBLIC_DEMONSTRATION_BUILD and Date.now.
   */
  oidcEnvironment?: OidcEnvironment;
  apiBaseUrl?: string;
  isDevelopmentBuild?: boolean;
  /** A demonstration build (D-148): accepts a development-only issuer, never plain http. */
  demonstrationBuild?: boolean;
  clock?: () => number;
}

/**
 * API mode signs in with OpenID Connect when the build names an issuer and a client (resolveOidcConfig), and
 * otherwise honestly says sign-in is not available. A build without the native modules says the same.
 */
function createApiAuth(dependencies: ServiceDependencies, baseUrl: string | undefined): AuthProvider {
  const config = resolveOidcConfig(
    dependencies.oidcEnvironment ?? oidcEnvironment,
    dependencies.isDevelopmentBuild ?? __DEV__,
    dependencies.demonstrationBuild ?? configuredDemonstrationBuild,
  );
  if (!config || !dependencies.oidcPlatform) {
    return createOidcPlaceholderAuth();
  }
  let platform: OidcPlatform;
  try {
    platform = dependencies.oidcPlatform();
  } catch {
    return createOidcPlaceholderAuth();
  }
  return createOidcAuth({ config, ...platform, clock: dependencies.clock ?? Date.now, apiBaseUrl: baseUrl });
}

/**
 * Wires the app for its mode. Development fixtures: the simulated sign-in and the in-memory fixture API. API: the
 * OpenID Connect provider (or the "not configured" placeholder) and the real API. In both, drafts are encrypted on
 * the phone when the keystore works and kept in memory otherwise.
 *
 * The fixture modules (synthetic people, draft questionnaire content, simulated scoring) are loaded here and only
 * here, behind the compile-time flag: Metro drops the branch from release bundles, where `resolveAppMode` never
 * yields fixtures mode anyway, so a participant build carries none of it (D-99).
 */
export function createServices(mode: AppMode, dependencies: ServiceDependencies): AppServices {
  const { db, settings, protection, connectivity, activity } = dependencies;
  const drafts = createDraftStore(db, protection);
  const chat = createChatStore(db, protection);
  const reports = createScoreReportStore(db, protection);
  if (__DEV__ && mode === 'development_fixtures') {
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- development-only module, loaded only here (above)
    const { createFixtureParticipantApi, FIXTURE_WORLDS_KEY_NAME } = require('@/dev-fixtures/fixtureParticipantApi') as typeof import('@/dev-fixtures/fixtureParticipantApi');
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- as above
    const { DEV_PERSONAS } = require('@/dev-fixtures/personas') as typeof import('@/dev-fixtures/personas');
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- as above
    const { createSimulatedConsultantConnector } = require('@/dev-fixtures/simulatedConsultant') as typeof import('@/dev-fixtures/simulatedConsultant');
    const auth = createDevelopmentMockAuth({ settings, personas: DEV_PERSONAS });
    const api = createFixtureParticipantApi({
      getSubject: () => {
        const state = auth.getState();
        return state.status === 'signed_in' ? state.session.subject : null;
      },
      // Kept in settings, so the reload that applies a language does not wipe a demonstration; sealed like the
      // answers, under a key of their own that sign-out keeps. With no keystore (drafts in memory) nothing is kept.
      settings,
      cipher: protection?.withKey(FIXTURE_WORLDS_KEY_NAME) ?? null,
    });
    const consultant = createSimulatedConsultantConnector();
    return { mode, auth, api, drafts, chat, reports, consultant, connectivity, activity };
  }
  const baseUrl = dependencies.apiBaseUrl ?? configuredApiBaseUrl;
  const auth = createApiAuth(dependencies, baseUrl);
  return {
    mode,
    auth,
    api: createHttpParticipantApi({ baseUrl, auth }),
    drafts,
    chat,
    reports,
    consultant: createWebSocketConsultantConnector({ baseUrl, auth }),
    connectivity,
    activity,
  };
}
