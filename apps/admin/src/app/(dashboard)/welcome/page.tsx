import { redirect } from 'next/navigation';

import { WelcomeView } from '@/features/welcome/welcome-view';
import { firstSection } from '@/lib/permissions';
import { getCaller } from '@/lib/server/caller';

/**
 * The page for a role with no section. A caller whose role does open one (they typed the address, or kept an old
 * link) is sent to their first section rather than told they have none.
 */
export default async function WelcomePage() {
  const result = await getCaller();
  if (result.kind === 'caller') {
    const first = firstSection(result.caller.permissions);
    if (first !== '/welcome') redirect(first);
  }
  return <WelcomeView />;
}
