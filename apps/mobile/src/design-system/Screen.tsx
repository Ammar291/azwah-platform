import { useRef, type ReactNode } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { borderWidths, colors, maxContentWidth, spacing } from './tokens';

export interface ScreenProps {
  children: ReactNode;
  /** Rendered above the scrolling content, for example an AppHeader. */
  header?: ReactNode;
  /** Pinned under the content, for example the primary action of a form. */
  footer?: ReactNode;
  /** Set false for screens that manage their own layout (states that fill the screen). */
  scroll?: boolean;
  /** Keeps focused inputs and the footer above the on-screen keyboard. */
  keyboardAware?: boolean;
  /** Floats over the content (for example Home's SOS and assistant buttons); it positions itself. */
  overlay?: ReactNode;
  /** Keeps the end of the content in view as it grows, for a conversation. */
  followEnd?: boolean;
  testID?: string;
}

/**
 * Safe-area aware page. Content is limited to a readable width on tablets. Keyboard handling follows Expo's guidance:
 * padding on iOS; Android resizes the window itself.
 */
export function Screen({
  children,
  header,
  footer,
  scroll = true,
  keyboardAware = false,
  overlay,
  followEnd = false,
  testID,
}: ScreenProps) {
  const scrollView = useRef<ScrollView>(null);
  const content = scroll ? (
    <ScrollView
      ref={scrollView}
      onContentSizeChange={followEnd ? () => scrollView.current?.scrollToEnd({ animated: true }) : undefined}
      contentContainerStyle={styles.scrollContent}
      keyboardShouldPersistTaps="handled"
      keyboardDismissMode="interactive"
    >
      <View style={styles.column}>{children}</View>
    </ScrollView>
  ) : (
    <View style={[styles.fill, styles.staticContent]}>
      <View style={[styles.column, styles.fill]}>{children}</View>
    </View>
  );

  const body = (
    <>
      {content}
      {footer ? (
        <View style={styles.footer}>
          <View style={styles.column}>{footer}</View>
        </View>
      ) : null}
      {overlay}
    </>
  );

  return (
    <SafeAreaView style={styles.safeArea} edges={['top', 'bottom', 'left', 'right']} testID={testID}>
      {header}
      {keyboardAware ? (
        <KeyboardAvoidingView style={styles.fill} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
          {body}
        </KeyboardAvoidingView>
      ) : (
        <View style={styles.fill}>{body}</View>
      )}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: colors.background },
  fill: { flex: 1 },
  scrollContent: { padding: spacing.xl, paddingBottom: spacing.xxl },
  staticContent: { padding: spacing.xl },
  column: { width: '100%', maxWidth: maxContentWidth, alignSelf: 'center', gap: spacing.lg },
  footer: {
    paddingHorizontal: spacing.xl,
    paddingVertical: spacing.lg,
    borderTopWidth: borderWidths.hairline,
    borderTopColor: colors.borderSoft,
    backgroundColor: colors.background,
  },
});
