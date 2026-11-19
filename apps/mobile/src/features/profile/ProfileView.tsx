import { useRouter } from 'expo-router';
import { useState } from 'react';

import { demonstrationDataOf, type DemonstrationData } from '@/api/participant/demonstrationData';
import { useParticipantApi } from '@/api/participant/ParticipantApiContext';
import { useMe } from '@/api/participant/queries';
import { useAppMode } from '@/app-shell/AppModeContext';
import { useAuth } from '@/auth/AuthContext';
import { isDevelopmentMockAuth, type DevelopmentMockAuth } from '@/auth/developmentMockAuth';
import { fullName } from '@/auth/identityProfile';
import { LanguageSwitcher } from '@/components/LanguageSwitcher';
import { SignOutDialog } from '@/components/SignOutDialog';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';
import { ListGroup, ListRow } from '@/design-system/ListGroup';
import { Notice } from '@/design-system/Notice';
import { Section } from '@/design-system/Section';
import { useLocalization } from '@/i18n/useLocalization';

import { DataExportCard } from './DataExportCard';
import { PrivacyRequests } from './PrivacyRequests';

/**
 * Settings: who is signed in, the household, language, consent and help, the person's data (a copy, and requests
 * about it), signing out, and development tools when simulated.
 */
export function ProfileView() {
  const { t, language } = useLocalization();
  const router = useRouter();
  const { state, provider } = useAuth();
  const api = useParticipantApi();
  const mode = useAppMode();
  const me = useMe();
  const [confirmingSignOut, setConfirmingSignOut] = useState(false);
  const simulated = isDevelopmentMockAuth(provider);
  // The fixtures' own guard (app-shell/services.ts): a development build with the fixtures flag, never anything else.
  const demonstration = __DEV__ && mode === 'development_fixtures' ? demonstrationDataOf(api) : null;
  const persona = state.status === 'signed_in' ? state.session.personaCode : null;
  const profile = state.status === 'signed_in' ? state.session.profile : null;
  const name = fullName(profile, language);

  return (
    <>
      <Section title={t('profile.account')}>
        <AppCard>
          <AppText variant="bodyStrong">
            {name ?? (simulated && persona ? t(`devFixtures.personas.${persona}.name`) : t('profile.signedIn'))}
          </AppText>
          {profile?.source === 'uae_pass' || profile?.source === 'uae_pass_staging' ? (
            // Staging or not, it reads "UAE PASS": no "(staging)" label in the app (D-142), and never more than that.
            <AppText tone="secondary">{t('profile.signedInWithUaePass')}</AppText>
          ) : null}
          {me.data?.household ? (
            <AppText tone="secondary">
              {t('household.reference', { reference: me.data.household.reference })}
            </AppText>
          ) : null}
        </AppCard>
      </Section>

      <Section title={t('language.title')}>
        <LanguageSwitcher variant="choice" hideLabel />
      </Section>

      <Section title={t('profile.more')}>
        <ListGroup>
          <ListRow title={t('consent.title')} onPress={() => router.push('/consent')} />
          <ListRow title={t('help.title')} onPress={() => router.push('/help')} />
        </ListGroup>
      </Section>

      <Section title={t('profile.data.title')}>
        <DataExportCard />
      </Section>

      <Section title={t('profile.data.requests.title')} description={t('profile.data.requests.intro')}>
        <PrivacyRequests />
      </Section>

      <AppButton label={t('profile.signOut')} variant="secondary" onPress={() => setConfirmingSignOut(true)} />

      {/* Development fixtures: the reset, as a quiet control under Sign out with a neutral name (D-142). */}
      {simulated && demonstration ? <DemonstrationReset demonstration={demonstration} auth={provider} /> : null}

      <SignOutDialog visible={confirmingSignOut} onClose={() => setConfirmingSignOut(false)} />
    </>
  );
}

/**
 * Development fixtures only: puts every simulated account back to how it started, after asking. The fixture data
 * goes first; then an ordinary sign-out deletes the answers, session copies and conversation kept on the phone, and
 * the sign-in screen says the reset is done. A failure changes nothing and says so here.
 */
function DemonstrationReset({ demonstration, auth }: { demonstration: DemonstrationData; auth: DevelopmentMockAuth }) {
  const { t } = useLocalization();
  const [confirming, setConfirming] = useState(false);
  const [resetting, setResetting] = useState(false);
  const [failed, setFailed] = useState(false);

  const reset = async () => {
    setResetting(true);
    setFailed(false);
    try {
      await demonstration.resetDemonstration();
      await auth.signOutAfterDemonstrationReset();
    } catch {
      setFailed(true);
    } finally {
      setResetting(false);
      setConfirming(false);
    }
  };

  return (
    <>
      <AppButton
        label={t('profile.devTools.resetData')}
        variant="tertiary"
        onPress={() => setConfirming(true)}
      />
      {failed ? (
        <Notice
          tone="danger"
          title={t('errors.kinds.unexpected.title')}
          message={t('errors.kinds.unexpected.message')}
          announce
        />
      ) : null}
      <ConfirmationDialog
        visible={confirming}
        destructive
        title={t('profile.devTools.resetTitle')}
        message={t('profile.devTools.resetMessage')}
        confirmLabel={t('profile.devTools.resetData')}
        cancelLabel={t('common.cancel')}
        busy={resetting}
        onConfirm={() => void reset()}
        onCancel={() => setConfirming(false)}
      />
    </>
  );
}
