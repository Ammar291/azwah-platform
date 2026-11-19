import { Children, isValidElement, useState, type ReactNode } from 'react';
import { StyleSheet, useWindowDimensions, View, type LayoutChangeEvent } from 'react-native';

import { ButtonRowContext } from './ButtonRowContext';
import { spacing } from './tokens';

export interface ButtonRowProps {
  children: ReactNode;
  /**
   * The narrowest a button may be at 100% text before the row stacks instead: room for its padding and a word or two.
   * Scaled by the system text size. The default fits three one-word buttons on a 360-point phone.
   */
  minButtonWidth?: number;
  testID?: string;
}

const DEFAULT_MIN_BUTTON_WIDTH = 88;

/**
 * Buttons that belong together (Previous and Next; Low, Medium and High; Mute and End). Side by side, each takes an
 * equal share of the width with compact padding; when they would not fit (a narrow phone, long labels in Arabic, large
 * system text) they stack, full width, in the same order. Start and end follow the language direction.
 */
export function ButtonRow({ children, minButtonWidth = DEFAULT_MIN_BUTTON_WIDTH, testID = 'button-row' }: ButtonRowProps) {
  const { width, fontScale } = useWindowDimensions();
  const [measured, setMeasured] = useState<number | null>(null);
  const items = Children.toArray(children).filter(isValidElement);
  // Before the first layout (and in tests, which never lay out): the window less the screen's side padding.
  const available = measured ?? width - 2 * spacing.xl;
  const needed = items.length * minButtonWidth * Math.max(1, fontScale) + (items.length - 1) * spacing.sm;
  const sideBySide = items.length > 1 && needed <= available;

  const onLayout = (event: LayoutChangeEvent) => {
    const next = event.nativeEvent.layout.width;
    if (next !== measured) setMeasured(next);
  };

  return (
    <ButtonRowContext.Provider value={sideBySide}>
      <View testID={testID} onLayout={onLayout} style={sideBySide ? styles.row : styles.column}>
        {items.map((item, index) => (
          <View key={item.key ?? index} testID={`${testID}-cell`} style={sideBySide ? styles.cell : null}>
            {item}
          </View>
        ))}
      </View>
    </ButtonRowContext.Provider>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'stretch', gap: spacing.sm },
  // flexBasis 0: equal shares whatever each label's length, so the buttons line up.
  cell: { flexGrow: 1, flexShrink: 1, flexBasis: 0 },
  column: { gap: spacing.sm },
});
