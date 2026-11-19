import { useRouter } from 'expo-router';

import { useAssignments, useHistory, usePublishedVersion } from '@/api/participant/queries';
import type { Assignment, HistoryItem } from '@/api/participant/schemas';
import { QueryView } from '@/components/QueryView';
import { roundOf } from '@/components/roundLabel';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge, type BadgeTone } from '@/design-system/Badge';
import { EmptyState } from '@/design-system/EmptyState';
import { useLocalization } from '@/i18n/useLocalization';

function resultBadge(item: HistoryItem): { tone: BadgeTone; key: string } {
  if (item.scoreStatus === 'complete') return { tone: 'success', key: 'history.result.complete' };
  if (item.scoreStatus === 'incomplete') return { tone: 'warning', key: 'history.result.incomplete' };
  return { tone: 'info', key: 'history.result.pending' };
}

/** Submitted questionnaires, newest first, each opening its result. */
export function HistoryList() {
  const { t } = useLocalization();
  const router = useRouter();
  const history = useHistory();
  // History rows carry the instrument code only; the assignment gives its name in both languages.
  const assignments = useAssignments();

  return (
    <QueryView query={history} loadingLabel={t('history.loading')}>
      {(items) =>
        items.length === 0 ? (
          <EmptyState
            title={t('history.none.title')}
            message={t('history.none.message')}
            action={{ label: t('home.assessments.seeAll'), onPress: () => router.push('/assessments') }}
          />
        ) : (
          items.map((item) => (
            <HistoryRow
              key={item.sessionId}
              item={item}
              assignment={assignments.data?.find((candidate) => candidate.id === item.assignmentId)}
              // Ask for the version's name only once the assignments are known not to have it.
              nameFromVersion={!assignments.isPending}
            />
          ))
        )
      }
    </QueryView>
  );
}

interface HistoryRowProps {
  item: HistoryItem;
  assignment: Assignment | undefined;
  nameFromVersion: boolean;
}

/**
 * One submitted questionnaire under its own name: the assignment's, or the published version's when the assignment
 * is not in the list. The raw instrument code never stands in for a name; until one is known the row says
 * "Questionnaire".
 */
function HistoryRow({ item, assignment, nameFromVersion }: HistoryRowProps) {
  const { t, pickText, formatDate } = useLocalization();
  const router = useRouter();
  const lookUp = !assignment && nameFromVersion;
  const version = usePublishedVersion(lookUp ? item.instrumentCode : undefined, lookUp ? item.versionNumber : undefined);
  const versionSection = version.data?.sections.find((section) => section.code === item.sectionCode);
  const name = assignment
    ? pickText(assignment.instrumentName)
    : version.data
      ? pickText(version.data.name)
      : t('history.questionnaire');
  const sectionTitle = assignment ? assignment.sectionTitle : (versionSection?.title ?? null);
  const section = sectionTitle ? pickText(sectionTitle) : null;
  const badge = resultBadge(item);
  const submitted = item.submittedAt ? formatDate(item.submittedAt) : '';
  return (
    <AppCard
      onPress={() => router.push({ pathname: '/sessions/[sessionId]/result', params: { sessionId: item.sessionId } })}
      accessibilityLabel={[name, section, submitted, t(badge.key)].filter(Boolean).join(', ')}
      accessibilityHint={t('history.openResult')}
    >
      <Badge tone={badge.tone} label={t(badge.key)} />
      <AppText variant="bodyStrong">{name}</AppText>
      {section ? <AppText tone="secondary">{section}</AppText> : null}
      <AppText variant="caption" tone="muted">
        {t('history.submittedOn', { date: submitted, wave: roundOf(item.waveCode) })}
      </AppText>
    </AppCard>
  );
}
