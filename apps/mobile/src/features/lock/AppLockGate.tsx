import { useCallback, type ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';

import { useAppLock } from '@/security/AppLockContext';

import { LockScreen } from './LockScreen';
import { PrivacyCover } from './PrivacyCover';

/**
 * Wraps the navigator. Every touch reaches the lock's idle timer on the way down (the capture handler never takes the
 * touch). Locked, the lock screen covers everything and the navigator is hidden from screen readers; the signed-in
 * screens themselves are unmounted by the navigator's guard (AppShell). Away from the foreground, the privacy cover
 * hides everything, the lock screen included.
 */
export function AppLockGate({ children }: { children: ReactNode }) {
  const { locked, covered, touched } = useAppLock();
  const hidden = locked || covered;
  const noteTouch = useCallback(() => {
    touched();
    return false;
  }, [touched]);

  return (
    <View style={styles.fill} onStartShouldSetResponderCapture={noteTouch} testID="app-lock-gate">
      <View
        style={styles.fill}
        accessibilityElementsHidden={hidden}
        importantForAccessibility={hidden ? 'no-hide-descendants' : 'auto'}
        testID="app-content"
      >
        {children}
      </View>
      {locked ? <LockScreen /> : null}
      {covered ? <PrivacyCover /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
});
