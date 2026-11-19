import 'server-only';

import { cookies } from 'next/headers';
import { cache } from 'react';
import { z } from 'zod';

import { MOCK_PERMISSIONS } from '@/lib/mock/permissions';
import type { Persona } from '@/lib/personas';

import { getAdminConfig, type AdminConfig } from './config';
import { unsealSession, type Session } from './session';
import { readSessionValue } from './session-cookies';
import { bearerFor, callApi, type FetchApi } from './upstream';

/** Who is signed in to the dashboard and what the platform lets them do. Roles never come from the browser. */
export interface Caller {
  permissions: string[];
  persona: Persona | null;
  dataSource: 'api' | 'mock';
  authMode: 'oidc' | 'development';
}

export type CallerResult =
  | { kind: 'caller'; caller: Caller }
  | { kind: 'signed_out' }
  | { kind: 'unavailable'; reason: 'api_unreachable' | 'unexpected_response' | 'dev_token_missing' | 'not_configured' };

const permissionsSchema = z.object({
  permissions: z.array(z.string()),
  households: z.array(z.object({ householdId: z.string(), permissions: z.array(z.string()) })),
});

export async function loadCaller(config: AdminConfig, session: Session | null, fetchApi: FetchApi = fetch): Promise<CallerResult> {
  if (!session) return { kind: 'signed_out' };
  const persona = session.kind === 'development' ? session.persona : null;
  const authMode = config.auth.mode;

  if (config.data.source === 'mock') {
    if (!persona) return { kind: 'signed_out' };
    return { kind: 'caller', caller: { permissions: MOCK_PERMISSIONS[persona], persona, dataSource: 'mock', authMode } };
  }

  const bearer = bearerFor(config, session);
  if (!bearer.ok) return bearer.reason === 'dev_token_missing' ? { kind: 'unavailable', reason: 'dev_token_missing' } : { kind: 'signed_out' };

  let response: Response;
  try {
    response = await callApi(config.data.apiUrl, bearer.token, '/api/v1/me/permissions', { method: 'GET' }, fetchApi);
  } catch {
    return { kind: 'unavailable', reason: 'api_unreachable' };
  }
  if (response.status === 401) return { kind: 'signed_out' };
  if (!response.ok) return { kind: 'unavailable', reason: 'api_unreachable' };
  let body: unknown;
  try {
    body = await response.json();
  } catch {
    return { kind: 'unavailable', reason: 'unexpected_response' };
  }
  const parsed = permissionsSchema.safeParse(body);
  if (!parsed.success) return { kind: 'unavailable', reason: 'unexpected_response' };
  return { kind: 'caller', caller: { permissions: parsed.data.permissions, persona, dataSource: 'api', authMode } };
}

/** The signed-in caller for this request (once per render), from the session cookie and the platform's answer. */
export const getCaller = cache(async (): Promise<CallerResult> => {
  const result = getAdminConfig();
  if (!result.ok) return { kind: 'unavailable', reason: 'not_configured' };
  const session = await unsealSession(readSessionValue(await cookies(), result.config), result.config.sessionSecret);
  return loadCaller(result.config, session);
});
