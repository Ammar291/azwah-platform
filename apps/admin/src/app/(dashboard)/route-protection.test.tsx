import { isValidElement, type ReactElement } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AppShell } from '@/components/app-shell/app-shell';
import { PermissionDenied } from '@/components/states/permission-denied';
import { UnavailablePage } from '@/components/states/unavailable-page';
import { PAGE_PERMISSIONS, type Permission } from '@/lib/permissions';
import type { CallerResult } from '@/lib/server/caller';

const requirePermission = vi.fn<(permission: Permission) => Promise<unknown>>();
const getCaller = vi.fn<() => Promise<CallerResult>>();
const redirect = vi.fn((url: string) => {
  throw new Error(`redirect:${url}`);
});

vi.mock('@/lib/server/guard', () => ({ requirePermission: (permission: Permission) => requirePermission(permission) }));
vi.mock('@/lib/server/caller', () => ({ getCaller: () => getCaller() }));
vi.mock('next/navigation', async () => ({ ...(await import('@/test/navigation')).nextNavigation, redirect: (url: string) => redirect(url) }));

const caller = { permissions: ['analytics.read'], persona: 'researcher' as const, dataSource: 'mock' as const, authMode: 'development' as const };
const id = '0190f0e2-3333-7000-8000-000000000001';

/** Every page behind a permission, and the permission it must ask for. */
const GUARDED: { name: string; load: () => Promise<{ default: (props: never) => Promise<ReactElement> }>; permission: Permission; props?: object }[] = [
  { name: 'overview', load: () => import('./overview/page'), permission: PAGE_PERMISSIONS.overview },
  { name: 'analytics', load: () => import('./analytics/page'), permission: PAGE_PERMISSIONS.analytics },
  { name: 'exports', load: () => import('./exports/page'), permission: PAGE_PERMISSIONS.exports },
  { name: 'assessment monitoring', load: () => import('./monitoring/assessments/page'), permission: PAGE_PERMISSIONS.assessments },
  { name: 'participation', load: () => import('./monitoring/participation/page'), permission: PAGE_PERMISSIONS.participation },
  { name: 'configuration', load: () => import('./configuration/page'), permission: PAGE_PERMISSIONS.configuration },
  { name: 'household results', load: () => import('./results/page'), permission: PAGE_PERMISSIONS.results },
  { name: 'privacy requests', load: () => import('./privacy-requests/page'), permission: PAGE_PERMISSIONS.privacyRequests },
  { name: 'safety alerts', load: () => import('./safety-alerts/page'), permission: PAGE_PERMISSIONS.safetyAlerts },
  {
    name: 'instrument',
    load: () => import('./configuration/instruments/[instrumentId]/page'),
    permission: PAGE_PERMISSIONS.configuration,
    props: { params: Promise.resolve({ instrumentId: id }) },
  },
  {
    name: 'version',
    load: () => import('./configuration/versions/[versionId]/page'),
    permission: PAGE_PERMISSIONS.configuration,
    props: { params: Promise.resolve({ versionId: id }) },
  },
  {
    name: 'rule set',
    load: () => import('./configuration/rule-sets/[ruleSetId]/page'),
    permission: PAGE_PERMISSIONS.configuration,
    props: { params: Promise.resolve({ ruleSetId: id }) },
  },
];

beforeEach(() => {
  requirePermission.mockReset();
  getCaller.mockReset();
  redirect.mockClear();
});

describe('guarded pages', () => {
  it('covers every permission the navigation knows', () => {
    expect(new Set(GUARDED.map((page) => page.permission))).toEqual(new Set(Object.values(PAGE_PERMISSIONS)));
  });

  it.each(GUARDED)('$name shows permission denied without its permission', async ({ load, permission, props }) => {
    requirePermission.mockResolvedValue(null);
    const Page = (await load()).default;

    const element = await Page((props ?? {}) as never);

    expect(requirePermission).toHaveBeenCalledWith(permission);
    expect(isValidElement(element) && element.type).toBe(PermissionDenied);
  });

  it.each(GUARDED)('$name shows its view with the permission', async ({ load, props }) => {
    requirePermission.mockResolvedValue(caller);
    const Page = (await load()).default;

    const element = await Page((props ?? {}) as never);

    expect(isValidElement(element) && element.type).not.toBe(PermissionDenied);
  });
});

describe('dashboard layout', () => {
  async function layout() {
    return (await import('./layout')).default;
  }

  it('sends a caller the platform does not accept to sign-in', async () => {
    getCaller.mockResolvedValue({ kind: 'signed_out' });
    const Layout = await layout();

    await expect(Layout({ children: null })).rejects.toThrow('redirect:/sign-in?error=session_rejected');
  });

  it('shows the unavailable page when the platform cannot say who the caller is', async () => {
    getCaller.mockResolvedValue({ kind: 'unavailable', reason: 'api_unreachable' });
    const Layout = await layout();

    const element = await Layout({ children: null });

    expect(isValidElement(element) && element.type).toBe(UnavailablePage);
  });

  it('builds the navigation from the permissions the platform returned', async () => {
    getCaller.mockResolvedValue({ kind: 'caller', caller });
    const Layout = await layout();

    const element = (await Layout({ children: null })) as ReactElement<{ navigation: { href: string }[] }>;

    expect(element.type).toBe(AppShell);
    expect(element.props.navigation.map((item) => item.href)).toEqual(['/overview', '/status']);
  });
});

describe('home page', () => {
  it('opens the first section the caller may use', async () => {
    getCaller.mockResolvedValue({ kind: 'caller', caller: { ...caller, permissions: ['content.manage'] } });
    const Home = (await import('./page')).default;

    await expect(Home()).rejects.toThrow('redirect:/configuration');
  });

  it('sends a signed-out caller to sign-in', async () => {
    getCaller.mockResolvedValue({ kind: 'signed_out' });
    const Home = (await import('./page')).default;

    await expect(Home()).rejects.toThrow('redirect:/sign-in');
  });
});

describe('welcome page', () => {
  it('sends a caller whose role opens a section there, instead of saying they have none', async () => {
    getCaller.mockResolvedValue({ kind: 'caller', caller });
    const Welcome = (await import('./welcome/page')).default;

    await expect(Welcome()).rejects.toThrow('redirect:/overview');
  });

  it('greets a caller whose role opens no section', async () => {
    getCaller.mockResolvedValue({ kind: 'caller', caller: { ...caller, permissions: [], persona: 'support_coordinator' } });
    const Welcome = (await import('./welcome/page')).default;

    const element = await Welcome();

    expect(isValidElement(element)).toBe(true);
    expect(redirect).not.toHaveBeenCalled();
  });
});
