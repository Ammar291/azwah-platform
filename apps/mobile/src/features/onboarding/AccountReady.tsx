import { StyleSheet, View } from 'react-native';

import { useAuth } from '@/auth/AuthContext';
import { givenName } from '@/auth/identityProfile';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Icon } from '@/design-system/Icon';
import { ProgressIndicator } from '@/design-system/ProgressIndicator';
import { Screen } from '@/design-system/Screen';
import { borderWidths, colors, spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { useOnboarding } from './OnboardingContext';

const NEXT_STEPS = ['household', 'consent', 'checkIn'] as const;

/**
 * Onboarding step 2 of 2 (the "Account ready" Stitch design): a welcome by name and what happens next, in the order
 * the app asks for it (household, consent, the first questionnaire). "Go to home" ends onboarding.
 */
export function AccountReady() {
  const { t, language } = useLocalization();
  const { state } = useAuth();
  const onboarding = useOnboarding();
  const name = givenName(state.status === 'signed_in' ? state.session.profile : null, language);

  return (
    <Screen
      footer={<AppButton label={t('onboarding.ready.action')} trailingIcon="chevron-forward" onPress={onboarding.finish} />}
    >
      <ProgressIndicator current={2} total={2} label={t('onboarding.step', { current: 2, total: 2 })} />
      <View style={styles.hero}>
        <View style={styles.halo}>
          {[0, 90, 180, 270].map((angle) => (
            <View key={angle} style={[styles.haloDotArm, { transform: [{ rotate: `${angle}deg` }] }]}>
              <View style={styles.haloDot} />
            </View>
          ))}
          <View style={styles.tick}>
            <Icon name="check" size={40} color={colors.primary} />
          </View>
        </View>
        <AppText variant="caption" tone="secondary" style={[styles.overline, language === 'ar' ? styles.joined : null]}>
          {t('onboarding.ready.overline')}
        </AppText>
        <AppText variant="title" accessibilityRole="header" style={styles.centred}>
          {name ? t('onboarding.ready.titleNamed', { name }) : t('onboarding.ready.title')}
        </AppText>
        <AppText tone="secondary" style={styles.centred}>
          {t('onboarding.ready.message')}
        </AppText>
      </View>

      <AppCard>
        <AppText variant="label" tone="primary" style={[styles.overline, language === 'ar' ? styles.joined : null]}>
          {t('onboarding.ready.nextTitle')}
        </AppText>
        {NEXT_STEPS.map((step, index) => (
          <View key={step} style={[styles.step, index > 0 ? styles.stepDivider : null]}>
            <View style={styles.number}>
              <AppText variant="label" tone="primary">
                {String(index + 1)}
              </AppText>
            </View>
            <View style={styles.grow}>
              <AppText variant="bodyStrong">{t(`onboarding.ready.steps.${step}.title`)}</AppText>
            </View>
          </View>
        ))}
      </AppCard>
    </Screen>
  );
}

const HALO = 136;

const styles = StyleSheet.create({
  hero: { alignItems: 'center', gap: spacing.sm, paddingVertical: spacing.lg },
  halo: {
    width: HALO,
    height: HALO,
    borderRadius: HALO / 2,
    borderWidth: borderWidths.hairline,
    borderColor: colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.md,
  },
  haloDotArm: { position: 'absolute', width: HALO, height: HALO, alignItems: 'center' },
  haloDot: { width: 8, height: 8, borderRadius: 4, marginTop: -4, backgroundColor: colors.accent },
  tick: {
    width: 88,
    height: 88,
    borderRadius: 44,
    backgroundColor: colors.primarySoft,
    borderWidth: 6,
    borderColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  overline: { letterSpacing: 1.5, textTransform: 'uppercase' },
  // Arabic letters join: spacing them apart breaks the words.
  joined: { letterSpacing: 0 },
  centred: { textAlign: 'center' },
  step: { flexDirection: 'row', gap: spacing.md, paddingVertical: spacing.md },
  stepDivider: { borderTopWidth: borderWidths.hairline, borderTopColor: colors.borderSoft },
  number: {
    width: 32,
    height: 32,
    borderRadius: 16,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  grow: { flex: 1 },
});
