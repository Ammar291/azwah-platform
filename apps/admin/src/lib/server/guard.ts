import 'server-only';

import type { Permission } from '@/lib/permissions';

import { getCaller, type Caller } from './caller';

/**
 * The caller when they hold the page's permission, otherwise null and the page renders its permission-denied state.
 * The API checks the permission again on every request the page makes.
 */
export async function requirePermission(permission: Permission): Promise<Caller | null> {
  const result = await getCaller();
  return result.kind === 'caller' && result.caller.permissions.includes(permission) ? result.caller : null;
}
