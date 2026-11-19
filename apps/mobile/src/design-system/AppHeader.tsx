import { Pressable, StyleSheet, View } from 'react-native';

import { AppText } from './AppText';
import { Icon } from './Icon';
import { borderWidths, colors, minTouchTarget, radii, spacing } from './tokens';

export interface HeaderAction {
  label: string;
  onPress: () => void;
}

export interface AppHeaderProps {
  title: string;
  /** Shows a back button; it points to the start edge, so right in Arabic. */
  onBack?: () => void;
  backLabel?: string;
  /** Shows the menu button at the start edge instead of back, on the screens the side menu leads to. */
  menu?: HeaderAction;
  action?: HeaderAction;
}

export function AppHeader({ title, onBack, backLabel, menu, action }: AppHeaderProps) {
  return (
    <View style={styles.bar}>
      {menu ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={menu.label}
          onPress={menu.onPress}
          hitSlop={spacing.xs}
          style={({ pressed }) => [styles.iconButton, pressed ? styles.pressed : null]}
          testID="header-menu"
        >
          <Icon name="menu" size={24} color={colors.primary} />
        </Pressable>
      ) : onBack ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={backLabel}
          onPress={onBack}
          hitSlop={spacing.xs}
          style={({ pressed }) => [styles.iconButton, pressed ? styles.pressed : null]}
        >
          <Icon name="chevron-back" size={22} color={colors.primary} />
        </Pressable>
      ) : null}
      <AppText variant="barTitle" accessibilityRole="header" numberOfLines={2} style={styles.title}>
        {title}
      </AppText>
      {action ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={action.label}
          onPress={action.onPress}
          style={({ pressed }) => [styles.action, pressed ? styles.pressedAction : null]}
        >
          {/* One line, so a long action never pushes the title into a column of half words. */}
          <AppText variant="label" tone="primary" numberOfLines={1}>
            {action.label}
          </AppText>
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  bar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    minHeight: minTouchTarget + spacing.md,
    paddingHorizontal: spacing.md,
    borderBottomWidth: borderWidths.hairline,
    borderBottomColor: colors.borderSoft,
    backgroundColor: colors.background,
  },
  iconButton: {
    width: minTouchTarget,
    height: minTouchTarget,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: radii.pill,
  },
  title: { flex: 1, paddingHorizontal: spacing.xs },
  action: {
    minHeight: minTouchTarget,
    minWidth: minTouchTarget,
    paddingHorizontal: spacing.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  // A round tint behind the glyph, as the platforms do for toolbar buttons.
  pressed: { backgroundColor: colors.primarySoft },
  pressedAction: { backgroundColor: colors.primarySoft, borderRadius: radii.md },
});
