import type { NextRequest } from 'next/server';

import { getAdminConfig } from '@/lib/server/config';
import { decide } from '@/lib/server/proxy-decision';

/** Keeps dashboard pages and the BFF behind a session and refreshes tokens; see src/lib/server/proxy-decision.ts. */
export function proxy(request: NextRequest) {
  return decide(request, { result: getAdminConfig() });
}

export const config = {
  // Everything except Next.js assets, files with an extension, the sign-in page and the /auth routes.
  matcher: ['/((?!_next/|favicon\\.ico|sign-in|auth/|.*\\.[a-z0-9]+$).*)'],
};
