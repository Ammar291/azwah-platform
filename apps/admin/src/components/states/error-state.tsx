'use client';

import { CircleAlert, FileQuestion, Hourglass, KeyRound, LockKeyhole, LogIn, SlidersHorizontal, Unplug, WifiOff } from 'lucide-react';
import type { ReactNode } from 'react';

import { Button, buttonVariants } from '@/components/ui/button';
import { ApiError, errorKind, type ErrorKind } from '@/lib/api/errors';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';

import { StateLayout } from './state-layout';

const RETRYABLE: readonly ErrorKind[] = ['generic', 'network', 'unavailable', 'unexpected', 'conflict', 'rate_limited'];

/** A failed request as a translated state. The server's own text never reaches the page; its trace ID does. */
export function ErrorState({ error, onRetry, className }: { error: unknown; onRetry?: () => void; className?: string }) {
  const { t } = useI18n();
  const kind = errorKind(error);
  const states: Record<ErrorKind, { title: string; body: string; icon: ReactNode; tone: 'neutral' | 'danger' | 'warning' }> = {
    signed_out: { title: t.states.signedOutTitle, body: t.states.signedOutBody, icon: <LogIn />, tone: 'neutral' },
    denied: { title: t.states.deniedTitle, body: t.states.deniedBody, icon: <LockKeyhole />, tone: 'warning' },
    not_found: { title: t.states.notFoundTitle, body: t.states.notFoundBody, icon: <FileQuestion />, tone: 'neutral' },
    filters: { title: t.states.filtersTitle, body: t.states.filtersBody, icon: <SlidersHorizontal />, tone: 'warning' },
    conflict: { title: t.states.errorTitle, body: t.states.errorBody, icon: <CircleAlert />, tone: 'danger' },
    rate_limited: { title: t.states.rateLimitedTitle, body: t.states.rateLimitedBody, icon: <Hourglass />, tone: 'warning' },
    unexpected: { title: t.states.unexpectedTitle, body: t.states.unexpectedBody, icon: <CircleAlert />, tone: 'danger' },
    network: { title: t.states.networkTitle, body: t.states.networkBody, icon: <WifiOff />, tone: 'danger' },
    unavailable: { title: t.states.unavailableTitle, body: t.states.unavailableBody, icon: <Unplug />, tone: 'danger' },
    dev_token: { title: t.states.devTokenTitle, body: t.states.devTokenBody, icon: <KeyRound />, tone: 'warning' },
    generic: { title: t.states.errorTitle, body: t.states.errorBody, icon: <CircleAlert />, tone: 'danger' },
  };
  const state = states[kind];
  const traceId = error instanceof ApiError ? error.traceId : null;
  return (
    <StateLayout
      role="alert"
      icon={state.icon}
      title={state.title}
      tone={state.tone}
      className={className}
      body={
        <>
          <p>{state.body}</p>
          {traceId ? <p className="mt-1 break-all text-xs">{fill(t.states.traceId, { traceId })}</p> : null}
        </>
      }
    >
      {kind === 'signed_out' ? (
        <a href="/auth/sign-in" className={buttonVariants({ size: 'lg' })}>
          {t.states.signInAgain}
        </a>
      ) : null}
      {onRetry && RETRYABLE.includes(kind) ? (
        <Button size="lg" variant="outline" onClick={onRetry}>
          {t.common.retry}
        </Button>
      ) : null}
    </StateLayout>
  );
}
