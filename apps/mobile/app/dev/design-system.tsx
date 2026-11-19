import { zodResolver } from '@hookform/resolvers/zod';
import { Redirect, useRouter } from 'expo-router';
import { useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { StyleSheet, View } from 'react-native';
import { z } from 'zod';

import { LanguageSwitcher } from '@/components/LanguageSwitcher';
import { AppButton } from '@/design-system/AppButton';
import { ButtonRow } from '@/design-system/ButtonRow';
import { AppCard } from '@/design-system/AppCard';
import { AppHeader } from '@/design-system/AppHeader';
import { AppInput } from '@/design-system/AppInput';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';
import { EmptyState } from '@/design-system/EmptyState';
import { ErrorState } from '@/design-system/ErrorState';
import { FormField } from '@/design-system/FormField';
import { Icon, type IconName } from '@/design-system/Icon';
import { LikertScale } from '@/design-system/LikertScale';
import { ListGroup, ListRow } from '@/design-system/ListGroup';
import { LoadingState } from '@/design-system/LoadingState';
import { Notice } from '@/design-system/Notice';
import { ProgressIndicator } from '@/design-system/ProgressIndicator';
import { RadioCardGroup } from '@/design-system/RadioCardGroup';
import { Screen } from '@/design-system/Screen';
import { Section } from '@/design-system/Section';
import { SwitchField } from '@/design-system/SwitchField';
import { colors, radii, spacing, statusColors } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

/** Every component in the current language and direction, with live state. Development builds only. */
export default function DesignSystemScreen() {
  if (!__DEV__) {
    return <Redirect href="/" />;
  }
  return <DesignSystemGallery />;
}

const SWATCHES: { name: string; value: string }[] = [
  { name: 'primary', value: colors.primary },
  { name: 'primarySoft', value: colors.primarySoft },
  { name: 'secondary', value: colors.secondary },
  { name: 'accent', value: colors.accent },
  { name: 'background', value: colors.background },
  { name: 'surfaceMuted', value: colors.surfaceMuted },
  { name: 'text', value: colors.text },
  { name: 'borderStrong', value: colors.borderStrong },
  { name: 'success', value: statusColors.success },
  { name: 'warning', value: statusColors.warning },
  { name: 'danger', value: statusColors.danger },
];

const ICONS: IconName[] = [
  'chevron-back',
  'chevron-forward',
  'check',
  'alert',
  'info',
  'plus',
  'close',
  'dot',
  'menu',
  'home',
  'chat',
  'mic',
  'people',
  'heart',
  'grid',
  'settings',
  'sign-out',
];

function DesignSystemGallery() {
  const { t, formatNumber } = useLocalization();
  const router = useRouter();
  const [agreement, setAgreement] = useState<number | null>(null);
  const [frequency, setFrequency] = useState<number | null>(null);
  const [choice, setChoice] = useState<string | null>('b');
  const [toggle, setToggle] = useState(true);
  const [step, setStep] = useState(3);
  const [dialog, setDialog] = useState(false);
  const [saved, setSaved] = useState<string | null>(null);
  const schema = useMemo(
    () => z.object({ name: z.string().trim().min(1, t('designSystem.nameRequired')) }),
    [t],
  );
  const { control, handleSubmit } = useForm({ resolver: zodResolver(schema), defaultValues: { name: '' } });
  const agreementOptions = [1, 2, 3, 4].map((value) => ({ value, label: t(`designSystem.agreement.${value}`) }));
  const frequencyOptions = [
    ...[1, 2, 3, 4, 5].map((value) => ({ value, label: t(`designSystem.frequency.${value}`) })),
    { value: 99, label: t('designSystem.outsideScale'), nonSubstantive: true },
  ];

  return (
    <Screen
      header={
        <AppHeader
          title={t('designSystem.title')}
          onBack={() => (router.canGoBack() ? router.back() : router.replace('/'))}
          backLabel={t('common.back')}
          action={{ label: t('designSystem.headerAction'), onPress: () => setDialog(true) }}
        />
      }
    >
      <Notice tone="info" message={t('designSystem.intro')} />
      <LanguageSwitcher variant="choice" />

      <Section title={t('designSystem.colours')}>
        <View style={styles.swatches}>
          {SWATCHES.map((swatch) => (
            <View key={swatch.name} style={styles.swatch}>
              <View style={[styles.swatchColour, { backgroundColor: swatch.value }]} />
              <AppText variant="caption">{swatch.name}</AppText>
            </View>
          ))}
        </View>
      </Section>

      <Section title={t('designSystem.typography')}>
        <AppText variant="display">{t('app.name')}</AppText>
        <AppText variant="title">{t('app.meaning')}</AppText>
        <AppText variant="heading">{t('designSystem.sampleHeading')}</AppText>
        <AppText>{t('app.tagline')}</AppText>
        <AppText variant="bodyStrong">{t('designSystem.sampleStrong')}</AppText>
        <AppText variant="label" tone="secondary">
          {t('designSystem.sampleLabel')}
        </AppText>
        <AppText variant="caption" tone="muted">
          {t('designSystem.sampleCaption', { number: formatNumber(1234.5) })}
        </AppText>
      </Section>

      <Section title={t('designSystem.buttons')}>
        <AppButton label={t('designSystem.primary')} onPress={() => undefined} />
        <AppButton label={t('designSystem.secondary')} variant="secondary" onPress={() => undefined} />
        <AppButton label={t('designSystem.tertiary')} variant="tertiary" onPress={() => undefined} />
        <AppButton label={t('designSystem.danger')} variant="danger" onPress={() => setDialog(true)} />
        <AppButton label={t('designSystem.loading')} loading />
        <AppButton label={t('designSystem.disabled')} disabled />
        <AppButton label={t('common.next')} trailingIcon="chevron-forward" onPress={() => undefined} />
      </Section>

      <Section title={t('designSystem.inputs')}>
        <FormField control={control} name="name" label={t('designSystem.nameLabel')} hint={t('designSystem.nameHint')} />
        <AppButton label={t('designSystem.save')} onPress={() => void handleSubmit((values) => setSaved(values.name))()} />
        {saved ? <Notice tone="success" message={t('designSystem.saved', { name: saved })} /> : null}
        <AppInput label={t('designSystem.errorInput')} error={t('designSystem.nameRequired')} />
        <AppInput label={t('designSystem.disabledInput')} value={t('designSystem.disabled')} disabled />
        <SwitchField
          label={t('designSystem.switchLabel')}
          description={t('designSystem.switchHint')}
          value={toggle}
          onValueChange={setToggle}
        />
      </Section>

      <Section title={t('designSystem.cards')}>
        <AppCard>
          <AppText>{t('designSystem.cardDefault')}</AppText>
        </AppCard>
        <AppCard variant="muted">
          <AppText>{t('designSystem.cardMuted')}</AppText>
        </AppCard>
        <AppCard variant="highlight">
          <AppText>{t('designSystem.cardHighlight')}</AppText>
        </AppCard>
        <AppCard onPress={() => undefined} accessibilityLabel={t('designSystem.cardPressable')}>
          <AppText variant="bodyStrong">{t('designSystem.cardPressable')}</AppText>
        </AppCard>
        <ListGroup>
          <ListRow title={t('history.title')} description={t('designSystem.cardPressable')} onPress={() => undefined} />
          <ListRow title={t('help.title')} icon="info" onPress={() => undefined} />
        </ListGroup>
      </Section>

      <Section title={t('designSystem.status')}>
        <View style={styles.badges}>
          <Badge label={t('assignmentStatus.assigned')} />
          <Badge tone="info" label={t('assignmentStatus.in_progress')} />
          <Badge tone="success" label={t('assignmentStatus.completed')} />
          <Badge tone="warning" label={t('assignmentStatus.expired')} />
          <Badge tone="danger" label={t('designSystem.dangerBadge')} />
        </View>
        <Notice tone="info" message={t('designSystem.noticeInfo')} />
        <Notice tone="success" message={t('designSystem.noticeSuccess')} />
        <Notice tone="warning" title={t('placeholder.title')} message={t('designSystem.noticeWarning')} />
        <Notice tone="danger" message={t('designSystem.noticeDanger')} />
        <View style={styles.icons}>
          {ICONS.map((name) => (
            <View key={name} style={styles.icon}>
              <Icon name={name} color={colors.primary} />
            </View>
          ))}
        </View>
      </Section>

      <Section title={t('designSystem.progress')}>
        <ProgressIndicator
          current={step}
          total={10}
          label={t('session.progress', { current: formatNumber(step), total: formatNumber(10) })}
        />
        <ButtonRow>
          <AppButton
            label={t('common.previous')}
            variant="secondary"
            onPress={() => setStep((current) => Math.max(current - 1, 0))}
          />
          <AppButton label={t('common.next')} onPress={() => setStep((current) => Math.min(current + 1, 10))} />
        </ButtonRow>
      </Section>

      <Section title={t('designSystem.selection')}>
        <RadioCardGroup
          label={t('designSystem.radioLabel')}
          options={[
            { value: 'a', label: t('designSystem.radioA'), description: t('designSystem.radioHint') },
            { value: 'b', label: t('designSystem.radioB') },
            { value: 'c', label: t('designSystem.radioC') },
          ]}
          value={choice}
          onChange={setChoice}
        />
        <LikertScale
          label={t('designSystem.agreementQuestion')}
          options={agreementOptions}
          value={agreement}
          onChange={setAgreement}
        />
        <LikertScale
          label={t('designSystem.frequencyQuestion')}
          options={frequencyOptions}
          value={frequency}
          onChange={setFrequency}
        />
      </Section>

      <Section title={t('designSystem.states')}>
        <AppCard>
          <LoadingState label={t('common.loading')} />
        </AppCard>
        <AppCard>
          <EmptyState
            title={t('designSystem.emptyTitle')}
            message={t('designSystem.emptyMessage')}
            action={{ label: t('designSystem.primary'), onPress: () => undefined }}
          />
        </AppCard>
        <AppCard>
          <ErrorState
            title={t('errors.kinds.network.title')}
            message={t('errors.kinds.network.message')}
            retry={{ label: t('common.retry'), onPress: () => undefined }}
          />
        </AppCard>
      </Section>

      <ConfirmationDialog
        visible={dialog}
        destructive
        title={t('designSystem.dialogTitle')}
        message={t('designSystem.dialogMessage')}
        confirmLabel={t('designSystem.danger')}
        cancelLabel={t('common.cancel')}
        onConfirm={() => setDialog(false)}
        onCancel={() => setDialog(false)}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  swatches: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  swatch: { width: 88, gap: spacing.xs },
  swatchColour: { height: 48, borderRadius: radii.md, borderWidth: 1, borderColor: colors.border },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  icons: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  icon: { padding: spacing.sm, borderRadius: radii.md, backgroundColor: colors.surface },
});
