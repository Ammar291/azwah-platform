import { useFonts } from 'expo-font';
import { type ErrorBoundaryProps } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect, useMemo, useState } from 'react';
import { StyleSheet } from 'react-native';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { createQueryClient } from '@/api/queries';
import { AppProviders } from '@/app-shell/AppProviders';
import { AppShell } from '@/app-shell/AppShell';
import { BootFailure } from '@/app-shell/BootFailure';
import { bootstrap, type BootResult } from '@/app-shell/bootstrap';
import { nativeBootDependencies } from '@/app-shell/nativeBootDependencies';
import { RootErrorFallback } from '@/app-shell/RootErrorFallback';
import { appFonts } from '@/design-system/fonts';
import { LayoutDirectionProvider } from '@/design-system/LayoutDirection';
import { createAppAssessmentEngine } from '@/features/sessions/assessmentEngine';
import { StartupSplash } from '@/features/splash/StartupSplash';
import type { Language } from '@/i18n/languages';
import { createLanguageSwitcher } from '@/i18n/languageSwitcher';

void SplashScreen.preventAutoHideAsync();

export default function RootLayout() {
  const [fontsLoaded, fontError] = useFonts(appFonts);
  const [boot, setBoot] = useState<BootResult | null>(null);
  const [bootFailed, setBootFailed] = useState(false);
  const [language, setLanguage] = useState<Language | null>(null);
  const [queryClient] = useState(createQueryClient);
  const [splashShown, setSplashShown] = useState(false);

  useEffect(() => {
    bootstrap(nativeBootDependencies).then(
      (result) => {
        setBoot(result);
        setLanguage(result.language);
      },
      () => setBootFailed(true),
    );
  }, []);

  const ready = (fontsLoaded || fontError !== null) && (boot !== null || bootFailed);
  useEffect(() => {
    if (ready) {
      void SplashScreen.hideAsync();
    }
  }, [ready]);

  const changeLanguage = useMemo(
    () => (boot ? createLanguageSwitcher({ settings: boot.settings, i18n: boot.i18n, onChanged: setLanguage }) : null),
    [boot],
  );

  // Starts paused: nothing is sent until the drafts on the phone are confirmed as the signed-in person's.
  const engine = useMemo(
    () => (boot ? createAppAssessmentEngine(boot.services, queryClient, { paused: true }) : null),
    [boot, queryClient],
  );

  const languageValue = useMemo(
    () => (language && changeLanguage ? { language, changeLanguage } : null),
    [language, changeLanguage],
  );

  if (bootFailed) {
    return <BootFailure />;
  }
  if (!ready || !boot || !languageValue || !engine) {
    return null;
  }

  return (
    // The side menu's swipe needs the gesture root (D-106).
    <GestureHandlerRootView style={styles.root}>
      <SafeAreaProvider>
        <LayoutDirectionProvider>
          <AppProviders
            services={boot.services}
            engine={engine}
            i18n={boot.i18n}
            language={languageValue}
            queryClient={queryClient}
          >
            <StatusBar style="dark" />
            {/* A restored session starts locked, except straight after the reload that applies a chosen language. */}
            <AppShell settings={boot.settings} lockRestoredSession={!boot.startedByLanguageChange} />
            {/* The opening sequence, once per start; not again after the reload that applies a chosen language. */}
            {splashShown || boot.startedByLanguageChange ? null : <StartupSplash onDone={() => setSplashShown(true)} />}
          </AppProviders>
        </LayoutDirectionProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1 },
});

/** The root layout itself failed: translations may not be loaded, so the fallback is bilingual inline text. */
export function ErrorBoundary({ retry }: ErrorBoundaryProps) {
  return <RootErrorFallback retry={() => void retry()} />;
}
