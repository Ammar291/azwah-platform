import { createContext, useContext, useMemo, type ReactNode } from 'react';

import { useParticipantApi } from '@/api/participant/ParticipantApiContext';

import { createReportPreparer, type ReportPreparer } from './reportPreparer';
import type { ScoreReportStore } from './reportStore';

interface ScoreReportServices {
  store: ScoreReportStore;
  preparer: ReportPreparer;
}

const ScoreReportContext = createContext<ScoreReportServices | null>(null);

/** The store and the one preparer of this app run, which outlives every screen that asks it to write. */
export function ScoreReportStoreProvider({ value, children }: { value: ScoreReportStore; children: ReactNode }) {
  const api = useParticipantApi();
  const services = useMemo(() => ({ store: value, preparer: createReportPreparer({ api, store: value }) }), [api, value]);
  return <ScoreReportContext.Provider value={services}>{children}</ScoreReportContext.Provider>;
}

function useServices(): ScoreReportServices {
  const services = useContext(ScoreReportContext);
  if (!services) {
    throw new Error('Score report services are used inside ScoreReportStoreProvider.');
  }
  return services;
}

export const useScoreReportStore = (): ScoreReportStore => useServices().store;

export const useReportPreparer = (): ReportPreparer => useServices().preparer;
