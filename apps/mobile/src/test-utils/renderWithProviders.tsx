import { notifyManager, QueryClient } from '@tanstack/react-query';
import { render, waitFor } from '@testing-library/react-native';
import type { ReactElement, ReactNode } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import type { ParticipantApi } from '@/api/participant/participantApi';
import { AppProviders } from '@/app-shell/AppProviders';
import { createDevelopmentMockAuth } from '@/auth/developmentMockAuth';
import { createOidcPlaceholderAuth } from '@/auth/oidcPlaceholderAuth';
import type { AuthProvider } from '@/auth/types';
import type { AppMode } from '@/config';
import { createFixtureParticipantApi } from '@/dev-fixtures/fixtureParticipantApi';
import { DEV_PERSONAS, type DevPersonaCode } from '@/dev-fixtures/personas';
import { createMemoryDraftStore } from '@/drafts/memoryDraftStore';
import type { DraftStore } from '@/drafts/types';
import { createMemoryChatStore, type ChatStore } from '@/features/assistant/chatStore';
import type { ConsultantConnector } from '@/features/consultant/consultantConnector';
import { createMemoryScoreReportStore, type ScoreReportStore } from '@/features/score-report/reportStore';
import { createAppAssessmentEngine, type AssessmentEngineDependencies } from '@/features/sessions/assessmentEngine';
import { createI18n } from '@/i18n/i18n';
import type { Language } from '@/i18n/languages';
import { createSettingsRepository } from '@/storage/settings';
import { FakeSqlDatabase } from '@/storage/testing/FakeSqlDatabase';

import { createFakeConnectivity, type FakeConnectivity } from './fakeConnectivity';
import { PLACEHOLDER_CATALOG } from './placeholderContent';

export interface RenderOptions {
  language?: Language;
  mode?: AppMode;
  /** Signed in as this fixture persona; null for signed out. Ignored in API mode. */
  persona?: DevPersonaCode | null;
  /** API mode only: this provider (for example the OpenID Connect one) instead of the "not configured" placeholder. */
  auth?: AuthProvider;
  api?: ParticipantApi;
  drafts?: DraftStore;
  chat?: ChatStore;
  reports?: ScoreReportStore;
  /** Default: a connector that never connects, for screens that do not start a call. */
  consultant?: ConsultantConnector;
  /** Online unless a test passes its own and switches it. */
  connectivity?: FakeConnectivity;
  /** Timing of saves and retries, for tests that exercise backoff. */
  engine?: Pick<AssessmentEngineDependencies, 'delayMs' | 'retry' | 'random' | 'now'>;
}

let latestQueryClient: QueryClient | null = null;

/**
 * Waits until the last rendered tree has no query or mutation in flight, so refetches started by a change (for
 * example after saving) land inside the test, not during the next interaction.
 */
export async function queriesIdle(): Promise<void> {
  const client = latestQueryClient;
  if (client) {
    await waitFor(() => expect(client.isFetching() + client.isMutating()).toBe(0));
  }
}

/** `queriesIdle` for `afterEach`: background refetches finish before the test ends. */
export async function settleQueries(): Promise<void> {
  await queriesIdle();
  latestQueryClient = null;
}

// TanStack Query delivers cache notifications on a later timer tick by default, so in tests one could land between
// steps, outside act. As microtasks they arrive within the same wait as the change that caused them.
notifyManager.setScheduler((callback) => queueMicrotask(callback));

/**
 * Tests have no native view to measure, so the safe-area provider needs starting metrics or it renders nothing.
 * Insets are physical screen edges by definition (the library's own shape), not layout styles.
 */
const METRICS = {
  frame: { x: 0, y: 0, width: 390, height: 844 },
  // eslint-disable-next-line no-restricted-syntax -- physical insets required by react-native-safe-area-context
  insets: { top: 0, bottom: 0, left: 0, right: 0 },
};

/**
 * Every provider the app uses, backed by the fixture API (serving the test placeholder) with no artificial delay;
 * `wrap` puts a tree inside them. renderWithProviders uses it; tests that render the real router
 * (expo-router/testing-library) wrap their root layout's content with it.
 */
export async function createTestProviders(options: RenderOptions = {}) {
  const { language = 'en', mode = 'development_fixtures', persona = 'household_lead' } = options;
  const i18n = await createI18n(language);
  const settings = createSettingsRepository(new FakeSqlDatabase());

  let auth: AuthProvider;
  if (mode === 'development_fixtures') {
    const mock = createDevelopmentMockAuth({ settings, personas: DEV_PERSONAS });
    await mock.restore();
    if (persona) {
      await mock.signIn({ method: 'development_persona', personaCode: persona });
    }
    auth = mock;
  } else {
    auth = options.auth ?? createOidcPlaceholderAuth();
  }

  const api =
    options.api ??
    createFixtureParticipantApi({
      getSubject: () => {
        const state = auth.getState();
        return state.status === 'signed_in' ? state.session.subject : null;
      },
      latencyMs: 0,
      catalog: PLACEHOLDER_CATALOG,
    });
  const drafts = options.drafts ?? createMemoryDraftStore();
  const chat = options.chat ?? createMemoryChatStore();
  const reports = options.reports ?? createMemoryScoreReportStore();
  const consultant: ConsultantConnector =
    options.consultant ?? (() => Promise.reject(new Error('No consultant in this test.')));
  const connectivity = options.connectivity ?? createFakeConnectivity();
  // No retries, and no garbage-collection timers left running after a test.
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false, gcTime: Infinity } },
  });
  latestQueryClient = queryClient;
  const services = { mode, auth, api, drafts, chat, reports, consultant, connectivity };
  // Running from the start: the tests sign in before rendering and have no root layout to resume it.
  const engine = createAppAssessmentEngine(services, queryClient, {
    delayMs: 50,
    retry: { baseMs: 50, maxMs: 200, maxAttempts: 3 },
    random: () => 0.5,
    ...options.engine,
  });
  const changeLanguage = jest.fn(async (_next: Language) => undefined);

  const wrap = (children: ReactNode) => (
    <SafeAreaProvider initialMetrics={METRICS}>
      <AppProviders
        services={services}
        engine={engine}
        i18n={i18n}
        language={{ language, changeLanguage }}
        queryClient={queryClient}
      >
        {children}
      </AppProviders>
    </SafeAreaProvider>
  );

  return { wrap, auth, api, drafts, chat, queryClient, changeLanguage, i18n, engine, connectivity, settings };
}

/** Renders with every provider the app uses, backed by the fixture API with no artificial delay. */
export async function renderWithProviders(ui: ReactElement, options: RenderOptions = {}) {
  const { wrap, ...handles } = await createTestProviders(options);
  const result = await render(wrap(ui));
  return { ...result, ...handles };
}
