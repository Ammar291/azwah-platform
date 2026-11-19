import { isDevelopmentMockAuth } from '@/auth/developmentMockAuth';
import { createFakeIdentityProvider, createRecordingCredentials, TEST_API, TEST_CLIENT_ID, TEST_ISSUER, TEST_NOW } from '@/auth/oidc/testing/fakeIdentityProvider';
import { nodeRandom, nodeSha256 } from '@/auth/oidc/testing/nodeCrypto';
import { isOidcAuthProvider } from '@/auth/types';
import { migrate } from '@/storage/migrations';
import { createFieldCipher } from '@/storage/protection/fieldCipher';
import { createSettingsRepository } from '@/storage/settings';
import type { SqlDatabase, SqlValue } from '@/storage/sql';
import { createMemorySecretStore } from '@/storage/testing/memorySecretStore';
import { createNodeAesGcm } from '@/storage/testing/nodeAesGcm';
import { NodeSqliteDatabase } from '@/storage/testing/NodeSqliteDatabase';
import { createFakeConnectivity } from '@/test-utils/fakeConnectivity';

import { createServices, type ServiceDependencies } from '../services';

// The development fixtures (synthetic people, draft questionnaire content, simulated scoring) must be unreachable from
// the real-API wiring, not merely unused: a release bundle carries whatever the production path imports. These
// factories throw the moment either module is loaded.
jest.mock('@/dev-fixtures/fixtureParticipantApi', () => {
  throw new Error('fixture API loaded while wiring the real API');
});
jest.mock('@/dev-fixtures/personas', () => {
  throw new Error('fixture personas loaded while wiring the real API');
});

/** Real SQLite that remembers every statement and parameter it was given. */
class RecordingDatabase implements SqlDatabase {
  readonly seen: string[] = [];

  constructor(private readonly inner: SqlDatabase) {}

  private note(source: string, params: SqlValue[]) {
    this.seen.push(JSON.stringify([source, ...params]));
  }

  async execAsync(source: string) {
    this.note(source, []);
    return this.inner.execAsync(source);
  }

  async runAsync(source: string, ...params: SqlValue[]) {
    this.note(source, params);
    return this.inner.runAsync(source, ...params);
  }

  async getFirstAsync<T>(source: string, ...params: SqlValue[]) {
    this.note(source, params);
    return this.inner.getFirstAsync<T>(source, ...params);
  }

  async getAllAsync<T>(source: string, ...params: SqlValue[]) {
    this.note(source, params);
    return this.inner.getAllAsync<T>(source, ...params);
  }

  withTransactionAsync(task: () => Promise<void>) {
    return this.inner.withTransactionAsync(task);
  }
}

const environment = { EXPO_PUBLIC_OIDC_ISSUER: TEST_ISSUER, EXPO_PUBLIC_OIDC_CLIENT_ID: TEST_CLIENT_ID };

async function phone(overrides: Partial<ServiceDependencies> = {}) {
  const clock = { now: TEST_NOW };
  const idp = createFakeIdentityProvider({ clock: () => clock.now });
  const recording = createRecordingCredentials();
  const sqlite = new NodeSqliteDatabase();
  await migrate(sqlite);
  const db = new RecordingDatabase(sqlite);
  const settings = createSettingsRepository(db);
  const settingsWrites = jest.spyOn(settings, 'set');
  // The participant API uses the global fetch; here it reaches the same fake as sign-in, never the network.
  jest.spyOn(globalThis, 'fetch').mockImplementation((input, init) => idp.fetchImpl(String(input), init));
  const services = createServices('api', {
    db,
    settings,
    protection: createFieldCipher({ aes: createNodeAesGcm(), secrets: createMemorySecretStore() }),
    connectivity: createFakeConnectivity(),
    oidcPlatform: () => ({
      browser: idp.browser,
      fetchImpl: idp.fetchImpl,
      random: nodeRandom,
      sha256: nodeSha256,
      credentials: recording.credentials,
    }),
    oidcEnvironment: environment,
    apiBaseUrl: TEST_API,
    isDevelopmentBuild: false,
    clock: () => clock.now,
    ...overrides,
  });
  return { clock, idp, recording, db, settingsWrites, services };
}

afterEach(() => {
  jest.restoreAllMocks();
});

describe('services in API mode', () => {
  it('signs in with OpenID Connect when the build names an issuer and a client', async () => {
    const { services } = await phone();

    expect(isOidcAuthProvider(services.auth)).toBe(true);
    expect(isDevelopmentMockAuth(services.auth)).toBe(false);
  });

  it.each([
    ['no issuer or client', { oidcEnvironment: {} }],
    ['an http issuer in a release build', { oidcEnvironment: { ...environment, EXPO_PUBLIC_OIDC_ISSUER: 'http://localhost:5090' } }],
    ['no sign-in modules', { oidcPlatform: undefined }],
    [
      'sign-in modules that fail to load',
      {
        oidcPlatform: () => {
          throw new Error("Cannot find native module 'ExpoWebBrowser'");
        },
      },
    ],
  ])('honestly says sign-in is not configured with %s', async (_name, overrides: Partial<ServiceDependencies>) => {
    const { services } = await phone(overrides);
    await services.auth.restore();

    expect(services.auth.getState()).toEqual({ status: 'unavailable', reason: 'not_configured' });
  });

  it('accepts a development-only issuer in a release build only when it is a demonstration build (D-148)', async () => {
    const plain = await phone();
    plain.idp.patchDiscovery({ azwah_development_only: true });
    const demonstration = await phone({ demonstrationBuild: true });
    demonstration.idp.patchDiscovery({ azwah_development_only: true });

    if (!isOidcAuthProvider(plain.services.auth) || !isOidcAuthProvider(demonstration.services.auth)) {
      throw new Error('expected OpenID Connect sign-in');
    }
    await expect(plain.services.auth.describeIssuer()).resolves.toBeNull();
    await expect(demonstration.services.auth.describeIssuer()).resolves.toMatchObject({ developmentOnly: true });
  });

  it('still refuses an http issuer in a demonstration release build (D-148)', async () => {
    const { services } = await phone({
      demonstrationBuild: true,
      oidcEnvironment: { ...environment, EXPO_PUBLIC_OIDC_ISSUER: 'http://localhost:5090' },
    });
    await services.auth.restore();

    expect(services.auth.getState()).toEqual({ status: 'unavailable', reason: 'not_configured' });
  });

  it('never lets a token reach SQLite or the settings, and gives the keystore only refresh tokens and the subject', async () => {
    const { clock, idp, recording, db, settingsWrites, services } = await phone();
    const { auth, api, drafts } = services;

    await auth.restore();
    await auth.signIn({ method: 'oidc' });
    await drafts.saveAnswer({ sessionId: 's-1', itemId: 'i-1', optionId: 'o-1', baseModifiedAt: null, at: new Date(TEST_NOW) });
    await api.getMe().catch(() => undefined);
    clock.now += 11 * 60_000;
    await api.getMe().catch(() => undefined);
    await auth.signOut();

    // The API saw the bearer tokens; nothing stored them.
    const bearer = idp.requests.filter((request) => request.url === `${TEST_API}/api/v1/me`);
    expect(bearer.map((request) => request.headers.Authorization)).toEqual([
      `Bearer ${idp.issued[1] ?? ''}`,
      `Bearer ${idp.issued.at(-3) ?? ''}`,
    ]);
    expect(db.seen.length).toBeGreaterThan(0);
    for (const secret of idp.issued) {
      expect(db.seen.filter((entry) => entry.includes(secret))).toEqual([]);
      expect(settingsWrites.mock.calls.filter((call) => call.join(' ').includes(secret))).toEqual([]);
    }
    const refreshTokens = idp.issued.filter((value) => value.startsWith('refresh-'));
    expect(recording.writes.every(([name, value]) => (name === 'subject' ? value === 'dev-participant-lead' : refreshTokens.includes(value)))).toBe(true);
    expect(recording.writes.map(([name]) => name)).toEqual(['refresh_token', 'subject', 'refresh_token']);
    for (const secret of idp.issued.filter((value) => !value.startsWith('refresh-'))) {
      expect(JSON.stringify(recording.writes)).not.toContain(secret);
    }
  });

  it('never loads the development fixtures when wiring the real API', async () => {
    const { services } = await phone();

    expect(services.mode).toBe('api');
    expect(services.auth.getState().status).not.toBe('signed_in');
  });
});
