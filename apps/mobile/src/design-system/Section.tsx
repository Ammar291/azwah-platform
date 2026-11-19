import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';

import { AppText } from './AppText';
import { spacing } from './tokens';

export interface SectionProps {
  title: string;
  /** Optional line under the heading. */
  description?: string;
  children: ReactNode;
}

/** A titled group on a screen. The title is a header, so screen readers can jump between sections. */
export function Section({ title, description, children }: SectionProps) {
  return (
    <View style={styles.section}>
      <View style={styles.heading}>
        <AppText variant="heading" accessibilityRole="header">
          {title}
        </AppText>
        {description ? <AppText tone="secondary">{description}</AppText> : null}
      </View>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  // More space above a heading than below it, so each section reads as its own group.
  section: { gap: spacing.md, marginTop: spacing.sm },
  heading: { gap: spacing.xxs },
});
