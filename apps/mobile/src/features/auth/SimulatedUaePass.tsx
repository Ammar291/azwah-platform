import { Redirect, useRouter } from 'expo-router';
import { useEffect, useRef, useState } from 'react';

import { useAuth } from '@/auth/AuthContext';
import { isDevelopmentMockAuth, type DevelopmentMockAuth } from '@/auth/developmentMockAuth';
import { fullName } from '@/auth/identityProfile';
import { AppButton } from '@/design-system/AppButton';
import { AppHeader } from '@/design-system/AppHeader';
import { RadioCardGroup } from '@/design-system/RadioCardGroup';
import { Screen } from '@/design-system/Screen';
import { useLocalization } from '@/i18n/useLocalization';

/** How long the approve button stays busy before the simulated sign-in finishes, so the flow reads like a real one. */
export const SIMULATED_HANDOVER_MS = 1200;

/**
 * DEVELOPMENT FIXTURES ONLY. Stands in for UAE PASS: no identity is checked and nothing leaves the phone. The
 * presenter picks a made-up account (one returning, one first-time), continues, and the simulated sign-in releases
 * that persona's invented details, so the confirm-details step can be seen without a UAE PASS staging account. It is
 * titled "Choose an account" and never calls itself UAE PASS; nothing on it says it is simulated (D-142). In any other
 * build the route sends people back to the welcome screen.
 */
export function SimulatedUaePass() {
  const { provider } = useAuth();
  if (!isDevelopmentMockAuth(provider)) {
    return <Redirect href="/welcome" />;
  }
  return <SimulatedUaePassScreen provider={provider} />;
}

function SimulatedUaePassScreen({ provider }: { provider: DevelopmentMockAuth }) {
  const { t, language } = useLocalization();
  const router = useRouter();
  const [persona, setPersona] = useState<string | null>(null);
  const [connecting, setConnecting] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (timer.current) clearTimeout(timer.current);
    },
    [],
  );

  const approve = () => {
    if (!persona || connecting) return;
    setConnecting(true);
    timer.current = setTimeout(() => {
      void provider.signIn({ method: 'development_persona', personaCode: persona }).catch(() => setConnecting(false));
    }, SIMULATED_HANDOVER_MS);
  };

  return (
    <Screen
      header={
        <AppHeader
          title={t('signIn.simulated.title')}
          onBack={() => (router.canGoBack() ? router.back() : router.replace('/welcome'))}
          backLabel={t('common.back')}
        />
      }
      footer={
        <AppButton label={t('signIn.simulated.approve')} disabled={!persona} loading={connecting} onPress={approve} />
      }
    >
      <RadioCardGroup
        label={t('signIn.simulated.accountLabel')}
        options={provider.personas.map(({ code, profile }) => ({
          value: code,
          label: fullName(profile ?? null, language) ?? t(`devFixtures.personas.${code}.name`),
          description: t(`devFixtures.personas.${code}.description`),
        }))}
        value={persona}
        onChange={setPersona}
      />
    </Screen>
  );
}
