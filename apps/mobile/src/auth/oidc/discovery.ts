import { z } from 'zod';

import { fetchWithTimeout, type FetchLike } from '@/api/client';

import { SignInError } from '../types';
import { acceptsDevelopmentIssuer, isAllowedUrl, type OidcConfig } from './oidcConfig';

const DISCOVERY_TIMEOUT_MS = 10_000;
/** What the Azwah identity provider understands as "go straight to Google" (D-147). */
export const GOOGLE_HINT = 'google';

/** The parts of OpenID Connect Discovery 1.0 (and RFC 8414, RFC 9207) the app uses; anything else is ignored. */
const discoverySchema = z.object({
  issuer: z.string(),
  authorization_endpoint: z.string(),
  token_endpoint: z.string(),
  revocation_endpoint: z.string().optional(),
  end_session_endpoint: z.string().optional(),
  response_types_supported: z.array(z.string()).optional(),
  code_challenge_methods_supported: z.array(z.string()).optional(),
  authorization_response_iss_parameter_supported: z.boolean().optional(),
  /** Set by the platform's development identity provider (simulated sign-in). */
  azwah_development_only: z.boolean().optional(),
  /**
   * The Azwah identity provider's configured upstream sign-ins, as `idp_hint` values (`uae_pass`, `google`; D-147).
   * A value the app cannot read counts as none, never as a refusal of the issuer.
   */
  azwah_identity_providers: z.array(z.string()).optional().catch(undefined),
});

export interface ProviderMetadata {
  issuer: string;
  authorizationEndpoint: string;
  tokenEndpoint: string;
  revocationEndpoint: string | null;
  endSessionEndpoint: string | null;
  /** RFC 9207: the callback must then carry `iss`. */
  issParameterSupported: boolean;
  developmentOnly: boolean;
  /** The issuer offers the Google sign-in (`azwah_identity_providers` lists `google`, D-147). */
  offersGoogle: boolean;
}

/** OpenID Connect Discovery 1.0 section 4: the issuer without a trailing slash, then the well-known path. */
export function discoveryUrl(issuer: string): string {
  return `${issuer.replace(/\/+$/, '')}/.well-known/openid-configuration`;
}

/**
 * Reads and checks the issuer's discovery document. Refused (`unsafe_response`): another issuer, an endpoint that is
 * not https (http is accepted in development builds only), no S256 or no `code` response type when the lists are
 * given, and an issuer that declares itself development-only in a release build. No answer, or an error status, is
 * `provider_unreachable`.
 */
export async function discoverProvider(
  config: OidcConfig,
  fetchImpl: FetchLike,
  timeoutMs: number = DISCOVERY_TIMEOUT_MS,
): Promise<ProviderMetadata> {
  let request: Awaited<ReturnType<typeof fetchWithTimeout>>;
  try {
    request = await fetchWithTimeout(
      discoveryUrl(config.issuer),
      { method: 'GET', headers: { Accept: 'application/json' } },
      timeoutMs,
      fetchImpl,
    );
  } catch {
    throw new SignInError('provider_unreachable');
  }
  const { response, done } = request;
  try {
    if (response.status !== 200) {
      throw new SignInError('provider_unreachable');
    }
    let body: unknown;
    try {
      body = await response.json();
    } catch {
      throw new SignInError(request.aborted() ? 'provider_unreachable' : 'unsafe_response');
    }
    const parsed = discoverySchema.safeParse(body);
    if (!parsed.success) {
      throw new SignInError('unsafe_response');
    }
    const document = parsed.data;
    const endpoints = [
      document.authorization_endpoint,
      document.token_endpoint,
      document.revocation_endpoint,
      document.end_session_endpoint,
    ].filter((endpoint): endpoint is string => endpoint !== undefined);
    const developmentOnly = document.azwah_development_only === true;
    if (
      document.issuer !== config.issuer ||
      !endpoints.every((endpoint) => isAllowedUrl(endpoint, config.isDevelopmentBuild)) ||
      (document.code_challenge_methods_supported && !document.code_challenge_methods_supported.includes('S256')) ||
      (document.response_types_supported && !document.response_types_supported.includes('code')) ||
      (developmentOnly && !acceptsDevelopmentIssuer(config))
    ) {
      throw new SignInError('unsafe_response');
    }
    return {
      issuer: document.issuer,
      authorizationEndpoint: document.authorization_endpoint,
      tokenEndpoint: document.token_endpoint,
      revocationEndpoint: document.revocation_endpoint ?? null,
      endSessionEndpoint: document.end_session_endpoint ?? null,
      issParameterSupported: document.authorization_response_iss_parameter_supported === true,
      developmentOnly,
      offersGoogle: document.azwah_identity_providers?.includes(GOOGLE_HINT) === true,
    };
  } finally {
    done();
  }
}
