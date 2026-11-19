import { StyleSheet, View } from 'react-native';

import { AppButton } from './AppButton';
import { AppText } from './AppText';
import { Icon, type IconName } from './Icon';
import { radii, spacing } from './tokens';

export interface StateAction {
  label: string;
  onPress: () => void;
}

interface StateLayoutProps {
  icon: IconName;
  iconColor: string;
  iconBackground: string;
  title: string;
  titleRole: 'alert' | 'header';
  message?: string;
  primary?: StateAction;
  secondary?: StateAction;
}

/** The shared shape of ErrorState and EmptyState: a glyph, a heading, a message and up to two actions. */
export function StateLayout({
  icon,
  iconColor,
  iconBackground,
  title,
  titleRole,
  message,
  primary,
  secondary,
}: StateLayoutProps) {
  return (
    <View style={styles.container}>
      <View style={[styles.emblem, { backgroundColor: iconBackground }]}>
        <Icon name={icon} size={28} color={iconColor} />
      </View>
      <AppText variant="heading" accessibilityRole={titleRole} style={styles.centred}>
        {title}
      </AppText>
      {message ? (
        <AppText tone="secondary" style={styles.centred}>
          {message}
        </AppText>
      ) : null}
      {primary || secondary ? (
        <View style={styles.actions}>
          {primary ? <AppButton label={primary.label} onPress={primary.onPress} /> : null}
          {secondary ? <AppButton label={secondary.label} variant="secondary" onPress={secondary.onPress} /> : null}
        </View>
      ) : null}
    </View>
  );
}

export const stateStyles = StyleSheet.create({
  container: { alignItems: 'center', justifyContent: 'center', gap: spacing.md, paddingVertical: spacing.xxl },
  centred: { textAlign: 'center' },
});

const styles = StyleSheet.create({
  ...stateStyles,
  emblem: { width: 64, height: 64, borderRadius: radii.pill, alignItems: 'center', justifyContent: 'center' },
  actions: { alignSelf: 'stretch', gap: spacing.sm, marginTop: spacing.sm },
});
