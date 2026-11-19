import { createContext, useContext } from 'react';

import type { DraftStore } from './types';

const DraftStoreContext = createContext<DraftStore | null>(null);

export const DraftStoreProvider = DraftStoreContext.Provider;

export function useDraftStore(): DraftStore {
  const store = useContext(DraftStoreContext);
  if (!store) {
    throw new Error('useDraftStore must be used inside DraftStoreProvider.');
  }
  return store;
}
