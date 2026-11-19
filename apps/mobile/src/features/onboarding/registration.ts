import { useMutation, useQueryClient } from '@tanstack/react-query';

import { useParticipantApi } from '@/api/participant/ParticipantApiContext';
import { participantKeys, useMe } from '@/api/participant/queries';
import type { Me } from '@/api/participant/schemas';
import { useAuth } from '@/auth/AuthContext';

/**
 * Whether the signed-in person still has to confirm their details (D-97):
 * - `checking`: the first answer from the server is on its way;
 * - `required`: the server says they are not registered yet;
 * - `registered`: they are, or the server predates registration;
 * - `unknown`: the server could not be asked (offline, an error). Home is shown, with its own offline and error
 *   states; the server never needs registration to serve anything, so nothing is unlocked by guessing.
 */
export type RegistrationStatus = 'checking' | 'required' | 'registered' | 'unknown';

export function registrationStatusOf(me: Pick<Me, 'registeredAt'> | undefined, failed: boolean): RegistrationStatus {
  if (me) {
    return me.registeredAt === null ? 'required' : 'registered';
  }
  return failed ? 'unknown' : 'checking';
}

/**
 * Signed out, there is nothing to check and the query stays idle. `enabled: false` (the app is locked) asks nothing
 * until it is unlocked.
 */
export function useRegistrationStatus({ enabled = true }: { enabled?: boolean } = {}): RegistrationStatus | null {
  const { state } = useAuth();
  const signedIn = state.status === 'signed_in';
  const me = useMe({ enabled: signedIn && enabled });
  if (!signedIn) {
    return null;
  }
  // A paused query (offline) never fails: it is "unknown" rather than "checking" for ever.
  return registrationStatusOf(me.data, me.isError || me.fetchStatus === 'paused');
}

/**
 * Confirms the details: registers the person, then writes the answer into the cached `me`. `onRegistered` runs first,
 * so a caller can keep onboarding open ("account ready") before the registration gate sees the change.
 */
export function useRegisterMe({ onRegistered }: { onRegistered?: () => void } = {}) {
  const api = useParticipantApi();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.registerMe(),
    onSuccess: ({ registeredAt }) => {
      onRegistered?.();
      queryClient.setQueryData<Me>(participantKeys.me(), (me) => (me ? { ...me, registeredAt } : me));
      void queryClient.invalidateQueries({ queryKey: participantKeys.me() });
    },
  });
}
