import 'server-only';

import type { AdminConfig } from './config';
import type { Session } from './session';
import { bearerFor, callApi, type FetchApi } from './upstream';

/** Long enough for a healthy API, short enough not to hold up a sign-in or sign-out. */
const RECORD_TIMEOUT_MS = 3_000;

export type SessionEvent = 'sign-ins' | 'sign-outs';

/**
 * Tells the platform that this person signed in to or is signing out of the dashboard (`POST /api/v1/me/sign-ins` or
 * `sign-outs`, audited as `identity.session.started` or `ended`). Best effort: a failure never stops the sign-in or
 * sign-out, is never shown and is not logged. Nothing is sent with mock data, which has no platform behind it.
 */
export async function recordSessionEvent(
  config: AdminConfig,
  session: Session,
  event: SessionEvent,
  fetchApi: FetchApi = fetch,
): Promise<void> {
  if (config.data.source !== 'api') return;
  const bearer = bearerFor(config, session);
  if (!bearer.ok) return;
  try {
    const response = await callApi(
      config.data.apiUrl,
      bearer.token,
      `/api/v1/me/${event}`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ client: 'admin' }),
        signal: AbortSignal.timeout(RECORD_TIMEOUT_MS),
      },
      fetchApi,
    );
    await response.body?.cancel();
  } catch {
    // Best effort: the identity provider's own log is the authoritative record.
  }
}
