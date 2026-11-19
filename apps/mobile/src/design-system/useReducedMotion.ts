import { useEffect, useState } from 'react';
import { AccessibilityInfo } from 'react-native';

/**
 * True when the person asked the phone to reduce motion. Animations then keep only fades: nothing moves, scales or
 * spins. Starts false and updates once the setting is read.
 */
export function useReducedMotion(): boolean {
  const [reduced, setReduced] = useState(false);
  useEffect(() => {
    let current = true;
    AccessibilityInfo.isReduceMotionEnabled().then(
      (value) => {
        if (current) setReduced(value);
      },
      () => undefined,
    );
    const subscription = AccessibilityInfo.addEventListener('reduceMotionChanged', setReduced);
    return () => {
      current = false;
      subscription.remove();
    };
  }, []);
  return reduced;
}
