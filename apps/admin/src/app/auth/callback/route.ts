import type { NextRequest } from 'next/server';

import { handleCallback } from '@/lib/server/auth-handlers';
import { getAdminConfig } from '@/lib/server/config';

/** Finishes OpenID Connect sign-in and seals the session cookie. */
export function GET(request: NextRequest): Promise<Response> {
  return handleCallback(request, getAdminConfig());
}
