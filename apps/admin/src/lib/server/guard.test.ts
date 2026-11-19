// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { CallerResult } from './caller';

const getCaller = vi.fn<() => Promise<CallerResult>>();
vi.mock('./caller', () => ({ getCaller: () => getCaller() }));

const { requirePermission } = await import('./guard');

const caller = { permissions: ['analytics.read'], persona: 'researcher' as const, dataSource: 'mock' as const, authMode: 'development' as const };

beforeEach(() => getCaller.mockReset());

describe('requirePermission', () => {
  it('returns the caller when the platform grants the permission', async () => {
    getCaller.mockResolvedValue({ kind: 'caller', caller });

    expect(await requirePermission('analytics.read')).toBe(caller);
  });

  it('refuses a caller without the permission', async () => {
    getCaller.mockResolvedValue({ kind: 'caller', caller });

    expect(await requirePermission('exports.manage')).toBeNull();
  });

  it('refuses when nobody is signed in or the platform cannot say', async () => {
    getCaller.mockResolvedValue({ kind: 'signed_out' });
    expect(await requirePermission('analytics.read')).toBeNull();

    getCaller.mockResolvedValue({ kind: 'unavailable', reason: 'api_unreachable' });
    expect(await requirePermission('analytics.read')).toBeNull();
  });
});
