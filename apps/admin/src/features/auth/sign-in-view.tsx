'use client';

import { CircleAlert, CircleCheck, FlaskConical, LogIn, Settings } from 'lucide-react';
import { useId } from 'react';

import { LanguageSwitch } from '@/components/app-shell/language-switch';
import { Button, buttonVariants } from '@/components/ui/button';
import { toSearch } from '@/lib/api/search';
import { useI18n } from '@/lib/i18n-context';
import { PERSONAS } from '@/lib/personas';
import type { ConfigProblem } from '@/lib/server/config';

export type SignInMode =
  | { kind: 'oidc' }
  | { kind: 'development'; mockData: boolean }
  | { kind: 'not_configured'; problems: ConfigProblem[] };

const ERROR_CODES = ['transaction_missing', 'callback_rejected', 'issuer_unreachable', 'not_configured', 'session_rejected', 'cross_origin'] as const;
type SignInError = (typeof ERROR_CODES)[number];

function knownError(code: string | null): SignInError | null {
  return code !== null && (ERROR_CODES as readonly string[]).includes(code) ? (code as SignInError) : null;
}

/**
 * The sign-in page: the organisation's identity provider in production, a clearly labelled persona picker in
 * development, or the list of missing settings. It never names an identity provider that is not configured.
 *
 * Anyone can open `/sign-in?error=…` from a crafted link, so an error names server settings only in development: a
 * cross-origin refusal is explained plainly everywhere, and the ADMIN_APP_ORIGIN hint is added only when
 * `development` (ADMIN_ENVIRONMENT=development) is set.
 */
export function SignInView({
  mode,
  returnTo,
  error,
  signedOut,
  development = false,
}: {
  mode: SignInMode;
  returnTo: string;
  error: string | null;
  signedOut: boolean;
  /** The dashboard runs with ADMIN_ENVIRONMENT=development, so configuration hints may be shown. */
  development?: boolean;
}) {
  const { t } = useI18n();
  const failure = knownError(error);
  const personaGroup = useId();
  return (
    <main id="main" className="grid min-h-screen bg-background md:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
      <section className="flex flex-col justify-between gap-10 bg-sidebar px-6 py-8 text-sidebar-foreground md:px-10 md:py-12">
        <div className="flex flex-col gap-2 text-sidebar-primary">
          <span className="font-heading text-5xl leading-none font-semibold">{t.app.name}</span>
          <span className="text-sm opacity-80">{t.app.tagline}</span>
        </div>
        <p className="max-w-sm text-sm leading-relaxed opacity-90">{t.signIn.intro}</p>
      </section>
      <section className="flex flex-col gap-6 px-6 py-8 md:px-12 md:py-12">
        <div className="flex justify-end">
          <LanguageSwitch />
        </div>
        <div className="flex w-full max-w-md flex-col gap-6">
          <h1 className="font-heading text-4xl font-semibold text-primary">{t.signIn.title}</h1>
          {signedOut ? (
            <p role="status" className="flex items-center gap-2 rounded-md bg-status-success-soft px-3 py-2 text-sm text-status-success">
              <CircleCheck aria-hidden className="size-4" />
              {t.signIn.signedOut}
            </p>
          ) : null}
          {failure ? (
            <p role="alert" className="flex items-start gap-2 rounded-md bg-status-danger-soft px-3 py-2 text-sm text-status-danger">
              <CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />
              <span className="flex flex-col gap-1">
                <span>{t.signIn.errors[failure]}</span>
                {failure === 'cross_origin' && development ? <span>{t.signIn.crossOriginDevelopmentHint}</span> : null}
              </span>
            </p>
          ) : null}

          {mode.kind === 'oidc' ? (
            <div className="flex flex-col gap-3">
              <a href={`/auth/sign-in${toSearch({ returnTo: returnTo === '/' ? null : returnTo })}`} className={buttonVariants({ size: 'lg', className: 'h-11 w-full sm:w-auto' })}>
                <LogIn aria-hidden className="rtl:rotate-180" />
                {t.signIn.oidcButton}
              </a>
              <p className="text-sm text-muted-foreground">{t.signIn.oidcNote}</p>
            </div>
          ) : null}

          {mode.kind === 'development' ? (
            <form action="/auth/development" method="post" className="flex flex-col gap-4 rounded-lg border border-status-warning/30 bg-card p-5">
              <div className="flex flex-col gap-1">
                <h2 className="flex items-center gap-2 text-base font-semibold">
                  <FlaskConical aria-hidden className="size-4 text-status-warning" />
                  {t.signIn.devTitle}
                </h2>
                <p className="text-sm text-muted-foreground">{t.signIn.devNote}</p>
                {mode.mockData ? <p className="text-sm text-status-warning">{t.signIn.devMock}</p> : null}
              </div>
              <input type="hidden" name="returnTo" value={returnTo} />
              <div role="radiogroup" aria-labelledby={personaGroup} className="flex flex-col gap-1">
                <p id={personaGroup} className="text-sm font-medium">
                  {t.signIn.devPersona}
                </p>
                {PERSONAS.map((persona, index) => (
                  <label key={persona} className="flex min-h-11 items-center gap-3 rounded-md px-2 hover:bg-muted">
                    <input type="radio" name="persona" value={persona} defaultChecked={index === 0} className="size-4 accent-[var(--primary)]" />
                    <span className="text-sm">{t.shell.roles[persona]}</span>
                  </label>
                ))}
              </div>
              <div>
                <Button type="submit" size="lg">
                  {t.signIn.devSubmit}
                </Button>
              </div>
            </form>
          ) : null}

          {mode.kind === 'not_configured' ? (
            <section className="flex flex-col gap-3 rounded-lg border border-border bg-card p-5">
              <h2 className="flex items-center gap-2 text-base font-semibold">
                <Settings aria-hidden className="size-4" />
                {t.signIn.notConfiguredTitle}
              </h2>
              <p className="text-sm text-muted-foreground">{t.signIn.notConfiguredBody}</p>
              <ul className="flex list-disc flex-col gap-1 ps-5 text-sm">
                {mode.problems.map((problem) => (
                  <li key={problem}>{t.signIn.problems[problem]}</li>
                ))}
              </ul>
            </section>
          ) : null}
        </div>
      </section>
    </main>
  );
}
