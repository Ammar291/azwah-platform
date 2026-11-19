import type { NextRequest } from 'next/server';

import { getMockBackend } from '@/lib/mock/backend';
import { handleBff } from '@/lib/server/bff';
import { getAdminConfig } from '@/lib/server/config';
import { problem } from '@/lib/server/problem';
import { unsealSession } from '@/lib/server/session';
import { readSessionValue } from '@/lib/server/session-cookies';

type Context = { params: Promise<{ path: string[] }> };

/** The browser's only way to the API: allowlisted requests, forwarded with the session's token (src/lib/server/bff.ts). */
async function forward(request: NextRequest, context: Context): Promise<Response> {
  const result = getAdminConfig();
  if (!result.ok) return problem(503, 'bff.not_configured');
  const { config } = result;
  const session = await unsealSession(readSessionValue(request.cookies, config), config.sessionSecret);
  const { path } = await context.params;
  return handleBff(request, path, {
    config,
    session,
    fetchApi: fetch,
    mock: config.data.source === 'mock' ? getMockBackend(config.data.scenario) : null,
  });
}

export const GET = forward;
export const POST = forward;
