import { useRouter, type Href } from 'expo-router';
import { useCallback } from 'react';

/**
 * Leaves a finished flow (the roster confirmed, a questionnaire submitted). Every screen stacked over Home is
 * dismissed first, then `next` opens on top of Home (or nothing, to stay there). Back from `next` therefore goes
 * Home, and never walks through screens that have nothing left to do ("already set up", "already submitted").
 *
 * `dismissTo('/home')` pops the signed-in stack back to the side menu's group and shows Home in it; both calls go
 * through Expo Router's queue in order, so the stack changes once.
 */
export function useLeaveFlow(): (next?: Href) => void {
  const router = useRouter();
  return useCallback(
    (next?: Href) => {
      router.dismissTo('/home');
      if (next !== undefined) {
        router.push(next);
      }
    },
    [router],
  );
}
