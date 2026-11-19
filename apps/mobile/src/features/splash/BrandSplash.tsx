import { useEffect, useState } from 'react';
import { Animated, Easing, Image, StyleSheet, View } from 'react-native';

import { AppText } from '@/design-system/AppText';
import { brand } from '@/design-system/brand';
import { colors, radii, spacing } from '@/design-system/tokens';
import { useReducedMotion } from '@/design-system/useReducedMotion';
import { useLocalization } from '@/i18n/useLocalization';

/** The emblem as drawn inside its white disc. */
const EMBLEM_WIDTH = 128;
const EMBLEM_HEIGHT = Math.round(EMBLEM_WIDTH / brand.emblemAspectRatio);
const RING_SIZE = 220;
/** A faint halo of sand behind the emblem: stacked circles fade towards the edge (React Native draws no gradients). */
const GLOW = [
  { size: 340, opacity: 0.3 },
  { size: 300, opacity: 0.4 },
  { size: 264, opacity: 0.6 },
] as const;
const PROGRESS_WIDTH = 120;

/** The welcome scene, then the wordmark scene; the app opens after both, once it is ready. */
export const SPLASH_TIMING = { welcomeMs: 1100, wordmarkMs: 900, exitMs: 320 } as const;

export interface BrandSplashProps {
  /** The first screen can be shown (start-up finished, sign-in state known). */
  ready: boolean;
  /** Called once the splash has faded out; the parent then unmounts it. */
  onDone: () => void;
}

/**
 * The opening sequence over the app's first screen, in two scenes from the Stitch designs
 * (not included in this repository): "Welcome to Azwah" with the emblem inside a turning gold ring and a progress line,
 * then the wordmark with the meaning and the four pillars. It takes over from the native splash (the same emblem on
 * the same paper colour), stays at least for both scenes, waits for `ready`, then fades out. With reduced motion
 * nothing moves or turns: the scenes only fade.
 */
export function BrandSplash({ ready, onDone }: BrandSplashProps) {
  const { t } = useLocalization();
  const reduced = useReducedMotion();
  const [minimumShown, setMinimumShown] = useState(false);

  const [enter] = useState(() => new Animated.Value(0));
  const [ring] = useState(() => new Animated.Value(0));
  const [progress] = useState(() => new Animated.Value(0));
  const [scene] = useState(() => new Animated.Value(0));
  const [exit] = useState(() => new Animated.Value(1));

  useEffect(() => {
    const total = SPLASH_TIMING.welcomeMs + SPLASH_TIMING.wordmarkMs;
    const sequence = Animated.parallel([
      Animated.timing(enter, { toValue: 1, duration: 520, easing: Easing.out(Easing.cubic), useNativeDriver: true }),
      Animated.timing(progress, { toValue: 1, duration: total, easing: Easing.inOut(Easing.quad), useNativeDriver: false }),
      Animated.sequence([
        Animated.delay(SPLASH_TIMING.welcomeMs),
        Animated.timing(scene, { toValue: 1, duration: 420, easing: Easing.inOut(Easing.quad), useNativeDriver: true }),
      ]),
    ]);
    sequence.start();
    const turning = Animated.loop(
      Animated.timing(ring, { toValue: 1, duration: 2400, easing: Easing.linear, useNativeDriver: true }),
    );
    turning.start();
    const timer = setTimeout(() => setMinimumShown(true), total);
    return () => {
      sequence.stop();
      turning.stop();
      clearTimeout(timer);
    };
  }, [enter, progress, ring, scene]);

  useEffect(() => {
    if (!ready || !minimumShown) return;
    const fade = Animated.timing(exit, { toValue: 0, duration: SPLASH_TIMING.exitMs, useNativeDriver: true });
    fade.start(({ finished }) => {
      if (finished) onDone();
    });
    return () => fade.stop();
  }, [exit, minimumShown, onDone, ready]);

  const emblemScale = reduced ? 1 : enter.interpolate({ inputRange: [0, 1], outputRange: [0.94, 1] });
  const rise = (from: number) => (reduced ? 0 : enter.interpolate({ inputRange: [0, 1], outputRange: [from, 0] }));
  const rotate = reduced ? '0deg' : ring.interpolate({ inputRange: [0, 1], outputRange: ['0deg', '360deg'] });
  const welcomeOpacity = scene.interpolate({ inputRange: [0, 1], outputRange: [1, 0] });
  const wordmarkOpacity = scene;
  const emblemShift = reduced ? 0 : scene.interpolate({ inputRange: [0, 1], outputRange: [0, -28] });

  return (
    <Animated.View
      style={[styles.overlay, { opacity: exit }]}
      accessible
      accessibilityRole="progressbar"
      accessibilityLabel={t('splash.accessibilityLabel')}
      testID="brand-splash"
    >
      <View style={styles.centre}>
        <Animated.View style={[styles.mark, { transform: [{ translateY: emblemShift }] }]}>
          {GLOW.map(({ size, opacity }) => (
            <Animated.View
              key={size}
              style={[
                styles.glow,
                {
                  width: size,
                  height: size,
                  borderRadius: size / 2,
                  top: (RING_SIZE - size) / 2,
                  start: (RING_SIZE - size) / 2,
                  opacity: Animated.multiply(enter, opacity),
                },
              ]}
            />
          ))}
          <Animated.View style={[styles.ring, { opacity: enter, transform: [{ rotate }] }]}>
            <View style={styles.ringDot} />
          </Animated.View>
          <Animated.View style={[styles.disc, { transform: [{ scale: emblemScale }] }]}>
            <Image source={brand.emblem} style={styles.emblem} resizeMode="contain" accessibilityIgnoresInvertColors />
          </Animated.View>
        </Animated.View>

        <View style={styles.scenes}>
          <Animated.View
            style={[styles.scene, { opacity: Animated.multiply(enter, welcomeOpacity), transform: [{ translateY: rise(12) }] }]}
          >
            <AppText variant="display" style={styles.centred}>
              {t('splash.welcome')}
            </AppText>
            <Divider />
            <AppText tone="secondary" style={styles.centred}>
              {t('app.meaning')}
            </AppText>
          </Animated.View>
          <Animated.View style={[styles.scene, styles.overlapping, { opacity: wordmarkOpacity }]}>
            <View style={styles.wordmark}>
              <View style={styles.rule} />
              <AppText variant="display" tone="primary" style={[styles.centred, styles.wordmarkText]}>
                {t(brand.nameKey)}
              </AppText>
              <View style={styles.rule} />
            </View>
            <AppText tone="secondary" style={styles.centred}>
              {t('app.meaning')}
            </AppText>
          </Animated.View>
        </View>
      </View>

      <View style={styles.footer}>
        <Animated.View style={[styles.pillars, { opacity: wordmarkOpacity }]}>
          {brand.pillars.map((pillar, index) => (
            <View key={pillar.key} style={styles.pillar}>
              {index > 0 ? <View style={styles.pillarDot} /> : null}
              <AppText variant="caption" tone="secondary">
                {t(`pillars.${pillar.key}`)}
              </AppText>
            </View>
          ))}
        </Animated.View>
        <AppText variant="caption" tone="muted" style={styles.centred}>
          {t('splash.preparing')}
        </AppText>
        <View style={styles.track}>
          <Animated.View
            style={[
              styles.fill,
              { width: progress.interpolate({ inputRange: [0, 1], outputRange: [0, PROGRESS_WIDTH] }) },
            ]}
          />
        </View>
      </View>
    </Animated.View>
  );
}

function Divider() {
  return (
    <View style={styles.divider}>
      <View style={styles.dividerRule} />
      <View style={styles.dividerDot} />
      <View style={styles.dividerRule} />
    </View>
  );
}

const styles = StyleSheet.create({
  overlay: { position: 'absolute', top: 0, bottom: 0, start: 0, end: 0, backgroundColor: colors.background, zIndex: 10 },
  glow: { position: 'absolute', backgroundColor: colors.accentSoft },
  centre: { flex: 1, alignItems: 'center', justifyContent: 'center', paddingHorizontal: spacing.xl, gap: spacing.xl },
  mark: { width: RING_SIZE, height: RING_SIZE, alignItems: 'center', justifyContent: 'center' },
  ring: {
    position: 'absolute',
    width: RING_SIZE,
    height: RING_SIZE,
    borderRadius: RING_SIZE / 2,
    borderWidth: 1.5,
    borderColor: colors.secondary,
    borderTopColor: 'transparent',
    borderEndColor: colors.accentSoft,
  },
  ringDot: {
    position: 'absolute',
    top: RING_SIZE * 0.146 - 3,
    start: RING_SIZE * 0.146 - 3,
    width: 6,
    height: 6,
    borderRadius: 3,
    backgroundColor: colors.secondary,
  },
  disc: {
    width: RING_SIZE - 28,
    height: RING_SIZE - 28,
    borderRadius: (RING_SIZE - 28) / 2,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  // Explicit size: Android ignores aspectRatio on an Image sized by width alone.
  emblem: { width: EMBLEM_WIDTH, height: EMBLEM_HEIGHT },
  scenes: { alignSelf: 'stretch', minHeight: 150 },
  scene: { alignItems: 'center', gap: spacing.sm },
  overlapping: { position: 'absolute', top: 0, bottom: 0, start: 0, end: 0 },
  centred: { textAlign: 'center' },
  divider: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  dividerRule: { width: 40, height: 1, backgroundColor: colors.accent },
  dividerDot: { width: 6, height: 6, borderRadius: 3, backgroundColor: colors.accent },
  wordmark: { flexDirection: 'row', alignItems: 'center', gap: spacing.lg },
  wordmarkText: { fontSize: 44, lineHeight: 58 },
  rule: { width: 48, height: 1, backgroundColor: colors.secondary },
  footer: { alignItems: 'center', gap: spacing.sm, paddingBottom: spacing.xxxl, paddingHorizontal: spacing.xl },
  pillars: { flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'center', marginBottom: spacing.md },
  pillar: { flexDirection: 'row', alignItems: 'center' },
  pillarDot: {
    width: 4,
    height: 4,
    borderRadius: 2,
    backgroundColor: colors.secondary,
    marginHorizontal: spacing.sm,
  },
  track: { width: PROGRESS_WIDTH, height: 3, borderRadius: radii.pill, backgroundColor: colors.border, overflow: 'hidden' },
  fill: { height: 3, borderRadius: radii.pill, backgroundColor: colors.primary },
});
