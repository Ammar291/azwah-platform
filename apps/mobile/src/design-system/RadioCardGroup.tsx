import { Fragment } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { AppText } from './AppText';
import { Icon } from './Icon';
import { borderWidths, colors, minTouchTarget, motion, radii, spacing, statusColors } from './tokens';

export type RadioValue = string | number;

export interface RadioCardOption<V extends RadioValue> {
  value: V;
  label: string;
  description?: string;
  /** Draws a divider before this option, for answers set apart from the scale such as "prefer not to answer". */
  separated?: boolean;
  /**
   * An answer of another kind than its neighbours (outside the scale): a muted card with quieter text until chosen,
   * so more than a hairline tells it apart. Its label, value and behaviour are those of any option.
   */
  subdued?: boolean;
}

export interface RadioCardGroupProps<V extends RadioValue> {
  /** The question; also the group's accessible name. */
  label: string;
  /** Hide the visible label when the screen already shows the question as a heading. */
  hideLabel?: boolean;
  options: readonly RadioCardOption<V>[];
  value: V | null;
  onChange: (value: V) => void;
  disabled?: boolean;
  error?: string;
  density?: 'comfortable' | 'compact';
}

/**
 * Large, accessible single-choice cards. Selection shows as a filled radio, a check mark and a thicker border, so it
 * never depends on colour alone. Options stack vertically so long Arabic labels wrap.
 */
export function RadioCardGroup<V extends RadioValue>({
  label,
  hideLabel = false,
  options,
  value,
  onChange,
  disabled = false,
  error,
  density = 'comfortable',
}: RadioCardGroupProps<V>) {
  return (
    <View accessibilityRole="radiogroup" accessibilityLabel={label} style={styles.group}>
      {hideLabel ? null : <AppText variant="bodyStrong">{label}</AppText>}
      {options.map((option) => {
        const selected = option.value === value;
        return (
          <Fragment key={String(option.value)}>
            {option.separated ? <View style={styles.divider} testID="radio-divider" /> : null}
            <Pressable
              accessibilityRole="radio"
              accessibilityLabel={option.label}
              accessibilityHint={option.description}
              accessibilityState={{ checked: selected, disabled }}
              disabled={disabled}
              onPress={() => onChange(option.value)}
              style={({ pressed }) => [
                styles.option,
                density === 'compact' ? styles.compact : null,
                option.subdued ? styles.optionSubdued : null,
                selected ? styles.optionSelected : null,
                pressed && !disabled && !selected ? styles.pressed : null,
                disabled ? styles.optionDisabled : null,
              ]}
            >
              <View style={[styles.radio, selected ? styles.radioSelected : null]}>
                {selected ? <View style={styles.radioDot} /> : null}
              </View>
              <View style={styles.text}>
                <AppText
                  variant={selected ? 'bodyStrong' : 'body'}
                  tone={option.subdued && !selected ? 'secondary' : 'default'}
                >
                  {option.label}
                </AppText>
                {option.description ? (
                  <AppText variant="caption" tone="muted">
                    {option.description}
                  </AppText>
                ) : null}
              </View>
              {selected ? <Icon name="check" size={20} color={colors.primary} /> : null}
            </Pressable>
          </Fragment>
        );
      })}
      {error ? (
        <View style={styles.error}>
          <Icon name="alert" size={16} color={statusColors.danger} />
          <AppText variant="caption" tone="danger" accessibilityRole="alert" style={styles.errorText}>
            {error}
          </AppText>
        </View>
      ) : null}
    </View>
  );
}

const RADIO = 22;

const styles = StyleSheet.create({
  group: { gap: spacing.sm },
  option: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    minHeight: minTouchTarget + spacing.md,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
    borderRadius: radii.md,
    borderWidth: borderWidths.hairline,
    borderColor: colors.borderStrong,
    backgroundColor: colors.surface,
  },
  compact: { minHeight: minTouchTarget, paddingVertical: spacing.sm },
  // Before `optionSelected`, which wins once the option is chosen.
  optionSubdued: { backgroundColor: colors.surfaceMuted },
  optionSelected: {
    borderColor: colors.primary,
    borderWidth: borderWidths.strong,
    backgroundColor: colors.primarySoft,
  },
  optionDisabled: { opacity: motion.disabledOpacity },
  pressed: { backgroundColor: colors.surfaceMuted, borderColor: colors.primary },
  radio: {
    width: RADIO,
    height: RADIO,
    borderRadius: RADIO / 2,
    borderWidth: borderWidths.strong,
    borderColor: colors.borderStrong,
    alignItems: 'center',
    justifyContent: 'center',
  },
  radioSelected: { borderColor: colors.primary },
  radioDot: { width: RADIO / 2, height: RADIO / 2, borderRadius: RADIO / 4, backgroundColor: colors.primary },
  // Lines shrink to their words and sit at the layout's start edge, so a label in the other script (العربية in
  // English, English in Arabic) lines up with its description instead of aligning to its own script's edge.
  text: { flex: 1, gap: spacing.xxs, alignItems: 'flex-start' },
  // With the group's own gap on both sides, the two kinds of answer sit clearly apart.
  divider: { height: borderWidths.hairline, backgroundColor: colors.border, marginVertical: spacing.sm },
  error: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  errorText: { flexShrink: 1 },
});
