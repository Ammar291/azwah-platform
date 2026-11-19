import { createContext, useContext } from 'react';

import type { ChatStore } from './chatStore';

const ChatStoreContext = createContext<ChatStore | null>(null);

export const ChatStoreProvider = ChatStoreContext.Provider;

export function useChatStore(): ChatStore {
  const store = useContext(ChatStoreContext);
  if (!store) {
    throw new Error('useChatStore must be used inside ChatStoreProvider.');
  }
  return store;
}
