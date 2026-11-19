import { StyleSheet, View } from 'react-native';

import { AppButton } from '@/design-system/AppButton';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { Icon } from '@/design-system/Icon';
import { borderWidths, colors, radii, spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import type { FamilyPathAction, FamilyPathStep } from './familyPath';

export interface FamilyPathCardProps {
  steps: readonly FamilyPathStep[];
  onAction: (action: FamilyPathAction) => void;
  /** Shown with the check-in step whenever the family has any questionnaire. */
  onSeeAllAssignments: () => void;
}

const MARKER = 32;

/** The family's way through Azwah as a trail of steps: done, the one to do now, and what comes later. */
export function FamilyPathCard({ steps, onAction, onSeeAllAssignments }: FamilyPathCardProps) {
  const { t } = useLocalization();
  return (
    <View testID="family-path" style={styles.path}>
      <View style={styles.heading}>
        <AppText variant="heading" accessibilityRole="header">
          {t('home.path.title')}
        </AppText>
      </View>
      <View>
        {steps.map((step, index) => (
          <PathStep
            key={step.id}
            step={step}
            number={index + 1}
            total={steps.length}
            last={index === steps.length - 1}
            onAction={onAction}
            onSeeAllAssignments={onSeeAllAssignments}
          />
        ))}
      </View>
    </View>
  );
}

interface PathStepProps extends Omit<FamilyPathCardProps, 'steps'> {
  step: FamilyPathStep;
  number: number;
  total: number;
  last: boolean;
}

function PathStep({ step, number, total, last, onAction, onSeeAllAssignments }: PathStepProps) {
  const { t } = useLocalization();
  const title = t(`home.path.steps.${step.id}.title`);
  // Only the family survey says more than its title, and only until it is done: the next steps come from it (D-105).
  const body = step.id === 'checkIn' && step.state !== 'done' ? t('home.path.steps.checkIn.body') : null;
  const stateLabel = t(`home.path.state.${step.state}`);
  const current = step.state === 'current';
  const openCount = step.openCount ?? 0;
  const waiting = current && step.id === 'checkIn' && openCount === 0;

  return (
    <View style={styles.step} testID={`path-step-${step.id}`}>
      <View style={styles.rail}>
        <Marker state={step.state} />
        {last ? null : <View style={[styles.connector, step.state === 'done' ? styles.connectorDone : null]} />}
      </View>
      <View style={[styles.content, last ? null : styles.contentSpaced, current ? styles.currentPanel : null]}>
        <View
          accessible
          accessibilityLabel={[t('home.path.stepOf', { number, total }), title, stateLabel, body].filter(Boolean).join('. ')}
          style={styles.words}
        >
          <AppText variant="bodyStrong" tone={step.state === 'upcoming' ? 'secondary' : 'default'}>
            {title}
          </AppText>
          {body ? <AppText tone={step.state === 'upcoming' ? 'muted' : 'secondary'}>{body}</AppText> : null}
        </View>
        {step.state === 'done' ? <Badge tone="success" label={stateLabel} /> : null}
        {current && step.id === 'checkIn' && openCount > 0 ? (
          <AppText variant="label" tone="primary">
            {t('home.path.steps.checkIn.open', { count: openCount })}
          </AppText>
        ) : null}
        {waiting ? <AppText tone="secondary">{t('home.path.steps.checkIn.waiting')}</AppText> : null}
        {step.action ? <ActionButton action={step.action} onAction={onAction} /> : null}
        {/* Always there once the family has any questionnaire: only the current step carries the action button. */}
        {step.id === 'checkIn' && (step.assignmentCount ?? 0) > 0 ? (
          <AppButton label={t('home.assessments.seeAll')} variant="tertiary" onPress={onSeeAllAssignments} />
        ) : null}
      </View>
    </View>
  );
}

interface ActionButtonProps {
  action: FamilyPathAction;
  onAction: (action: FamilyPathAction) => void;
}

function ActionButton({ action, onAction }: ActionButtonProps) {
  const { t } = useLocalization();
  const label =
    action.kind === 'openAssignment'
      ? t(action.started ? 'home.path.actions.continue' : 'home.path.actions.start')
      : t(`home.path.actions.${action.kind}`);
  return <AppButton label={label} trailingIcon="chevron-forward" onPress={() => onAction(action)} />;
}

function Marker({ state }: { state: FamilyPathStep['state'] }) {
  if (state === 'done') {
    return (
      <View style={[styles.marker, styles.markerDone]}>
        <Icon name="check" size={18} color={colors.onPrimary} />
      </View>
    );
  }
  return (
    <View style={[styles.marker, state === 'current' ? styles.markerCurrent : styles.markerUpcoming]}>
      {state === 'current' ? <Icon name="dot" size={22} color={colors.primary} /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  path: { gap: spacing.lg },
  heading: { gap: spacing.xxs },
  step: { flexDirection: 'row', gap: spacing.md },
  rail: { width: MARKER, alignItems: 'center' },
  marker: {
    width: MARKER,
    height: MARKER,
    borderRadius: MARKER / 2,
    alignItems: 'center',
    justifyContent: 'center',
  },
  markerDone: { backgroundColor: colors.primary },
  markerCurrent: { backgroundColor: colors.surface, borderWidth: borderWidths.strong, borderColor: colors.primary },
  markerUpcoming: { backgroundColor: colors.surface, borderWidth: borderWidths.strong, borderColor: colors.border },
  connector: {
    flex: 1,
    width: borderWidths.strong,
    minHeight: spacing.lg,
    marginVertical: spacing.xs,
    backgroundColor: colors.border,
  },
  connectorDone: { backgroundColor: colors.primary },
  content: { flex: 1, gap: spacing.sm, paddingTop: spacing.xs },
  contentSpaced: { marginBottom: spacing.lg },
  // The step to do now is the one raised surface on the path.
  currentPanel: {
    padding: spacing.lg,
    gap: spacing.md,
    borderRadius: radii.lg,
    borderWidth: borderWidths.hairline,
    borderColor: colors.borderSoft,
    backgroundColor: colors.surface,
  },
  words: { gap: spacing.xxs },
});
