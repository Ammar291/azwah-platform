import type { NextRequest } from 'next/server';

import { handleSignIn } from '@/lib/server/auth-handlers';
import { getAdminConfig } from '@/lib/server/config';

/** Starts OpenID Connect sign-in, or sends development sign-in to the persona picker. */
export function GET(request: NextRequest): Promise<Response> {
  return handleSignIn(request, getAdminConfig());
}
