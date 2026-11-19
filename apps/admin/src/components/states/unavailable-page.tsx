'use client';

import { SignOutButton } from '@/components/app-shell/sign-out-button';
import { Button } from '@/components/ui/button';
import { ApiError } from '@/lib/api/errors';
import { useI18n } from '@/lib/i18n-context';

import { ErrorState } from './error-state';

export type UnavailableReason = 'api_unreachable' | 'unexpected_response' | 'dev_token_missing' | 'not_configured';

const AS_ERROR: Record<UnavailableReason, ApiError> = {
  api_unreachable: new ApiError(502, 'bff.api_unreachable', null),
  unexpected_response: new ApiError(200, 'unexpected_response', null),
  dev_token_missing: new ApiError(503, 'bff.dev_token_missing', null),
  not_configured: new ApiError(503, 'bff.api_unreachable', null),
};

/** Shown instead of the dashboard when the platform cannot say who the caller is; nothing is guessed meanwhile. */
export function UnavailablePage({ reason }: { reason: UnavailableReason }) {
  const { t } = useI18n();
  return (
    <main id="main" className="mx-auto flex min-h-screen w-full max-w-2xl flex-col justify-center gap-4 px-4 py-10">
      <ErrorState error={AS_ERROR[reason]} />
      <div className="flex flex-wrap gap-2">
        <Button variant="outline" size="lg" onClick={() => window.location.reload()}>
          {t.common.retry}
        </Button>
        <SignOutButton />
      </div>
    </main>
  );
}
