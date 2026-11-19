import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useRef } from 'react';

import type { ParticipantApi } from '@/api/participant/participantApi';
import { useParticipantApi } from '@/api/participant/ParticipantApiContext';
import { invalidateParticipantData, participantKeys } from '@/api/participant/queries';
import type { MemberDetails } from '@/api/participant/schemas';

import { demoHousehold } from './demoHousehold';
import * as requests from './rosterRequests';

/*
 * The household changes as TanStack Query mutations. Each hook keeps what it needs to retry safely (whether an
 * earlier attempt may have reached the server) for as long as its screen is open. Screens call these hooks and never
 * the API directly.
 */

/** After a roster change the server has cleared the confirmation, so the cached one is dropped and data refetched. */
function rosterChanged(queryClient: QueryClient, householdId: string): void {
  queryClient.removeQueries({ queryKey: participantKeys.rosterConfirmation(householdId) });
  void invalidateParticipantData(queryClient);
}

/** Reads the roster from the server (refreshing the cached copy) so a change is planned on what it holds now. */
function readRoster(queryClient: QueryClient, api: ParticipantApi, householdId: string) {
  return queryClient.fetchQuery({
    queryKey: participantKeys.members(householdId),
    queryFn: () => api.listMembers(householdId),
    staleTime: 0,
  });
}

/** Creates the household from the roster `rosterFor` gives for the mutation's input. */
function useCreateHouseholdFrom<Input>(rosterFor: (input: Input) => { self: MemberDetails; others: MemberDetails[] }) {
  const api = useParticipantApi();
  const queryClient = useQueryClient();
  const uncertain = useRef(false);
  return useMutation({
    mutationFn: async (input: Input) => {
      const { self, others } = rosterFor(input);
      try {
        const created = await requests.createHousehold(api, self, uncertain.current, others);
        uncertain.current = false;
        return created;
      } catch (error) {
        // Once an attempt may have created the household, every later attempt checks first.
        uncertain.current = uncertain.current || requests.mayHaveReachedServer(error);
        throw error;
      }
    },
    onSuccess: () => void invalidateParticipantData(queryClient),
  });
}

/** The household with the caller only; the rest of the roster is added one person at a time. */
export function useCreateHousehold() {
  return useCreateHouseholdFrom((self: MemberDetails) => ({ self, others: [] }));
}

/** Development builds only (D-131): the household with the whole made-up demo family, in one request. */
export function useCreateDemoHousehold() {
  return useCreateHouseholdFrom<void>(() => demoHousehold(new Date()));
}

export function useAddMember(householdId: string) {
  const api = useParticipantApi();
  const queryClient = useQueryClient();
  const pending = useRef<requests.UncertainAdd | null>(null);
  return useMutation({
    mutationFn: async (details: MemberDetails) => {
      const roster = await readRoster(queryClient, api, householdId);
      const baseline = pending.current?.baseline ?? new Set(roster.map((member) => member.id));
      try {
        const member = await requests.addMember(api, householdId, roster, details, pending.current);
        pending.current = null;
        return member;
      } catch (error) {
        if (pending.current || requests.mayHaveReachedServer(error)) {
          pending.current = { baseline, details };
        }
        throw error;
      }
    },
    // Settled, not succeeded: moving a role may have changed the roster even when adding failed.
    onSettled: () => rosterChanged(queryClient, householdId),
  });
}

export function useUpdateMember(householdId: string, memberId: string) {
  const api = useParticipantApi();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (details: MemberDetails) => {
      const roster = await readRoster(queryClient, api, householdId);
      const member = requests.requireActiveMember(roster, memberId);
      return requests.updateMember(api, householdId, roster, member, details);
    },
    onSettled: () => rosterChanged(queryClient, householdId),
  });
}

export function useRemoveMember(householdId: string) {
  const api = useParticipantApi();
  const queryClient = useQueryClient();
  const uncertain = useRef(new Set<string>());
  return useMutation({
    mutationFn: async (memberId: string) => {
      try {
        await requests.removeMember(api, householdId, memberId, uncertain.current.has(memberId));
        uncertain.current.delete(memberId);
      } catch (error) {
        if (requests.mayHaveReachedServer(error)) {
          uncertain.current.add(memberId);
        }
        throw error;
      }
    },
    onSettled: () => rosterChanged(queryClient, householdId),
  });
}

/**
 * Confirms the roster. Repeating a confirmation is safe: the server keeps the assignments it already made for the
 * wave and never duplicates them (D-35), so a retry after any failure sends the same request again. The answer is
 * kept for the assignments screen; refetching the rest may fail without undoing the confirmation.
 */
export function useConfirmRoster(householdId: string) {
  const api = useParticipantApi();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.confirmRoster(householdId),
    onSuccess: (confirmation) => {
      queryClient.setQueryData(participantKeys.rosterConfirmation(householdId), confirmation);
      void invalidateParticipantData(queryClient);
    },
    // A refusal (for example a missing detail) means the copy on screen may be out of date.
    onError: () => void invalidateParticipantData(queryClient),
  });
}
