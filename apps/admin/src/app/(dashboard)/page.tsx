import { redirect } from 'next/navigation';

import { firstSection } from '@/lib/permissions';
import { getCaller } from '@/lib/server/caller';

/** The dashboard's front door: the first section the caller's permissions open. */
export default async function HomePage() {
  const result = await getCaller();
  if (result.kind !== 'caller') redirect('/sign-in');
  redirect(firstSection(result.caller.permissions));
}
