import type { NextRequest } from 'next/server';

import { handleSignOut } from '@/lib/server/auth-handlers';
import { getAdminConfig } from '@/lib/server/config';

/** Signs out: clears the session, then ends the issuer session when it supports that. */
export function POST(request: NextRequest): Promise<Response> {
  return handleSignOut(request, getAdminConfig());
}
