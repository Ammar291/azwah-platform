import { Image, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView, useSafeAreaInsets } from 'react-native-safe-area-context';

import { LanguageSwitcher } from '@/components/LanguageSwitcher';
import { AppText } from '@/design-system/AppText';
import { brand } from '@/design-system/brand';
import { borderWidths, colors, maxContentWidth, minTouchTarget, radii, spacing } from '@/design-system/tokens';
import { SignInPanel } from '@/features/auth/SignInPanel';
import { UaePassConnecting } from '@/features/auth/UaePassConnecting';
import { useUaePassSignIn } from '@/features/auth/useUaePassSignIn';
import { useLocalization } from '@/i18n/useLocalization';

const HERO = 132;
/** Every pillar's artwork sits in a box this wide, so the four labels line up. */
const PILLAR_ART = 34;

/**
 * The first screen after the splash (the "Welcome & sign in" Stitch design): the Azwah name and meaning, what it is
 * for, the four pillars from the logo sheet, and a "Get started" sheet with UAE PASS. No endorsement is claimed: the
 * only other name on the screen is UAE PASS, as the way to sign in.
 */
export function WelcomeScreen() {
  const { t } = useLocalization();
  const insets = useSafeAreaInsets();
  const signIn = useUaePassSignIn();

  return (
    <SafeAreaView style={styles.safe} edges={['top', 'left', 'right']}>
      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        <View style={[styles.column, styles.top]}>
          <View style={styles.bar}>
            <View style={styles.brandRow}>
              <View style={styles.miniBadge}>
                <Image source={brand.emblem} style={styles.miniEmblem} resizeMode="contain" accessibilityIgnoresInvertColors />
              </View>
              <AppText variant="heading" tone="primary">
                {t(brand.nameKey)}
              </AppText>
            </View>
            <LanguageSwitcher variant="pill" />
          </View>

          <View style={styles.hero}>
            <View style={styles.heroDisc}>
              <Image
                source={brand.emblem}
                style={styles.heroEmblem}
                resizeMode="contain"
                accessible
                accessibilityLabel={t(brand.nameKey)}
                accessibilityIgnoresInvertColors
              />
            </View>
            <View style={styles.titleRow}>
              <View style={styles.rule} />
              <AppText variant="display" accessibilityRole="header" style={styles.centred}>
                {t(brand.nameKey)}
              </AppText>
              <View style={styles.rule} />
            </View>
            <AppText variant="heading" tone="primary" style={styles.centred}>
              {t('app.meaning')}
            </AppText>
            <AppText tone="secondary" style={styles.centred}>
              {t('welcome.intro')}
            </AppText>
          </View>

          <View style={styles.pillars} accessibilityRole="list" accessibilityLabel={t('pillars.title')}>
            {brand.pillars.map((pillar) => (
              <View key={pillar.key} style={styles.pillar} accessible accessibilityLabel={t(`pillars.${pillar.key}`)}>
                <View style={styles.pillarArt}>
                  <Image
                    source={pillar.image}
                    style={{ width: Math.min(PILLAR_ART, 22 * pillar.aspectRatio), height: 22 }}
                    resizeMode="contain"
                    accessibilityIgnoresInvertColors
                  />
                </View>
                <AppText variant="label" tone="primary" style={styles.pillarLabel}>
                  {t(`pillars.${pillar.key}`)}
                </AppText>
              </View>
            ))}
          </View>
        </View>

        <View style={[styles.sheet, { paddingBottom: spacing.xl + insets.bottom }]}>
          <View style={styles.column}>
            <View style={styles.handle} />
            <AppText variant="title" accessibilityRole="header" style={styles.centred}>
              {t('welcome.getStarted')}
            </AppText>
            <SignInPanel signIn={signIn} />
          </View>
        </View>
      </ScrollView>
      <UaePassConnecting visible={signIn.connecting} />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  scroll: { flexGrow: 1, justifyContent: 'space-between' },
  column: { width: '100%', maxWidth: maxContentWidth, alignSelf: 'center', gap: spacing.lg },
  top: { paddingHorizontal: spacing.xl, paddingTop: spacing.md, paddingBottom: spacing.xl },
  bar: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md },
  brandRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  miniBadge: {
    width: 40,
    height: 40,
    borderRadius: 20,
    backgroundColor: colors.surface,
    borderWidth: borderWidths.hairline,
    borderColor: colors.borderSoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  miniEmblem: { width: 28, height: Math.round(28 / brand.emblemAspectRatio) },
  hero: { alignItems: 'center', gap: spacing.sm, paddingTop: spacing.xs },
  heroDisc: {
    width: HERO,
    height: HERO,
    borderRadius: HERO / 2,
    backgroundColor: colors.primarySoft,
    borderWidth: borderWidths.strong,
    borderColor: colors.accentSoft,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.md,
  },
  // Explicit size: Android ignores aspectRatio on an Image sized by width alone.
  heroEmblem: { width: 88, height: Math.round(88 / brand.emblemAspectRatio) },
  titleRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.lg },
  rule: { width: 36, height: 1, backgroundColor: colors.secondary },
  centred: { textAlign: 'center' },
  // Two by two, so every pillar's name shows in full in both languages instead of shrinking to fit.
  pillars: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  pillar: {
    flexGrow: 1,
    flexBasis: '45%',
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    minHeight: minTouchTarget,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    borderRadius: radii.md,
    borderWidth: borderWidths.hairline,
    borderColor: colors.borderSoft,
    backgroundColor: colors.surface,
  },
  pillarArt: { width: PILLAR_ART, alignItems: 'center' },
  pillarLabel: { flexShrink: 1 },
  sheet: {
    backgroundColor: colors.surface,
    borderTopStartRadius: 28,
    borderTopEndRadius: 28,
    borderTopWidth: borderWidths.hairline,
    borderColor: colors.borderSoft,
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.md,
    shadowColor: colors.text,
    shadowOpacity: 0.06,
    shadowRadius: 16,
    shadowOffset: { width: 0, height: -4 },
    elevation: 6,
  },
  handle: { alignSelf: 'center', width: 40, height: 4, borderRadius: radii.pill, backgroundColor: colors.border },
});
