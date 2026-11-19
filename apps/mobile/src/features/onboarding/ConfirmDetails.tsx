import { useRouter } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { isApiError } from '@/api/client';
import { useAuth } from '@/auth/AuthContext';
import { fullName, initials, type IdentityProfile } from '@/auth/identityProfile';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge, type BadgeTone } from '@/design-system/Badge';
import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';
import { DetailRow } from '@/design-system/DetailRow';
import { Icon } from '@/design-system/Icon';
import { Notice } from '@/design-system/Notice';
import { ProgressIndicator } from '@/design-system/ProgressIndicator';
import { Screen } from '@/design-system/Screen';
import { SwitchField } from '@/design-system/SwitchField';
import { borderWidths, colors, radii, spacing } from '@/design-system/tokens';
import { formatDate } from '@/i18n/format';
import { useLocalization } from '@/i18n/useLocalization';

import { useOnboarding } from './OnboardingContext';
import { useRegisterMe } from './registration';

/**
 * Onboarding step 1 of 2 (the "Confirm your details" Stitch design): the details UAE PASS released, for the person to
 * confirm once (D-97). Confirming registers them; nothing about the details is sent or kept, which the screen says.
 * "These aren't my details" explains and signs out, so someone on a shared phone can hand it back. A provider that
 * released no details still lets the person confirm it is them.
 */
export function ConfirmDetails() {
  const { t } = useLocalization();
  const { state, provider } = useAuth();
  const onboarding = useOnboarding();
  const router = useRouter();
  const register = useRegisterMe({
    onRegistered: () => {
      onboarding.beginFinishing();
      router.replace('/account-ready');
    },
  });
  const [notMine, setNotMine] = useState(false);
  const [signingOut, setSigningOut] = useState(false);
  const [signOutFailed, setSignOutFailed] = useState(false);
  const profile = state.status === 'signed_in' ? state.session.profile : null;
  // The agreement belongs to the details on screen: if they arrive or change (a renewal after a restart), it resets.
  const shown = JSON.stringify(profile);
  const [confirmedFor, setConfirmedFor] = useState<string | null>(null);
  const confirmed = confirmedFor === shown;
  const setConfirmed = (value: boolean) => setConfirmedFor(value ? shown : null);

  const submit = () => {
    if (!confirmed || register.isPending) return;
    register.mutate();
  };

  // A sign-out that fails says so in the dialog, which stays open: the person is still signed in and can try again.
  const signOut = async () => {
    setSigningOut(true);
    setSignOutFailed(false);
    try {
      await provider.signOut();
      setNotMine(false);
    } catch {
      setSignOutFailed(true);
    } finally {
      setSigningOut(false);
    }
  };

  const failure = register.error
    ? isApiError(register.error) && register.error.kind === 'network'
      ? 'network'
      : 'unexpected'
    : null;

  return (
    <Screen
      footer={
        <View style={styles.footer}>
          <AppButton
            label={t('onboarding.confirm.action')}
            trailingIcon="chevron-forward"
            disabled={!confirmed}
            loading={register.isPending}
            onPress={submit}
          />
          <AppButton variant="tertiary" label={t('onboarding.confirm.notMine')} onPress={() => setNotMine(true)} />
        </View>
      }
    >
      <ProgressIndicator current={1} total={2} label={t('onboarding.step', { current: 1, total: 2 })} />
      <View style={styles.intro}>
        <AppText variant="title" accessibilityRole="header">
          {t('onboarding.confirm.title')}
        </AppText>
        <AppText tone="secondary">
          {t(profile ? 'onboarding.confirm.intro' : 'onboarding.confirm.introNoDetails')}
        </AppText>
      </View>

      {profile ? <ProfileCard profile={profile} /> : null}

      <View style={styles.privacy}>
        <Icon name="lock" size={18} color={colors.primary} />
        <AppText variant="label" tone="secondary" style={styles.grow}>
          {t('onboarding.confirm.privacy')}
        </AppText>
      </View>

      {failure ? (
        <Notice
          tone="danger"
          title={t(`onboarding.confirm.failures.${failure}.title`)}
          message={t(`onboarding.confirm.failures.${failure}.message`)}
          announce
        />
      ) : null}

      <SwitchField
        label={t(profile ? 'onboarding.confirm.agree' : 'onboarding.confirm.agreeNoDetails')}
        value={confirmed}
        onValueChange={setConfirmed}
      />

      <ConfirmationDialog
        visible={notMine}
        title={t('onboarding.confirm.notMineDialog.title')}
        message={t('onboarding.confirm.notMineDialog.message')}
        confirmLabel={t('onboarding.confirm.notMineDialog.signOut')}
        cancelLabel={t('common.cancel')}
        onConfirm={() => void signOut()}
        onCancel={() => {
          setSignOutFailed(false);
          setNotMine(false);
        }}
        busy={signingOut}
        problem={
          signOutFailed
            ? { title: t('errors.kinds.unexpected.title'), message: t('errors.kinds.unexpected.message') }
            : null
        }
        destructive
      />
    </Screen>
  );
}

/**
 * Only details that came from UAE PASS get the success colour (an identity the state checked); a Google account's are
 * neutral (D-147); simulated ones are flagged, unknown ones are neutral.
 */
const SOURCE_TONE: Record<IdentityProfile['source'], BadgeTone> = {
  uae_pass: 'success',
  uae_pass_staging: 'success',
  google: 'neutral',
  simulated: 'warning',
  unverified: 'neutral',
};

const SOURCE_LABEL: Record<Exclude<IdentityProfile['source'], 'simulated'>, string> = {
  uae_pass: 'onboarding.confirm.source.uae_pass',
  uae_pass_staging: 'onboarding.confirm.source.uae_pass',
  google: 'onboarding.confirm.source.google',
  unverified: 'onboarding.confirm.source.unverified',
};

function ProfileCard({ profile }: { profile: IdentityProfile }) {
  const { t, language } = useLocalization();
  const primaryName = fullName(profile, language);
  const otherName = language === 'ar' ? profile.nameEn : profile.nameAr;
  const rows: [string, string | null][] = [
    [t('onboarding.confirm.fields.emiratesId'), profile.emiratesIdMasked],
    [t('onboarding.confirm.fields.nationality'), language === 'ar' ? (profile.nationalityAr ?? profile.nationalityEn) : (profile.nationalityEn ?? profile.nationalityAr)],
    [t('onboarding.confirm.fields.birthdate'), profile.birthdate ? formatDate(profile.birthdate, language, 'date', 'UTC') : null],
    [t('onboarding.confirm.fields.gender'), profile.gender ? t(`onboarding.confirm.gender.${profile.gender}`) : null],
    [t('onboarding.confirm.fields.mobile'), profile.mobileMasked],
    [t('onboarding.confirm.fields.email'), profile.emailMasked],
  ];
  const shown = rows.filter((row): row is [string, string] => row[1] !== null);

  return (
    <AppCard>
      <View style={styles.identity}>
        <View style={styles.avatar} accessible={false}>
          <AppText variant="heading" tone="primary">
            {initials(profile) ?? ''}
          </AppText>
        </View>
        <View style={styles.grow}>
          {primaryName ? <AppText variant="bodyStrong">{primaryName}</AppText> : null}
          {otherName && otherName !== primaryName ? (
            <AppText variant="label" tone="secondary">
              {otherName}
            </AppText>
          ) : null}
        </View>
      </View>
      {/* No "simulated" source at all, and UAE PASS without "(staging)" (D-142). */}
      {profile.source === 'simulated' ? null : (
        <View style={styles.source}>
          <AppText variant="label" tone="secondary">
            {t('onboarding.confirm.fields.source')}
          </AppText>
          <Badge tone={SOURCE_TONE[profile.source]} label={t(SOURCE_LABEL[profile.source])} />
        </View>
      )}
      {shown.map(([label, value]) => (
        <View key={label} style={styles.row}>
          <DetailRow layout="inline" label={label} value={value} />
        </View>
      ))}
    </AppCard>
  );
}

const styles = StyleSheet.create({
  footer: { gap: spacing.xs },
  intro: { gap: spacing.sm },
  identity: { flexDirection: 'row', alignItems: 'center', gap: spacing.lg, paddingBottom: spacing.md },
  avatar: {
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: colors.primarySoft,
    borderWidth: borderWidths.hairline,
    borderColor: colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  grow: { flex: 1 },
  source: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
    paddingVertical: spacing.md,
    borderTopWidth: borderWidths.hairline,
    borderTopColor: colors.borderSoft,
  },
  row: { paddingVertical: spacing.md, borderTopWidth: borderWidths.hairline, borderTopColor: colors.borderSoft },
  privacy: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    padding: spacing.lg,
    borderRadius: radii.lg,
    backgroundColor: colors.surfaceMuted,
    borderWidth: borderWidths.hairline,
    borderColor: colors.accentSoft,
  },
});
