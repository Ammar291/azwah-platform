'use client';

import { CircleAlert } from 'lucide-react';

import { StateLayout } from '@/components/states/state-layout';
import { Button } from '@/components/ui/button';
import { fill } from '@/lib/format';
import { useI18n } from '@/lib/i18n-context';

/** A page that failed to render. The error's own message is never shown; its digest is the server log reference. */
export default function DashboardError({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  const { t } = useI18n();
  return (
    <StateLayout
      role="alert"
      tone="danger"
      icon={<CircleAlert />}
      title={t.errorPage.title}
      body={
        <>
          <p>{t.errorPage.body}</p>
          {error.digest ? <p className="mt-1 text-xs">{fill(t.errorPage.reference, { digest: error.digest })}</p> : null}
        </>
      }
    >
      <Button size="lg" variant="outline" onClick={reset}>
        {t.errorPage.retry}
      </Button>
    </StateLayout>
  );
}
