import { useHousehold, useMembers } from '@/api/participant/queries';
import type { Household, Member } from '@/api/participant/schemas';
import type { QueryLike } from '@/components/QueryView';

export interface RosterData {
  household: Household;
  members: Member[];
}

/** The household and its roster as one query for QueryView: loading until both arrive, retrying both together. */
export function useRosterData(householdId: string): QueryLike<RosterData> {
  const household = useHousehold(householdId);
  const members = useMembers(householdId);
  return {
    data: household.data && members.data ? { household: household.data, members: members.data } : undefined,
    error: household.error ?? members.error,
    isPending: household.isPending || members.isPending,
    refetch: () => Promise.all([household.refetch(), members.refetch()]),
  };
}
