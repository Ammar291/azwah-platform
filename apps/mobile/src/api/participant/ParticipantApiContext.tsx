import { createContext, useContext } from 'react';

import type { ParticipantApi } from './participantApi';

const ParticipantApiContext = createContext<ParticipantApi | null>(null);

export const ParticipantApiProvider = ParticipantApiContext.Provider;

export function useParticipantApi(): ParticipantApi {
  const api = useContext(ParticipantApiContext);
  if (!api) {
    throw new Error('useParticipantApi must be used inside ParticipantApiProvider.');
  }
  return api;
}
