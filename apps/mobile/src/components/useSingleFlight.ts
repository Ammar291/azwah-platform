import { useCallback, useRef } from 'react';

/**
 * Runs an async action at most once at a time: a second press while the first is still running is ignored. A ref,
 * not state, so two presses inside one frame (before the button re-renders as busy) still start only one request.
 */
export function useSingleFlight() {
  const running = useRef(false);
  return useCallback(async (action: () => Promise<unknown>) => {
    if (running.current) {
      return;
    }
    running.current = true;
    try {
      await action();
    } finally {
      running.current = false;
    }
  }, []);
}
