import type { NextRequest } from 'next/server';

import { handleDevelopmentSignIn } from '@/lib/server/auth-handlers';
import { getAdminConfig } from '@/lib/server/config';

/** Development sign-in as a persona. Answers 404 unless the deployment is development. */
export function POST(request: NextRequest): Promise<Response> {
  return handleDevelopmentSignIn(request, getAdminConfig());
}
