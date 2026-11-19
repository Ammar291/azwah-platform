import { useRouter } from 'expo-router';

import { useAssignments, useMe } from '@/api/participant/queries';
import type { Assignment } from '@/api/participant/schemas';
import { AssessmentCard } from '@/components/AssessmentCard';
import { ASSIGNMENT_STATUS_VIEW } from '@/components/assignmentStatus';
import { QueryView } from '@/components/QueryView';
import { EmptyState } from '@/design-system/EmptyState';
import { Section } from '@/design-system/Section';
import { useLocalization } from '@/i18n/useLocalization';

export interface AssessmentListProps {
  /** Only the questionnaires still to answer; "Your family resilience" lists the sent ones by their results. */
  openOnly?: boolean;
}

/** Everything assigned to the person (and to relatives they answer for), open ones first. */
export function AssessmentList({ openOnly = false }: AssessmentListProps) {
  const { t } = useLocalization();
  const assignments = useAssignments();
  return (
    <QueryView query={assignments} loadingLabel={t('assessments.loading')}>
      {(data) => (data.length === 0 ? <NoAssessments /> : <Groups assignments={data} openOnly={openOnly} />)}
    </QueryView>
  );
}

function Groups({ assignments, openOnly }: { assignments: Assignment[]; openOnly: boolean }) {
  const { t } = useLocalization();
  const router = useRouter();
  const open = assignments.filter((assignment) => ASSIGNMENT_STATUS_VIEW[assignment.status].open);
  const closed = assignments.filter((assignment) => !ASSIGNMENT_STATUS_VIEW[assignment.status].open);
  const card = (assignment: Assignment) => (
    <AssessmentCard
      key={assignment.id}
      assignment={assignment}
      onPress={() => router.push({ pathname: '/assessments/[assignmentId]', params: { assignmentId: assignment.id } })}
    />
  );
  return (
    <>
      <Section title={t('assessments.toDo')}>
        {open.length > 0 ? open.map(card) : <EmptyState title={t('assessments.allDone')} />}
      </Section>
      {closed.length > 0 && !openOnly ? <Section title={t('assessments.finished')}>{closed.map(card)}</Section> : null}
    </>
  );
}

function NoAssessments() {
  const { t } = useLocalization();
  const router = useRouter();
  const me = useMe();
  const household = me.data?.household;
  return (
    <EmptyState
      title={t('assessments.none.title')}
      message={t('assessments.none.message')}
      action={
        household
          ? {
              label: t('home.household.open'),
              onPress: () =>
                router.push({ pathname: '/household/[householdId]', params: { householdId: household.householdId } }),
            }
          : undefined
      }
    />
  );
}
