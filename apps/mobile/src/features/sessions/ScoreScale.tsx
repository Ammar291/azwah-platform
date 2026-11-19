import { StyleSheet, View } from 'react-native';

import type { ScoreScale as Scale } from '@/api/participant/schemas';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { borderWidths, colors, radii, spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { bandShare, scalePosition } from './scalePosition';

export interface ScoreScaleProps {
  total: number;
  /** The band the server recorded; the drawing never works it out from the total. */
  tierCode: string;
  scale: Scale;
  tierLabel: (code: string) => string;
}

const BAR_HEIGHT = 14;
const MARKER_SIZE = 20;

/** One hue from light to dark, lowest band to highest: the order carries the meaning, and words name each band. */
function bandColor(index: number, count: number): string {
  const ramp = [colors.primarySoft, colors.primaryMid, colors.primary];
  if (count <= 1) return colors.primaryMid;
  const step = Math.round((index / (count - 1)) * (ramp.length - 1));
  return ramp[step] ?? colors.primaryMid;
}

/**
 * The grading scale and where the family's total stands on it (D-111), both as the server sent them. It follows the
 * language's direction (the lowest band at the start), and the marker is explained in words for a screen reader and
 * by the "your family" label beside the band, never by colour alone.
 */
export function ScoreScale({ total, tierCode, scale, tierLabel }: ScoreScaleProps) {
  const { t, formatNumber } = useLocalization();
  const position = scalePosition(total, scale);
  const current = scale.bands.find((band) => band.code === tierCode);
  const range = (min: number, max: number) => t('result.scale.range', { min: formatNumber(min), max: formatNumber(max) });

  return (
    <View testID="result-scale" style={styles.scale}>
      <AppText variant="bodyStrong" accessibilityRole="header">
        {t('result.scale.title')}
      </AppText>

      <View
        accessible
        accessibilityRole="image"
        accessibilityLabel={t('result.scale.summary', {
          total: formatNumber(total),
          minimum: formatNumber(scale.minimum),
          maximum: formatNumber(scale.maximum),
          band: tierLabel(tierCode),
        })}
        testID="result-scale-figure"
        style={styles.figure}
      >
        <View style={styles.track}>
          {scale.bands.map((band, index) => (
            <View
              key={band.code}
              testID={`result-scale-segment-${band.code}`}
              style={[
                styles.segment,
                { flexGrow: bandShare(band, scale), backgroundColor: bandColor(index, scale.bands.length) },
                index === 0 ? styles.firstSegment : null,
                index === scale.bands.length - 1 ? styles.lastSegment : null,
              ]}
            />
          ))}
          <View testID="result-scale-marker" style={[styles.marker, { start: `${position * 100}%` }]} />
        </View>
        <View style={styles.ends}>
          <AppText variant="caption" tone="muted">
            {formatNumber(scale.minimum)}
          </AppText>
          <AppText variant="caption" tone="muted">
            {formatNumber(scale.maximum)}
          </AppText>
        </View>
      </View>

      <View style={styles.bands}>
        {scale.bands.map((band, index) => {
          const isCurrent = band === current;
          return (
            <View key={band.code} testID={`result-scale-band-${band.code}`} style={styles.band}>
              <View style={[styles.swatch, { backgroundColor: bandColor(index, scale.bands.length) }]} />
              <View style={styles.bandText}>
                <AppText variant={isCurrent ? 'bodyStrong' : 'body'}>{tierLabel(band.code)}</AppText>
                <AppText variant="caption" tone="secondary">
                  {range(band.min, band.max)}
                </AppText>
              </View>
              {isCurrent ? <Badge tone="neutral" label={t('result.scale.yourFamily', { total: formatNumber(total) })} /> : null}
            </View>
          );
        })}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  scale: { gap: spacing.md, paddingTop: spacing.sm },
  figure: { gap: spacing.xs },
  // The marker overlaps the bar, so the row is as tall as the marker.
  track: { flexDirection: 'row', alignItems: 'center', gap: 2, height: MARKER_SIZE },
  segment: {
    height: BAR_HEIGHT,
    flexBasis: 0,
    borderWidth: borderWidths.hairline,
    borderColor: colors.borderStrong,
  },
  firstSegment: { borderTopStartRadius: radii.sm, borderBottomStartRadius: radii.sm },
  lastSegment: { borderTopEndRadius: radii.sm, borderBottomEndRadius: radii.sm },
  marker: {
    position: 'absolute',
    top: 0,
    marginStart: -MARKER_SIZE / 2,
    width: MARKER_SIZE,
    height: MARKER_SIZE,
    borderRadius: radii.pill,
    backgroundColor: colors.primaryStrong,
    borderWidth: borderWidths.strong,
    borderColor: colors.surface,
  },
  ends: { flexDirection: 'row', justifyContent: 'space-between' },
  bands: { gap: spacing.sm },
  band: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, flexWrap: 'wrap' },
  swatch: {
    width: 14,
    height: 14,
    borderRadius: radii.sm,
    borderWidth: borderWidths.hairline,
    borderColor: colors.borderStrong,
  },
  bandText: { flex: 1, minWidth: 120 },
});
