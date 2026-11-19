import { redirect } from 'next/navigation';
import type { ReactNode } from 'react';

import { AppShell } from '@/components/app-shell/app-shell';
import { UnavailablePage } from '@/components/states/unavailable-page';
import { buildNavigation } from '@/lib/permissions';
import { getCaller } from '@/lib/server/caller';

/**
 * Every dashboard page sits behind this layout: it asks the platform who the caller is and what they may do, on the
 * server, before anything renders. Each page then checks its own permission.
 */
export default async function DashboardLayout({ children }: { children: ReactNode }) {
  const result = await getCaller();
  if (result.kind === 'signed_out') redirect('/sign-in?error=session_rejected');
  if (result.kind === 'unavailable') return <UnavailablePage reason={result.reason} />;
  const { caller } = result;
  return (
    <AppShell
      navigation={buildNavigation(caller.permissions)}
      persona={caller.persona}
      authMode={caller.authMode}
      dataSource={caller.dataSource}
    >
      {children}
    </AppShell>
  );
}
