import { createContext, useContext } from 'react';

import type { ConsultantConnector } from './consultantConnector';

const ConsultantConnectorContext = createContext<ConsultantConnector | null>(null);

export const ConsultantConnectorProvider = ConsultantConnectorContext.Provider;

export function useConsultantConnector(): ConsultantConnector {
  const connector = useContext(ConsultantConnectorContext);
  if (!connector) {
    throw new Error('useConsultantConnector must be used inside ConsultantConnectorProvider.');
  }
  return connector;
}
