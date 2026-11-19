import { useRouter } from 'expo-router';

import { useAssignments, useRosterConfirmation } from '@/api/participant/queries';
import type { Assignment, AssignmentStatus, Member, RosterConfirmation } from '@/api/participant/schemas';
import { AssessmentCard } from '@/components/AssessmentCard';
import { AppScreen } from '@/components/AppScreen';
import { ASSIGNMENT_STATUS_VIEW } from '@/components/assignmentStatus';
import { ProblemState } from '@/components/ProblemState';
import { QueryView } from '@/components/QueryView';
import { roundOf } from '@/components/roundLabel';
import { useLeaveFlow } from '@/components/useLeaveFlow';
import { useRouteParam } from '@/components/useRouteParam';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { EmptyState } from '@/design-system/EmptyState';
import { Notice } from '@/design-system/Notice';
import { Section } from '@/design-system/Section';
import { useLocalization } from '@/i18n/useLocalization';

import { memberTitle } from './memberLabels';
import { useRosterData } from './useRosterData';

/** One assignment to show: what the server assigned, with its details when the assignment list has them. */
interface AssignmentRow {
  assignmentId: string;
  memberId: string;
  status: AssignmentStatus;
  details: Assignment | null;
}

/**
 * The questionnaires the server assigned to the household, grouped by member: one member may have several, and there
 * may be none. Straight after confirming, the server's own answer is shown even if refreshing the rest fails. Display
 * only: nothing here can add, remove or change an assignment, and no answers are shown.
 */
export function HouseholdAssignments() {
  const { t } = useLocalization();
  const router = useRouter();
  const leaveFlow = useLeaveFlow();
  const householdId = useRouteParam('householdId') ?? '';
  const roster = useRosterData(householdId);
  const confirmation = useRosterConfirmation(householdId).data;
  const assignments = useAssignments();

  return (
    <AppScreen
      title={t('household.assignments.title')}
      footer={
        <AppButton
          label={t('household.assignments.done')}
          variant="secondary"
          // The set-up screens under this one are finished with: Back from the household goes Home, not through them.
          onPress={() => leaveFlow({ pathname: '/household/[householdId]', params: { householdId } })}
        />
      }
    >
      {confirmation ? (
        <ConfirmedNow
          confirmation={confirmation}
          members={roster.data?.members ?? null}
          assignments={assignments.data ?? null}
          detailsPending={assignments.isPending}
          refreshProblem={assignments.error ?? roster.error}
          onRetry={() => void Promise.all([assignments.refetch(), roster.refetch()])}
        />
      ) : (
        <QueryView query={roster} loadingLabel={t('household.assignments.loading')}>
          {(data) =>
            data.household.rosterConfirmedAt ? (
              <QueryView query={assignments} loadingLabel={t('household.assignments.loading')}>
                {(list) => <CurrentAssignments assignments={list} members={data.members} />}
              </QueryView>
            ) : (
              <Notice
                tone="warning"
                title={t('household.assignments.notConfirmed.title')}
                message={t('household.assignments.notConfirmed.message')}
                action={{
                  label: t('household.assignments.notConfirmed.action'),
                  onPress: () => router.push({ pathname: '/household/[householdId]/review', params: { householdId } }),
                }}
              />
            )
          }
        </QueryView>
      )}
    </AppScreen>
  );
}

function ConfirmedNow({
  confirmation,
  members,
  assignments,
  detailsPending,
  refreshProblem,
  onRetry,
}: {
  confirmation: RosterConfirmation;
  members: Member[] | null;
  assignments: Assignment[] | null;
  detailsPending: boolean;
  refreshProblem: unknown;
  onRetry: () => void;
}) {
  const { t, formatDate } = useLocalization();
  const byId = new Map((assignments ?? []).map((assignment) => [assignment.id, assignment]));
  const rows = confirmation.assignments.map((assignment) => ({
    assignmentId: assignment.assignmentId,
    memberId: assignment.memberId,
    status: assignment.status,
    details: byId.get(assignment.assignmentId) ?? null,
  }));

  return (
    <>
      <Notice
        tone="success"
        title={t('household.assignments.confirmedTitle')}
        message={t('household.confirmedOn', { date: formatDate(confirmation.rosterConfirmedAt) })}
        announce
      />
      {refreshProblem ? (
        <>
          <AppText tone="secondary">{t('household.assignments.refreshFailed')}</AppText>
          <ProblemState error={refreshProblem} variant="inline" onRetry={onRetry} />
        </>
      ) : null}
      {confirmation.waveId === null ? (
        <EmptyState
          title={t('household.assignments.noWave.title')}
          message={t('household.assignments.noWave.message')}
        />
      ) : rows.length === 0 ? (
        <EmptyState
          title={t('household.assignments.noneEligible.title')}
          message={t('household.assignments.noneEligible.message', { wave: roundOf(confirmation.waveCode ?? '') })}
        />
      ) : (
        <AssignmentGroups rows={rows} members={members} detailsPending={detailsPending} />
      )}
      <AppText tone="secondary">{t('household.confirm.decides')}</AppText>
    </>
  );
}

function CurrentAssignments({ assignments, members }: { assignments: Assignment[]; members: Member[] }) {
  const { t } = useLocalization();
  if (assignments.length === 0) {
    return <EmptyState title={t('household.assignments.none.title')} message={t('household.assignments.none.message')} />;
  }
  const open = (assignment: Assignment) => (ASSIGNMENT_STATUS_VIEW[assignment.status].open ? 0 : 1);
  const rows = [...assignments]
    .sort((a, b) => open(a) - open(b))
    .map((assignment) => ({
      assignmentId: assignment.id,
      memberId: assignment.memberId,
      status: assignment.status,
      details: assignment,
    }));
  return (
    <>
      <AssignmentGroups rows={rows} members={members} detailsPending={false} />
      <AppText tone="secondary">{t('household.confirm.decides')}</AppText>
    </>
  );
}

function AssignmentGroups({
  rows,
  members,
  detailsPending,
}: {
  rows: AssignmentRow[];
  members: Member[] | null;
  detailsPending: boolean;
}) {
  const { t, formatNumber } = useLocalization();
  const router = useRouter();
  // Members in roster order, then any the roster could not name.
  const order = [...(members ?? []).map((member) => member.id), ...rows.map((row) => row.memberId)];
  const memberIds = order.filter((id, index) => order.indexOf(id) === index && rows.some((row) => row.memberId === id));

  return (
    <>
      {memberIds.map((memberId) => {
        const member = members?.find((candidate) => candidate.id === memberId) ?? null;
        const own = rows.filter((row) => row.memberId === memberId);
        return (
          <Section
            key={memberId}
            title={member ? memberTitle(member, t) : t('household.assignments.unknownMember')}
            description={t('household.assignments.count', { count: formatNumber(own.length) })}
          >
            {own.map((row) =>
              row.details ? (
                <AssessmentCard
                  key={row.assignmentId}
                  assignment={row.details}
                  onPress={() =>
                    router.push({ pathname: '/assessments/[assignmentId]', params: { assignmentId: row.assignmentId } })
                  }
                />
              ) : (
                <WithoutDetails key={row.assignmentId} row={row} member={member} pending={detailsPending} />
              ),
            )}
          </Section>
        );
      })}
    </>
  );
}

/**
 * An assignment whose details the app cannot show: it belongs to a member who answers in their own account (the
 * manager never sees it), or the list could not be loaded. Only its status is shown.
 */
function WithoutDetails({ row, member, pending }: { row: AssignmentRow; member: Member | null; pending: boolean }) {
  const { t } = useLocalization();
  const status = ASSIGNMENT_STATUS_VIEW[row.status];
  const ownAccount = member !== null && member.hasAccount && !member.isCaller;
  return (
    <AppCard variant="muted" testID={`assignment-${row.assignmentId}`}>
      <Badge label={t(status.labelKey)} tone={status.tone} />
      <AppText tone="secondary">
        {ownAccount
          ? t('household.assignments.ownAccount')
          : pending
            ? t('common.loading')
            : t('household.assignments.detailsUnavailable')}
      </AppText>
    </AppCard>
  );
}
