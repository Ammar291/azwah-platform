import { useEffect, useState } from 'react';
import { ActivityIndicator, Animated, Easing, Image, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { AppText } from '@/design-system/AppText';
import { brand } from '@/design-system/brand';
import { Icon } from '@/design-system/Icon';
import { useIsRTLLayout } from '@/design-system/LayoutDirection';
import { colors, maxContentWidth, radii, spacing } from '@/design-system/tokens';
import { useReducedMotion } from '@/design-system/useReducedMotion';
import { useLocalization } from '@/i18n/useLocalization';

const BADGE = 88;
const LINK = 72;

export interface UaePassConnectingProps {
  visible: boolean;
}

/**
 * Shown while the app hands over to UAE PASS and while it finishes signing in afterwards (the "Connecting to UAE
 * PASS" Stitch design), as an overlay over the sign-in screen rather than a native modal, so the system browser can
 * always present above it. The browser or the UAE PASS app has its own cancel, so this screen offers none: it closes
 * when sign-in ends either way. A dot travels between the two marks unless reduced motion is on.
 */
export function UaePassConnecting({ visible }: UaePassConnectingProps) {
  const { t, language } = useLocalization();
  const reduced = useReducedMotion();
  const rtl = useIsRTLLayout();
  const [travel] = useState(() => new Animated.Value(0));

  useEffect(() => {
    if (!visible || reduced) return;
    const loop = Animated.loop(
      Animated.timing(travel, { toValue: 1, duration: 1400, easing: Easing.inOut(Easing.quad), useNativeDriver: true }),
    );
    loop.start();
    return () => loop.stop();
  }, [reduced, travel, visible]);

  const span = LINK - 10;
  const translateX = travel.interpolate({ inputRange: [0, 1], outputRange: rtl ? [0, -span] : [0, span] });

  if (!visible) {
    return null;
  }
  return (
    <View style={styles.overlay} testID="uae-pass-connecting">
      <SafeAreaView style={styles.safe} accessibilityViewIsModal>
        <View style={styles.column}>
          <View style={styles.marks} accessible={false} importantForAccessibility="no-hide-descendants">
            <View style={styles.markColumn}>
              <View style={[styles.badge, styles.azwahBadge]}>
                <Image source={brand.emblem} style={styles.emblem} resizeMode="contain" accessibilityIgnoresInvertColors />
              </View>
              <AppText variant="caption" tone="secondary">
                {t(brand.nameKey)}
              </AppText>
            </View>
            <View style={styles.link}>
              <View style={styles.dashes} />
              <Animated.View style={[styles.traveller, reduced ? null : { transform: [{ translateX }] }]} />
            </View>
            <View style={styles.markColumn}>
              <View style={[styles.badge, styles.uaePassBadge]}>
                <Icon name="fingerprint" size={36} color={colors.onPrimary} />
              </View>
              <AppText variant="caption" style={[styles.uaePassLabel, language === 'ar' ? styles.joined : null]}>
                {t('signIn.uaePass.name')}
              </AppText>
            </View>
          </View>

          <AppText variant="title" accessibilityRole="header" style={styles.centred}>
            {t('signIn.uaePass.connecting.title')}
          </AppText>
          <AppText tone="secondary" style={styles.centred}>
            {t('signIn.uaePass.connecting.message')}
          </AppText>
          <ActivityIndicator color={colors.primary} size="large" accessibilityLabel={t('signIn.uaePass.connecting.title')} />

          <View style={styles.notice}>
            <Icon name="lock" size={18} color={colors.primary} />
            <AppText variant="label" style={styles.noticeText}>
              {t('signIn.uaePass.connecting.secure')}
            </AppText>
          </View>
        </View>
      </SafeAreaView>
    </View>
  );
}

const styles = StyleSheet.create({
  overlay: { position: 'absolute', top: 0, bottom: 0, start: 0, end: 0, zIndex: 5, backgroundColor: colors.background },
  safe: { flex: 1, justifyContent: 'center' },
  column: {
    width: '100%',
    maxWidth: maxContentWidth,
    alignSelf: 'center',
    alignItems: 'center',
    gap: spacing.lg,
    padding: spacing.xl,
  },
  marks: { flexDirection: 'row', alignItems: 'center', marginBottom: spacing.lg },
  markColumn: { alignItems: 'center', gap: spacing.sm },
  badge: { width: BADGE, height: BADGE, borderRadius: BADGE / 2, alignItems: 'center', justifyContent: 'center' },
  azwahBadge: { backgroundColor: colors.surface, borderWidth: 2, borderColor: colors.accentSoft },
  uaePassBadge: { backgroundColor: '#000000' },
  uaePassLabel: { fontWeight: '700', letterSpacing: 1 },
  // Arabic letters join: spacing them apart breaks the words.
  joined: { letterSpacing: 0 },
  emblem: { width: 56, height: Math.round(56 / brand.emblemAspectRatio) },
  link: { width: LINK, height: 12, justifyContent: 'center', marginBottom: spacing.xl },
  dashes: { height: 0, borderTopWidth: 2, borderStyle: 'dashed', borderColor: colors.primaryMid },
  traveller: {
    position: 'absolute',
    start: 0,
    width: 10,
    height: 10,
    borderRadius: 5,
    backgroundColor: colors.primary,
  },
  centred: { textAlign: 'center' },
  notice: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    marginTop: spacing.lg,
    padding: spacing.lg,
    borderRadius: radii.lg,
    backgroundColor: colors.primarySoft,
    alignSelf: 'stretch',
  },
  noticeText: { flex: 1 },
});
