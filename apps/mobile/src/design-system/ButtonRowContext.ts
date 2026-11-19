import { createContext, useContext } from 'react';

/** True inside a ButtonRow laid out side by side: buttons there use compact padding. */
export const ButtonRowContext = createContext(false);

export function useInButtonRow(): boolean {
  return useContext(ButtonRowContext);
}
