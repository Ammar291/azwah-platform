'use client';

import {
  BarChart3,
  ClipboardCheck,
  ClipboardList,
  FileDown,
  Gauge,
  LayoutDashboard,
  ListChecks,
  ShieldAlert,
  ShieldCheck,
  Users,
  type LucideIcon,
} from 'lucide-react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';

import { attentionLabel } from '@/features/safety-alerts/safety-alert-labels';
import { useSafetyAlertSummary } from '@/lib/api/queries';
import { useI18n } from '@/lib/i18n-context';
import type { NavGroup, NavItem, NavKey } from '@/lib/permissions';
import { cn } from '@/lib/utils';

const ICONS: Record<NavKey, LucideIcon> = {
  overview: LayoutDashboard,
  analytics: BarChart3,
  exports: FileDown,
  assessments: ListChecks,
  participation: Users,
  configuration: ClipboardList,
  results: ClipboardCheck,
  privacyRequests: ShieldCheck,
  safetyAlerts: ShieldAlert,
  status: Gauge,
};

const GROUP_ORDER: readonly NavGroup[] = ['research', 'monitoring', 'administration', 'system'];

export function isCurrent(pathname: string | null, href: string): boolean {
  return pathname === href || (pathname?.startsWith(`${href}/`) ?? false);
}

/** The sections the caller's permissions allow, grouped; the current one is marked for assistive technology. */
export function SidebarNav({ navigation, onNavigate }: { navigation: NavItem[]; onNavigate?: () => void }) {
  const { t } = useI18n();
  const pathname = usePathname();
  return (
    <nav aria-label={t.nav.label} className="flex flex-col gap-5">
      {GROUP_ORDER.map((group) => {
        const items = navigation.filter((item) => item.group === group);
        if (items.length === 0) return null;
        return (
          <div key={group} className="flex flex-col gap-1">
            <p className="px-3 text-xs font-medium text-sidebar-foreground/70">{t.nav.groups[group]}</p>
            <ul className="flex flex-col gap-0.5">
              {items.map((item) => {
                const Icon = ICONS[item.key];
                const current = isCurrent(pathname, item.href);
                return (
                  <li key={item.href}>
                    <Link
                      href={item.href}
                      onClick={onNavigate}
                      aria-current={current ? 'page' : undefined}
                      className={cn(
                        'flex min-h-10 items-center gap-2.5 rounded-md px-3 py-2 text-sm text-sidebar-foreground transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:ring-2 focus-visible:ring-sidebar-ring focus-visible:outline-none',
                        current && 'bg-sidebar-accent font-medium text-sidebar-accent-foreground',
                      )}
                    >
                      <Icon aria-hidden className="size-4 shrink-0" />
                      {t.nav.items[item.key]}
                      {item.key === 'safetyAlerts' ? <SafetyAlertBadge /> : null}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        );
      })}
    </nav>
  );
}

/**
 * How many alerts need attention (open and acknowledged), read from the shell's summary. Red when any open alert is
 * critical; the number is hidden from assistive technology in favour of a full sentence. Absent while loading and at
 * zero. When the summary cannot be read it shows a neutral "!" instead, so a stale shell is never mistaken for a quiet one.
 */
function SafetyAlertBadge() {
  const { t, f, language } = useI18n();
  const summary = useSafetyAlertSummary();
  const base = 'ms-auto inline-flex min-w-5 items-center justify-center rounded-full px-1.5 text-xs font-semibold';
  if (summary.isError) {
    return (
      <span className={cn(base, 'bg-muted text-muted-foreground')}>
        <span aria-hidden>!</span>
        <span className="sr-only">{t.safetyAlerts.badgeStale}</span>
      </span>
    );
  }
  if (!summary.data) return null;
  const count = summary.data.open + summary.data.acknowledged;
  if (count === 0) return null;
  const critical = summary.data.openCritical > 0;
  return (
    <span className={cn(base, critical ? 'bg-destructive text-white' : 'bg-status-warning-soft text-status-warning')}>
      <span aria-hidden>{f.number(count)}</span>
      <span className="sr-only">{attentionLabel(t, language, count, f.number(count))}</span>
    </span>
  );
}
