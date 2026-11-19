import { ActivityIndicator, View } from 'react-native';

import { AppText } from './AppText';
import { stateStyles } from './StateLayout';
import { colors } from './tokens';

export interface LoadingStateProps {
  /** Shown and read aloud, for example "Loading your household". */
  label: string;
}

export function LoadingState({ label }: LoadingStateProps) {
  return (
    <View
      style={stateStyles.container}
      accessible
      accessibilityLabel={label}
      accessibilityState={{ busy: true }}
      accessibilityLiveRegion="polite"
    >
      <ActivityIndicator size="large" color={colors.primary} />
      <AppText tone="muted" style={stateStyles.centred}>
        {label}
      </AppText>
    </View>
  );
}
