import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import type { i18n as I18n } from 'i18next';
import { useEffect, type ReactNode } from 'react';
import { I18nextProvider } from 'react-i18next';

import { ParticipantApiProvider } from '@/api/participant/ParticipantApiContext';
import { AuthContextProvider } from '@/auth/AuthContext';
import { DraftStoreProvider } from '@/drafts/DraftStoreContext';
import { ChatStoreProvider } from '@/features/assistant/ChatStoreContext';
import { ScoreReportStoreProvider } from '@/features/score-report/ScoreReportStoreContext';
import { ConsultantConnectorProvider } from '@/features/consultant/ConsultantConnectorContext';
import type { AssessmentEngine } from '@/features/sessions/assessmentEngine';
import { AssessmentEngineProvider } from '@/features/sessions/AssessmentEngineContext';
import { LanguageProvider, type LanguageContextValue } from '@/i18n/LanguageContext';
import { bindOnlineManager } from '@/network/connectivity';
import { ConnectivityProvider } from '@/network/ConnectivityContext';

import { AppModeProvider } from './AppModeContext';
import type { AppServices } from './services';

export interface AppProvidersProps {
  services: AppServices;
  /** Made by the caller, which decides when it runs (the root layout resumes it once the drafts owner is known). */
  engine: AssessmentEngine;
  i18n: I18n;
  language: LanguageContextValue;
  queryClient: QueryClient;
  children: ReactNode;
}

/** Every context the screens rely on. The root layout and the screen tests use the same composition. */
export function AppProviders({ services, engine, i18n, language, queryClient, children }: AppProvidersProps) {
  // The engine listens for the connection and the foreground; queries pause while the phone is offline.
  useEffect(() => {
    engine.start();
    const unbind = bindOnlineManager(services.connectivity);
    return () => {
      unbind();
      engine.stop();
    };
  }, [engine, services.connectivity]);

  return (
    <QueryClientProvider client={queryClient}>
      <I18nextProvider i18n={i18n}>
        <LanguageProvider value={language}>
          <AppModeProvider value={services.mode}>
            <AuthContextProvider value={services.auth}>
              <ParticipantApiProvider value={services.api}>
                <DraftStoreProvider value={services.drafts}>
                  <ChatStoreProvider value={services.chat}>
                    <ScoreReportStoreProvider value={services.reports}>
                      <ConsultantConnectorProvider value={services.consultant}>
                        <ConnectivityProvider value={services.connectivity}>
                          <AssessmentEngineProvider value={engine}>{children}</AssessmentEngineProvider>
                        </ConnectivityProvider>
                      </ConsultantConnectorProvider>
                    </ScoreReportStoreProvider>
                  </ChatStoreProvider>
                </DraftStoreProvider>
              </ParticipantApiProvider>
            </AuthContextProvider>
          </AppModeProvider>
        </LanguageProvider>
      </I18nextProvider>
    </QueryClientProvider>
  );
}
