import { StyleSheet, View } from 'react-native';

import { useAuth } from '@/auth/AuthContext';
import { isDevelopmentMockAuth } from '@/auth/developmentMockAuth';
import { AppButton } from '@/design-system/AppButton';
import { AppText } from '@/design-system/AppText';
import { EmptyState } from '@/design-system/EmptyState';
import { Notice } from '@/design-system/Notice';
import { spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { GoogleButton } from './GoogleButton';
import { UaePassButton } from './UaePassButton';
import type { UaePassSignIn } from './useUaePassSignIn';

export interface SignInPanelProps {
  /** From useUaePassSignIn in the route, which also shows the connecting screen over everything. */
  signIn: UaePassSignIn;
}

/**
 * The way in, chosen in the app (D-142): "Sign in with UAE PASS", which opens UAE PASS itself (it also creates the
 * account: the server decides who still confirms their details, D-97), or a plain "Sign in" that names no provider
 * when the build does not send its issuer to UAE PASS, and in development fixtures, whose sign-in is simulated. A
 * development build against a development provider also offers its test accounts as a quiet second action. After an
 * expired session it says why first. A build with no sign-in says so honestly. No staging, simulated or development
 * label is shown (D-142); the docs say what each is. "Continue with Google" follows the main button when the identity
 * provider offers the Google sign-in (D-147).
 */
export function SignInPanel({ signIn }: SignInPanelProps) {
  const { t } = useLocalization();
  const { state, provider } = useAuth();
  const expired = state.status === 'signed_out' && state.reason === 'session_expired';
  // Development fixtures only: Settings reset the data and signed out (ProfileView).
  const dataReset = state.status === 'signed_out' && isDevelopmentMockAuth(provider) && provider.demonstrationReset;

  return (
    <View style={styles.panel}>
      {dataReset ? <Notice tone="success" message={t('profile.devTools.resetDone')} announce /> : null}
      {expired ? (
        <Notice tone="warning" title={t('signIn.expired.title')} message={t('signIn.expired.message')} announce />
      ) : null}
      {signIn.failure ? (
        <Notice
          tone="danger"
          title={t(`signIn.failures.${signIn.failure}.title`)}
          message={t(`signIn.failures.${signIn.failure}.message`)}
          announce
        />
      ) : null}
      {!signIn.available ? (
        <EmptyState title={t('signIn.unavailable.title')} message={t('signIn.unavailable.message')} />
      ) : (
        <>
          {signIn.uaePass ? (
            <UaePassButton
              label={t('signIn.uaePass.signIn')}
              loading={signIn.connecting}
              disabled={signIn.busy}
              onPress={signIn.start}
            />
          ) : (
            <AppButton
              label={t('signIn.action')}
              loading={signIn.busy && !signIn.connectingGoogle}
              disabled={signIn.busy}
              onPress={signIn.start}
            />
          )}
          {signIn.google ? (
            <GoogleButton
              label={t('signIn.google.signIn')}
              loading={signIn.connectingGoogle}
              disabled={signIn.busy}
              onPress={signIn.startGoogle}
            />
          ) : null}
          <AppText variant="caption" tone="secondary" style={styles.centred}>
            {t('signIn.newHere')}
          </AppText>
          {signIn.testAccounts ? (
            // The provider's own page, with UAE PASS among its choices: a neutral name, no explanation.
            <AppButton
              variant="tertiary"
              label={t('signIn.otherOptions')}
              loading={signIn.busy && !signIn.connecting && !signIn.connectingGoogle}
              disabled={signIn.busy}
              onPress={signIn.startTestAccounts}
            />
          ) : null}
        </>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  panel: { gap: spacing.md },
  centred: { textAlign: 'center' },
});
