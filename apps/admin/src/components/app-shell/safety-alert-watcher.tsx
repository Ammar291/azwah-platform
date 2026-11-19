'use client';

import { useQueryClient } from '@tanstack/react-query';
import Link from 'next/link';
import { useEffect, useRef } from 'react';
import { toast } from 'sonner';

import { buttonVariants } from '@/components/ui/button';
import { ApiError } from '@/lib/api/errors';
import { SAFETY_LIST_KEY, useSafetyAlertSummary } from '@/lib/api/queries';
import type { SafetyAlertSummary } from '@/lib/api/schemas';
import { useI18n } from '@/lib/i18n-context';

const SIGNED_OUT_TOAST = 'safety-alerts-signed-out';
const COUNT_PREFIX = /^\(\d+\) /;

function sameCounts(a: SafetyAlertSummary, b: SafetyAlertSummary): boolean {
  return a.open === b.open && a.acknowledged === b.acknowledged && a.openCritical === b.openCritical;
}

/**
 * Reads the alert summary on a timer, for the whole shell. It
 * - shows a warning toast when a newer unresolved alert appears (critical ones stay until dismissed); the first
 *   successful read only sets the baseline: what was already there is on the page and in the badge, not news;
 * - reads the alert list again whenever the summary moves or its counts change, so toast and list stay in step;
 * - prefixes the page title with the count, so a hidden tab shows it;
 * - says so when it can no longer watch because the session ended (the badge shows a "!" for any failed read).
 * Renders nothing.
 */
export function SafetyAlertWatcher({ pollMs }: { pollMs?: number }) {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const summary = useSafetyAlertSummary(pollMs);
  const data = summary.data;
  const error = summary.error;
  const previous = useRef<SafetyAlertSummary | null>(null);
  const baselineAt = useRef<string | null>(null);
  const signedOutShown = useRef(false);

  useEffect(() => {
    if (!data) return;
    const before = previous.current;
    previous.current = data;
    const latest = data.latestDetectedAt;
    const next = latest === null ? null : Date.parse(latest);
    const lastAt = baselineAt.current;
    if (latest === null || (next !== null && !Number.isNaN(next))) baselineAt.current = latest;
    if (before === null) return;

    const moved = latest !== before.latestDetectedAt;
    if (moved || !sameCounts(before, data)) void queryClient.invalidateQueries({ queryKey: SAFETY_LIST_KEY });

    const newer = next !== null && !Number.isNaN(next) && (lastAt === null || next > Date.parse(lastAt));
    const critical = data.openCritical > before.openCritical;
    if (!newer && !critical) return;
    const options = {
      duration: critical ? Infinity : 15_000,
      action: (
        <Link href="/safety-alerts" className={buttonVariants({ size: 'sm' })}>
          {t.safetyAlerts.toast.action}
        </Link>
      ),
    };
    toast.warning(critical ? t.safetyAlerts.toast.criticalTitle : t.safetyAlerts.toast.title, options);
  }, [data, queryClient, t]);

  // A session that has ended is shown once and stays until dismissed; a good read afterwards clears it.
  useEffect(() => {
    if (error instanceof ApiError && error.status === 401) {
      if (signedOutShown.current) return;
      signedOutShown.current = true;
      toast.error(t.safetyAlerts.toast.signedOutTitle, {
        id: SIGNED_OUT_TOAST,
        duration: Infinity,
        action: (
          <a href="/auth/sign-in" className={buttonVariants({ size: 'sm' })}>
            {t.safetyAlerts.toast.signIn}
          </a>
        ),
      });
    } else if (data && !error && signedOutShown.current) {
      signedOutShown.current = false;
      toast.dismiss(SIGNED_OUT_TOAST);
    }
  }, [error, data, t]);

  const count = data ? data.open + data.acknowledged : 0;
  useEffect(() => {
    const apply = () => {
      const base = document.title.replace(COUNT_PREFIX, '');
      const wanted = count > 0 ? `(${count}) ${base}` : base;
      if (document.title !== wanted) document.title = wanted;
    };
    apply();
    // The router sets the title again on every navigation; keep the prefix on whatever it writes.
    const observer = new MutationObserver(apply);
    observer.observe(document.head, { childList: true, subtree: true, characterData: true });
    return () => {
      observer.disconnect();
      document.title = document.title.replace(COUNT_PREFIX, '');
    };
  }, [count]);

  return null;
}
