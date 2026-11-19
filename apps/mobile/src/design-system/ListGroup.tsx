import { Children, Fragment, isValidElement, type ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { AppText } from './AppText';
import { Icon, type IconName } from './Icon';
import { borderWidths, colors, minTouchTarget, radii, spacing } from './tokens';

export interface ListGroupProps {
  children: ReactNode;
  testID?: string;
}

/**
 * Related links on one surface, separated by hairlines, instead of a stack of separate cards. Each child is a
 * ListRow.
 */
export function ListGroup({ children, testID }: ListGroupProps) {
  const rows = Children.toArray(children).filter(isValidElement);
  return (
    <View style={styles.group} testID={testID}>
      {rows.map((row, index) => (
        <Fragment key={row.key ?? index}>
          {index > 0 ? <View style={styles.divider} /> : null}
          {row}
        </Fragment>
      ))}
    </View>
  );
}

interface ListRowBase {
  /** A glyph at the start edge. */
  icon?: IconName;
  onPress: () => void;
  accessibilityHint?: string;
  testID?: string;
}

interface TitledListRowProps extends ListRowBase {
  title: string;
  /** A second line under the title. */
  description?: string;
  /** What the row does, read as the button's name. Defaults to the title. */
  accessibilityLabel?: string;
  children?: never;
}

interface CustomListRowProps extends ListRowBase {
  /** Content richer than a title and a line, for example a question and its answer. */
  children: ReactNode;
  /** Required: the row is one button, so its content is read as this name. */
  accessibilityLabel: string;
  title?: never;
  description?: never;
}

export type ListRowProps = TitledListRowProps | CustomListRowProps;

/** One link in a ListGroup: the whole row is a single labelled button, with a chevron at the end. */
export function ListRow({
  title,
  description,
  icon,
  onPress,
  accessibilityLabel,
  accessibilityHint,
  testID,
  children,
}: ListRowProps) {
  return (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? title}
      accessibilityHint={accessibilityHint}
      onPress={onPress}
      style={({ pressed }) => [styles.row, pressed ? styles.pressed : null]}
    >
      {icon ? (
        <View style={styles.icon}>
          <Icon name={icon} size={20} color={colors.primary} />
        </View>
      ) : null}
      <View style={styles.words}>
        {children ?? (
          <>
            <AppText variant="bodyStrong">{title}</AppText>
            {description ? (
              <AppText variant="caption" tone="secondary">
                {description}
              </AppText>
            ) : null}
          </>
        )}
      </View>
      <Icon name="chevron-forward" size={18} color={colors.textMuted} />
    </Pressable>
  );
}

const ICON = 36;

const styles = StyleSheet.create({
  group: {
    borderRadius: radii.lg,
    borderWidth: borderWidths.hairline,
    borderColor: colors.borderSoft,
    backgroundColor: colors.surface,
    overflow: 'hidden',
  },
  divider: { height: borderWidths.hairline, backgroundColor: colors.borderSoft, marginStart: spacing.lg },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    minHeight: minTouchTarget + spacing.sm,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
  },
  // Pressed rows tint rather than fade, so the words stay fully readable under the finger.
  pressed: { backgroundColor: colors.surfaceMuted },
  icon: {
    width: ICON,
    height: ICON,
    borderRadius: radii.md,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.primarySoft,
  },
  words: { flex: 1, gap: spacing.xxs },
});
