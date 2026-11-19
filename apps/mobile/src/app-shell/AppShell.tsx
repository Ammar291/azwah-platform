import { useQueryClient } from '@tanstack/react-query';
import { Stack, usePathname, useSegments } from 'expo-router';
import { useEffect, useMemo, useState } from 'react';

import { participantKeys } from '@/api/participant/queries';
import { useAuth } from '@/auth/AuthContext';
import { isSignedIn } from '@/auth/entryHref';
import type { AuthSession } from '@/auth/types';
import { colors } from '@/design-system/tokens';
import { useChatStore } from '@/features/assistant/ChatStoreContext';
import { AppLockGate } from '@/features/lock/AppLockGate';
import { OnboardingProvider, type OnboardingState } from '@/features/onboarding/OnboardingContext';
import { useRegistrationStatus } from '@/features/onboarding/registration';
import { useReportPreparer, useScoreReportStore } from '@/features/score-report/ScoreReportStoreContext';
import { useAssessmentEngine } from '@/features/sessions/AssessmentEngineContext';
import { AppLockProvider, useAppLock, type AppLockDependencies } from '@/security/AppLockContext';
import { expoDataFileSharing, type DataFileSharing } from '@/storage/exportFiles';
import type { SettingsRepository } from '@/storage/settings';

import { createDraftOwnership } from './draftOwnership';

export interface AppShellProps {
  settings: SettingsRepository;
  /** Tests pass fakes for the phone; the app uses the real AppState and expo-local-authentication. */
  lockDependencies?: Partial<AppLockDependencies>;
  /** False only for the reload that applies a language the person just chose (bootstrap.ts). */
  lockRestoredSession?: boolean;
  /** Where a data export waits for the share sheet; emptied at start, at every lock and at sign-out. */
  exportFiles?: Pick<DataFileSharing, 'clearAll'>;
}

/** The navigator inside the inactivity lock: what the root layout renders once start-up is done. */
export function AppShell({
  settings,
  lockDependencies,
  lockRestoredSession,
  exportFiles = expoDataFileSharing,
}: AppShellProps) {
  // A plaintext export left by a run that ended while the share sheet was open (the process killed, say).
  useEffect(() => {
    void exportFiles.clearAll();
  }, [exportFiles]);

  return (
    <AppLockProvider dependencies={lockDependencies} lockRestoredSession={lockRestoredSession}>
      <AppLockGate>
        <RootNavigator settings={settings} exportFiles={exportFiles} />
      </AppLockGate>
    </AppLockProvider>
  );
}

/**
 * Signed-out screens, onboarding and signed-in screens are separate guarded groups. When a guard closes (sign-in,
 * sign-out, an expired session, confirming details, or the app locking) Expo Router returns to the anchor route,
 * app/index.tsx, which redirects by auth and onboarding state. Locked, the signed-in groups' guards are closed, so
 * none of their screens stays mounted behind the lock.
 */
function RootNavigator({
  settings,
  exportFiles,
}: {
  settings: SettingsRepository;
  exportFiles: Pick<DataFileSharing, 'clearAll'>;
}) {
  const { state } = useAuth();
  const { locked, noteLocation, onLock, touched } = useAppLock();
  const queryClient = useQueryClient();
  const engine = useAssessmentEngine();
  const chat = useChatStore();
  const reports = useScoreReportStore();
  const reportPreparer = useReportPreparer();
  const ownership = useMemo(
    () =>
      createDraftOwnership(engine, settings, exportFiles, chat, {
        clearAll: () => {
          // First, so a part still being written is not saved after the store is emptied.
          reportPreparer.reset();
          return reports.clearAll();
        },
      }),
    [engine, settings, exportFiles, chat, reportPreparer, reports],
  );
  const signedIn = isSignedIn(state);
  // New people confirm their UAE PASS details once (D-97); "account ready" keeps onboarding open until they leave it.
  // Kept for this sign-in's session only, so it lapses by itself at sign-out, even if the same person signs in again.
  const registration = useRegistrationStatus({ enabled: !locked });
  const session = state.status === 'signed_in' ? state.session : null;
  const [finishingFor, setFinishingFor] = useState<AuthSession | null>(null);
  const finishing = session !== null && finishingFor === session;
  const onboardingActive = signedIn && (registration === 'required' || finishing);
  const onboarding = useMemo<OnboardingState>(
    () => ({
      active: onboardingActive,
      finishing,
      registration,
      beginFinishing: () => setFinishingFor(session),
      finish: () => setFinishingFor(null),
    }),
    [finishing, onboardingActive, registration, session],
  );
  const pathname = usePathname();
  const [group] = useSegments();
  const onSignedInScreen = group === '(app)' || group === 'consent';

  // Nobody sees the previous person's data: leaving the signed-in state pauses sending and drops cached responses,
  // signing out deletes local data, and data left by an expired session is kept only for the same account
  // (createDraftOwnership). Queued submissions resume once the data is confirmed as the signed-in person's.
  useEffect(() => {
    if (state.status === 'signed_in') {
      let current = true;
      void ownership.claim(state.session.subject).then(() => {
        if (current) engine.resume();
      });
      return () => {
        current = false;
      };
    }
    engine.pause();
    if (state.status === 'signed_out') {
      queryClient.removeQueries({ queryKey: participantKeys.all });
      if (state.reason === 'signed_out') {
        void ownership.forget();
      } else {
        // Drafts wait for the same account after an expiry; a plaintext export never waits (forget deletes it too).
        void exportFiles.clearAll();
      }
    }
    return undefined;
  }, [engine, exportFiles, ownership, queryClient, state]);

  // A share sheet the lock cannot cover may still be open: its file goes, so no share target can read it any more.
  useEffect(() => onLock(() => void exportFiles.clearAll()), [exportFiles, onLock]);

  // A new screen is activity, for people who use a keyboard or a screen reader rather than touch. Unlocking returns
  // to the screen the person was on (in memory only; IDs in routes are only handles).
  useEffect(() => {
    touched();
    if (onSignedInScreen) {
      noteLocation(pathname);
    }
  }, [noteLocation, onSignedInScreen, pathname, touched]);

  return (
    <OnboardingProvider value={onboarding}>
      <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.background } }}>
        <Stack.Screen name="index" options={{ animation: 'fade' }} />
        <Stack.Protected guard={!signedIn}>
          <Stack.Screen name="(public)" options={{ animation: 'fade' }} />
        </Stack.Protected>
        <Stack.Protected guard={signedIn && !locked && onboardingActive}>
          <Stack.Screen name="(onboarding)" options={{ animation: 'fade' }} />
        </Stack.Protected>
        <Stack.Protected guard={signedIn && !locked && !onboardingActive}>
          <Stack.Screen name="(app)" options={{ animation: 'fade' }} />
          <Stack.Screen name="consent" />
        </Stack.Protected>
      </Stack>
    </OnboardingProvider>
  );
}
