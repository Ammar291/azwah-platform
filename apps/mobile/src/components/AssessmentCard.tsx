import type { Assignment } from '@/api/participant/schemas';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { useLocalization } from '@/i18n/useLocalization';

import { ASSIGNMENT_STATUS_VIEW } from './assignmentStatus';
import { roundOf } from './roundLabel';

export interface AssessmentCardProps {
  assignment: Assignment;
  onPress: () => void;
}

/** One assigned questionnaire: its name and section from the server, its status in words and a glyph, and its due date. */
export function AssessmentCard({ assignment, onPress }: AssessmentCardProps) {
  const { t, pickText, formatDate } = useLocalization();
  const status = ASSIGNMENT_STATUS_VIEW[assignment.status];
  const name = pickText(assignment.instrumentName);
  const section = assignment.sectionTitle ? pickText(assignment.sectionTitle) : null;
  const statusLabel = t(status.labelKey);

  return (
    <AppCard
      onPress={onPress}
      accessibilityLabel={[name, section, statusLabel].filter(Boolean).join(', ')}
      accessibilityHint={t(status.actionKey)}
      testID={`assessment-${assignment.id}`}
    >
      <Badge label={statusLabel} tone={status.tone} />
      <AppText variant="bodyStrong">{name}</AppText>
      {section ? <AppText tone="secondary">{section}</AppText> : null}
      <AppText variant="caption" tone="muted">
        {t('assessments.wave', { wave: roundOf(assignment.waveCode) })}
      </AppText>
      {status.open && assignment.expiresAt ? (
        <AppText variant="caption" tone="muted">
          {t('assessments.dueBy', { date: formatDate(assignment.expiresAt) })}
        </AppText>
      ) : null}
    </AppCard>
  );
}
