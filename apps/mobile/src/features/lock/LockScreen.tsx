import { useEffect, useState } from 'react';
import { AccessibilityInfo, Image, StyleSheet, View } from 'react-native';

import { useAuth } from '@/auth/AuthContext';
import { AppButton } from '@/design-system/AppButton';
import { AppText } from '@/design-system/AppText';
import { brand } from '@/design-system/brand';
import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';
import { Notice } from '@/design-system/Notice';
import { Screen } from '@/design-system/Screen';
import { colors, spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';
import { useAppLock } from '@/security/AppLockContext';

type Availability = 'checking' | 'available' | 'unavailable';

/**
 * The only thing on screen while the app is locked: unlock with the phone's own lock (biometrics or passcode), or sign
 * out and sign in again, which deletes answers not yet sent (D-70). Without a phone lock only the second is offered.
 * A cancelled or failed attempt keeps the lock and says so quietly; so does a sign-out that fails. Outcomes are
 * announced with AccessibilityInfo, which VoiceOver and TalkBack both speak (a live region is Android only).
 */
export function LockScreen() {
  const { t } = useLocalization();
  const { provider } = useAuth();
  const { unlock, unlocking, isAuthenticationAvailable } = useAppLock();
  const [availability, setAvailability] = useState<Availability>('checking');
  const [outcome, setOutcome] = useState<'cancelled' | 'failed' | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [signingOut, setSigningOut] = useState(false);
  const [signOutFailed, setSignOutFailed] = useState(false);

  useEffect(() => {
    let current = true;
    void isAuthenticationAvailable().then((available) => {
      if (current) setAvailability(available ? 'available' : 'unavailable');
    });
    return () => {
      current = false;
    };
  }, [isAuthenticationAvailable]);

  const tryUnlock = async () => {
    setOutcome(null);
    setSignOutFailed(false);
    const result = await unlock({ promptMessage: t('lock.prompt'), cancelLabel: t('common.cancel') });
    if (result === 'unavailable') {
      setAvailability('unavailable');
      AccessibilityInfo.announceForAccessibility(t('lock.noDeviceLock.title'));
    } else if (result === 'cancelled' || result === 'failed') {
      setOutcome(result);
      AccessibilityInfo.announceForAccessibility(t(`lock.outcomes.${result}`));
    }
  };

  /** Signing out deletes unsent answers (D-70); if it fails, the lock stays and the person can try again. */
  const signInAgain = async () => {
    setSigningOut(true);
    setSignOutFailed(false);
    try {
      await provider.signOut();
    } catch {
      setSignOutFailed(true);
      AccessibilityInfo.announceForAccessibility(t('lock.signOutFailed'));
    } finally {
      setSigningOut(false);
      setConfirming(false);
    }
  };

  return (
    <View style={[StyleSheet.absoluteFill, styles.layer]} accessibilityViewIsModal testID="lock-screen">
      <Screen>
        <View style={styles.hero}>
          <Image
            source={brand.emblem}
            style={styles.emblem}
            resizeMode="contain"
            accessible
            accessibilityLabel={t(brand.nameKey)}
            accessibilityIgnoresInvertColors
          />
          <AppText variant="title" accessibilityRole="header" style={styles.centred}>
            {t('lock.title')}
          </AppText>
          <AppText tone="secondary" style={styles.centred}>
            {t('lock.message')}
          </AppText>
        </View>

        {availability === 'unavailable' ? (
          <Notice tone="warning" title={t('lock.noDeviceLock.title')} message={t('lock.noDeviceLock.message')} />
        ) : (
          <>
            {outcome ? (
              <AppText tone="secondary" style={styles.centred}>
                {t(`lock.outcomes.${outcome}`)}
              </AppText>
            ) : null}
            <AppButton
              label={t('lock.unlock')}
              loading={availability === 'checking' || unlocking}
              onPress={() => void tryUnlock()}
            />
          </>
        )}

        {signOutFailed ? <Notice tone="danger" message={t('lock.signOutFailed')} /> : null}
        <AppText tone="secondary">{t('lock.signInAgainNote')}</AppText>
        <AppButton
          label={t('lock.signInAgain')}
          variant={availability === 'unavailable' ? 'primary' : 'secondary'}
          disabled={unlocking}
          onPress={() => setConfirming(true)}
        />
      </Screen>

      <ConfirmationDialog
        visible={confirming}
        destructive
        title={t('lock.confirm.title')}
        message={t('lock.confirm.message')}
        confirmLabel={t('lock.confirm.confirm')}
        cancelLabel={t('common.cancel')}
        busy={signingOut}
        onConfirm={() => void signInAgain()}
        onCancel={() => setConfirming(false)}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  layer: { backgroundColor: colors.background },
  hero: { alignItems: 'center', gap: spacing.sm, paddingVertical: spacing.lg },
  // Explicit size: Android ignores aspectRatio on an Image sized by width alone.
  emblem: { width: 112, height: Math.round(112 / brand.emblemAspectRatio), marginBottom: spacing.sm },
  centred: { textAlign: 'center' },
});
