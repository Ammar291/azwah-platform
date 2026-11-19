import { useAuth } from '@/auth/AuthContext';
import { isStartupReady } from '@/auth/entryHref';

import { BrandSplash } from './BrandSplash';

/**
 * The opening sequence at start-up. It waits only for the remembered session (if any) to be restored, which itself
 * never waits more than a few seconds for the network; whether a signed-in person still confirms their details is
 * shown by the anchor route afterwards, so a slow server never holds the splash (or hides the lock screen).
 */
export function StartupSplash({ onDone }: { onDone: () => void }) {
  const { state } = useAuth();
  return <BrandSplash ready={isStartupReady(state)} onDone={onDone} />;
}
