'use client';

import type { ReactNode } from 'react';

import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';
import type { NavItem } from '@/lib/permissions';
import type { Persona } from '@/lib/personas';

import { BrandMark } from './brand-mark';
import { DevBanner } from './dev-banner';
import { LanguageSwitch } from './language-switch';
import { MobileNav } from './mobile-nav';
import { SafetyAlertWatcher } from './safety-alert-watcher';
import { SidebarNav } from './sidebar-nav';
import { SignOutButton } from './sign-out-button';

export interface AppShellProps {
  navigation: NavItem[];
  persona: Persona | null;
  authMode: 'oidc' | 'development';
  dataSource: 'api' | 'mock';
  children: ReactNode;
}

/** The dashboard frame: a pine sidebar on wide screens, a sheet on small ones, and a top bar with the account. */
export function AppShell({ navigation, persona, authMode, dataSource, children }: AppShellProps) {
  const { t } = useI18n();
  const account = persona ? fill(t.shell.signedInAs, { role: t.shell.roles[persona] }) : t.shell.signedIn;
  return (
    <div className="flex min-h-screen bg-background">
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-primary px-3 py-2 text-primary-foreground focus:not-sr-only focus:fixed focus:start-3 focus:top-3"
      >
        {t.app.skipToContent}
      </a>
      {navigation.some((item) => item.key === 'safetyAlerts') ? <SafetyAlertWatcher /> : null}
      <aside className="sticky top-0 hidden h-screen w-64 shrink-0 flex-col gap-8 overflow-y-auto bg-sidebar px-3 py-6 text-sidebar-foreground md:flex">
        <BrandMark className="px-3 text-sidebar-primary" />
        <SidebarNav navigation={navigation} />
      </aside>
      <div className="flex min-w-0 flex-1 flex-col">
        <DevBanner developmentSignIn={authMode === 'development'} mockData={dataSource === 'mock'} />
        <header className="flex items-center gap-2 border-b border-border bg-card px-4 py-2 md:px-8">
          <MobileNav navigation={navigation} />
          <BrandMark compact className="text-primary md:hidden" />
          <div className="ms-auto flex flex-wrap items-center justify-end gap-2">
            <p className="hidden text-sm text-muted-foreground sm:block">{account}</p>
            <LanguageSwitch />
            <SignOutButton />
          </div>
        </header>
        <main id="main" tabIndex={-1} className="mx-auto flex w-full max-w-6xl flex-1 flex-col gap-6 px-4 py-6 outline-none md:px-8 md:py-8">
          {children}
        </main>
      </div>
    </div>
  );
}
