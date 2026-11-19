import type { FetchLike } from '@/api/client';

import { SignInError } from '../../types';
import { discoverProvider, discoveryUrl } from '../discovery';
import { TEST_ISSUER, testConfig, testResponse } from '../testing/fakeIdentityProvider';

const document = (patch: Record<string, unknown> = {}) => ({
  issuer: TEST_ISSUER,
  authorization_endpoint: `${TEST_ISSUER}/authorize`,
  token_endpoint: `${TEST_ISSUER}/token`,
  revocation_endpoint: `${TEST_ISSUER}/revoke`,
  end_session_endpoint: `${TEST_ISSUER}/end-session`,
  response_types_supported: ['code'],
  code_challenge_methods_supported: ['S256'],
  authorization_response_iss_parameter_supported: true,
  ...patch,
});

const serving =
  (status: number, body: unknown): FetchLike =>
  async () =>
    testResponse(status, body);

async function failure(promise: Promise<unknown>): Promise<string> {
  const error = await promise.then(
    () => null,
    (caught: unknown) => caught,
  );
  if (!(error instanceof SignInError)) throw new Error(`expected a SignInError, got ${String(error)}`);
  return error.kind;
}

describe('discovery', () => {
  it('reads the well-known document of the issuer', async () => {
    const fetchImpl = jest.fn<ReturnType<FetchLike>, Parameters<FetchLike>>(serving(200, document()));

    await expect(discoverProvider(testConfig(), fetchImpl)).resolves.toEqual({
      issuer: TEST_ISSUER,
      authorizationEndpoint: `${TEST_ISSUER}/authorize`,
      tokenEndpoint: `${TEST_ISSUER}/token`,
      revocationEndpoint: `${TEST_ISSUER}/revoke`,
      endSessionEndpoint: `${TEST_ISSUER}/end-session`,
      issParameterSupported: true,
      developmentOnly: false,
      offersGoogle: false,
    });
    expect(fetchImpl.mock.calls[0]?.[0]).toBe(`${TEST_ISSUER}/.well-known/openid-configuration`);
  });

  it('reads whether the issuer offers Google, and never refuses an issuer for a list it cannot read (D-147)', async () => {
    const read = (listed: unknown) =>
      discoverProvider(testConfig(), serving(200, document({ azwah_identity_providers: listed }))).then((metadata) => metadata.offersGoogle);

    await expect(read(['uae_pass', 'google'])).resolves.toBe(true);
    await expect(read(['uae_pass'])).resolves.toBe(false);
    await expect(read('google')).resolves.toBe(false);
    await expect(read({ google: true })).resolves.toBe(false);
  });

  it('builds the well-known address without doubling a trailing slash', () => {
    expect(discoveryUrl('https://id.example.test/')).toBe('https://id.example.test/.well-known/openid-configuration');
    expect(discoveryUrl('https://id.example.test/tenant')).toBe('https://id.example.test/tenant/.well-known/openid-configuration');
  });

  it('refuses a document for another issuer, even one differing only by a slash', async () => {
    await expect(failure(discoverProvider(testConfig(), serving(200, document({ issuer: 'https://evil.example.test' }))))).resolves.toBe(
      'unsafe_response',
    );
    await expect(failure(discoverProvider(testConfig(), serving(200, document({ issuer: `${TEST_ISSUER}/` }))))).resolves.toBe(
      'unsafe_response',
    );
  });

  it('refuses http endpoints in a release build and accepts them in a development build', async () => {
    const http = { issuer: 'http://localhost:5090', authorization_endpoint: 'http://localhost:5090/authorize', token_endpoint: 'http://localhost:5090/token', revocation_endpoint: 'http://localhost:5090/revoke' };

    await expect(
      failure(discoverProvider(testConfig({ issuer: 'http://localhost:5090' }), serving(200, document(http)))),
    ).resolves.toBe('unsafe_response');
    await expect(
      discoverProvider(testConfig({ issuer: 'http://localhost:5090', isDevelopmentBuild: true }), serving(200, document(http))),
    ).resolves.toMatchObject({ tokenEndpoint: 'http://localhost:5090/token' });
  });

  it('refuses an https issuer that sends the code to an http token endpoint', async () => {
    await expect(failure(discoverProvider(testConfig(), serving(200, document({ token_endpoint: 'http://id.example.test/token' }))))).resolves.toBe(
      'unsafe_response',
    );
  });

  it('refuses a development-only issuer in a release build and reports it in a development build', async () => {
    const devOnly = serving(200, document({ azwah_development_only: true }));

    await expect(failure(discoverProvider(testConfig({ isDevelopmentBuild: false }), devOnly))).resolves.toBe('unsafe_response');
    await expect(discoverProvider(testConfig({ isDevelopmentBuild: true }), devOnly)).resolves.toMatchObject({
      developmentOnly: true,
    });
  });

  it('accepts a development-only issuer in a release build only when it is a demonstration build (D-148)', async () => {
    const devOnly = serving(200, document({ azwah_development_only: true }));

    await expect(
      failure(discoverProvider(testConfig({ isDevelopmentBuild: false, demonstrationBuild: false }), devOnly)),
    ).resolves.toBe('unsafe_response');
    await expect(
      discoverProvider(testConfig({ isDevelopmentBuild: false, demonstrationBuild: true }), devOnly),
    ).resolves.toMatchObject({ developmentOnly: true });
  });

  it('still refuses http endpoints in a demonstration release build (D-148)', async () => {
    const http = { issuer: 'http://localhost:5090', authorization_endpoint: 'http://localhost:5090/authorize', token_endpoint: 'http://localhost:5090/token', revocation_endpoint: 'http://localhost:5090/revoke', azwah_development_only: true };
    const demonstration = { isDevelopmentBuild: false, demonstrationBuild: true };

    await expect(
      failure(discoverProvider(testConfig({ issuer: 'http://localhost:5090', ...demonstration }), serving(200, document(http)))),
    ).resolves.toBe('unsafe_response');
    await expect(
      failure(
        discoverProvider(
          testConfig(demonstration),
          serving(200, document({ token_endpoint: 'http://id.example.test/token', azwah_development_only: true })),
        ),
      ),
    ).resolves.toBe('unsafe_response');
  });

  it('refuses a provider without S256 or without the code flow when it lists them', async () => {
    await expect(failure(discoverProvider(testConfig(), serving(200, document({ code_challenge_methods_supported: ['plain'] }))))).resolves.toBe(
      'unsafe_response',
    );
    await expect(failure(discoverProvider(testConfig(), serving(200, document({ response_types_supported: ['id_token'] }))))).resolves.toBe(
      'unsafe_response',
    );
    await expect(
      discoverProvider(
        testConfig(),
        serving(200, document({ code_challenge_methods_supported: undefined, response_types_supported: undefined })),
      ),
    ).resolves.toMatchObject({ issuer: TEST_ISSUER });
  });

  it('refuses a document of the wrong shape or that is not JSON', async () => {
    await expect(failure(discoverProvider(testConfig(), serving(200, { issuer: TEST_ISSUER })))).resolves.toBe('unsafe_response');
    await expect(failure(discoverProvider(testConfig(), serving(200, undefined)))).resolves.toBe('unsafe_response');
  });

  it('treats no answer, a timeout and an error status as an unreachable provider', async () => {
    const offline: FetchLike = async () => {
      throw new TypeError('Network request failed');
    };
    const hanging: FetchLike = (_input, init) =>
      new Promise((_resolve, reject) => init?.signal?.addEventListener('abort', () => reject(new Error('aborted'))));

    await expect(failure(discoverProvider(testConfig(), offline))).resolves.toBe('provider_unreachable');
    await expect(failure(discoverProvider(testConfig(), hanging, 20))).resolves.toBe('provider_unreachable');
    await expect(failure(discoverProvider(testConfig(), serving(503, { error: 'down' })))).resolves.toBe('provider_unreachable');
    await expect(failure(discoverProvider(testConfig(), serving(404, {})))).resolves.toBe('provider_unreachable');
  });
});
