import { Pressable, StyleSheet, Switch, View } from 'react-native';

import { AppText } from './AppText';
import { colors, minTouchTarget, spacing } from './tokens';

export interface SwitchFieldProps {
  label: string;
  description?: string;
  value: boolean;
  onValueChange: (value: boolean) => void;
  disabled?: boolean;
}

/** A yes/no setting. The whole row is one switch for screen readers and one large touch target. */
export function SwitchField({ label, description, value, onValueChange, disabled = false }: SwitchFieldProps) {
  return (
    <Pressable
      accessibilityRole="switch"
      accessibilityLabel={label}
      accessibilityHint={description}
      accessibilityState={{ checked: value, disabled }}
      disabled={disabled}
      onPress={() => onValueChange(!value)}
      style={styles.row}
    >
      <View style={styles.text}>
        <AppText variant="bodyStrong">{label}</AppText>
        {description ? (
          <AppText variant="caption" tone="muted">
            {description}
          </AppText>
        ) : null}
      </View>
      <Switch
        accessible={false}
        importantForAccessibility="no"
        value={value}
        onValueChange={onValueChange}
        disabled={disabled}
        trackColor={{ false: colors.borderStrong, true: colors.primary }}
        thumbColor={colors.surface}
        ios_backgroundColor={colors.borderStrong}
      />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, minHeight: minTouchTarget },
  text: { flex: 1, gap: spacing.xxs },
});
