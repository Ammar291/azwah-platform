import { createContext, useContext, useState, type ReactNode } from 'react';
import { I18nManager, StyleSheet, View } from 'react-native';

const LayoutDirectionContext = createContext<boolean | null>(null);

/**
 * Whether the native layout runs right to left. Usually the same as I18nManager.isRTL, but Expo Go can lay out right
 * to left while reporting false, so the provider measures it. Without a provider (or before it measures), falls back
 * to I18nManager.
 */
export function useIsRTLLayout(): boolean {
  return useContext(LayoutDirectionContext) ?? I18nManager.isRTL;
}

/**
 * Measures the layout direction once: an invisible one-point marker at `start: 0` inside a two-point box lands at
 * x = 0 left to right and x = 1 right to left.
 */
export function LayoutDirectionProvider({ children }: { children: ReactNode }) {
  const [rtl, setRtl] = useState<boolean | null>(null);
  return (
    <LayoutDirectionContext.Provider value={rtl}>
      <View style={styles.probe} pointerEvents="none" accessible={false} importantForAccessibility="no-hide-descendants">
        <View
          testID="layout-direction-marker"
          style={styles.marker}
          onLayout={(event) => setRtl(event.nativeEvent.layout.x > 0)}
        />
      </View>
      {children}
    </LayoutDirectionContext.Provider>
  );
}

const styles = StyleSheet.create({
  probe: { position: 'absolute', top: 0, width: 2, height: 1, opacity: 0 },
  marker: { position: 'absolute', top: 0, start: 0, width: 1, height: 1 },
});
