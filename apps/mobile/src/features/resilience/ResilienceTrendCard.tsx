import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, View, type LayoutChangeEvent } from 'react-native';
import Svg, { Circle, Line, Polyline, Rect } from 'react-native-svg';

import { useResilienceTrend } from '@/api/participant/queries';
import type { ResilienceTrend, ResilienceTrendPoint, ScoreScale } from '@/api/participant/schemas';
import { ProblemState } from '@/components/ProblemState';
import { roundOf } from '@/components/roundLabel';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { DetailRow } from '@/design-system/DetailRow';
import { useIsRTLLayout } from '@/design-system/LayoutDirection';
import { LoadingState } from '@/design-system/LoadingState';
import { Notice } from '@/design-system/Notice';
import { Section } from '@/design-system/Section';
import { borderWidths, colors, minTouchTarget, spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { axisTicks, bandExtents, runsOf, toSvgX, xOf, yOf, type PlotBox } from './trendGeometry';

/** Chart sizes in points. The plot starts after the axis labels and stays clear of both edges for the markers. */
const CHART_HEIGHT = 200;
const AXIS_WIDTH = 40;
const EDGE_INSET = 16;
const PLOT_TOP = 12;
const PLOT_BOTTOM = 12;
/** Until the card is measured; the first layout replaces it. */
const FALLBACK_WIDTH = 320;
const MARKER_RADIUS = 5;
const SELECTED_RADIUS = 7;
/** Bands shorter than this carry no name inside the plot; the list below names every band. */
const MIN_BAND_LABEL_HEIGHT = 24;

/**
 * "Your resilience over time" (D-132): the household assessment's recorded totals, one point per round, oldest at the
 * start, on the approved scale with its bands. Everything drawn is the server's; the app computes no score, band or
 * change. Only the head of household gets the values (D-103); anyone else is told the chart is the head's.
 */
export function ResilienceTrendCard() {
  const { t } = useLocalization();
  const trend = useResilienceTrend();

  let body;
  if (trend.isPending) {
    body = <LoadingState label={t('resilience.trend.loading')} />;
  } else if (trend.error || !trend.data) {
    body = <ProblemState error={trend.error} variant="inline" onRetry={() => void trend.refetch()} />;
  } else {
    body = <TrendBody trend={trend.data} />;
  }

  return (
    <Section title={t('resilience.trend.title')}>
      {body}
    </Section>
  );
}

function TrendBody({ trend }: { trend: ResilienceTrend }) {
  const { t, formatNumber } = useLocalization();
  if (trend.scoreVisibility === 'head_of_household_only') {
    return (
      <Notice
        tone="info"
        title={t('resilience.trend.headOnly.title')}
        message={t('resilience.trend.headOnly.message')}
        testID="trend-head-only"
      />
    );
  }
  const notShown =
    trend.notShownCount > 0 ? (
      <AppText variant="caption" tone="muted">
        {t('resilience.trend.notShown', { count: formatNumber(trend.notShownCount) })}
      </AppText>
    ) : null;
  if (!trend.scale || trend.points.length === 0) {
    return (
      <AppCard variant="muted" testID="trend-empty">
        <AppText variant="bodyStrong">{t('resilience.trend.empty.title')}</AppText>
        <AppText tone="secondary">{t('resilience.trend.empty.message')}</AppText>
        {notShown}
      </AppCard>
    );
  }
  return <TrendChart points={trend.points} scale={trend.scale} notShown={notShown} />;
}

interface TrendChartProps {
  points: readonly ResilienceTrendPoint[];
  scale: ScoreScale;
  notShown: ReactNode;
}

function TrendChart({ points, scale, notShown }: TrendChartProps) {
  const { t, formatDate, formatNumber } = useLocalization();
  const rtl = useIsRTLLayout();
  const [width, setWidth] = useState(FALLBACK_WIDTH);
  // The round shown above the chart: the latest until the person chooses another, so a new round shows once it arrives.
  const [chosenId, setChosenId] = useState<string | null>(null);
  const [listShown, setListShown] = useState(false);

  const latest = points[points.length - 1];
  const current = points.find((point) => point.sessionId === chosenId) ?? latest;
  const band = useBandName();
  const valueOf = (point: ResilienceTrendPoint) =>
    point.tierCode
      ? t('resilience.trend.scoreWithBand', { score: formatNumber(point.totalScore), band: band(point.tierCode) })
      : formatNumber(point.totalScore);
  const box: PlotBox = {
    start: AXIS_WIDTH + EDGE_INSET,
    top: PLOT_TOP,
    width: Math.max(1, width - AXIS_WIDTH - 2 * EDGE_INSET),
    height: CHART_HEIGHT - PLOT_TOP - PLOT_BOTTOM,
  };
  const plotStart = AXIS_WIDTH;
  const plotEnd = width;
  const svgX = (fromStart: number) => toSvgX(fromStart, width, rtl);
  const at = (index: number) => {
    const point = points[index];
    return { x: xOf(index, points.length, box), y: point ? yOf(point.totalScore, scale, box) : 0 };
  };
  const segments = new Set(points.map((point) => point.segment)).size;
  const onLayout = (event: LayoutChangeEvent) => {
    const measured = Math.round(event.nativeEvent.layout.width);
    if (measured > 0 && measured !== width) {
      setWidth(measured);
    }
  };

  return (
    <AppCard testID="trend-chart">
      {current ? (
        <View accessible accessibilityLiveRegion="polite" testID="trend-selected">
          <AppText variant="label" tone="secondary">
            {current === latest ? t('resilience.trend.latest') : t('resilience.trend.selectedRound')}
          </AppText>
          <View style={styles.figure}>
            <AppText variant="title">{formatNumber(current.totalScore)}</AppText>
            {current.tierCode ? (
              <AppText variant="bodyStrong" tone="secondary">
                {band(current.tierCode)}
              </AppText>
            ) : null}
          </View>
          <AppText variant="caption" tone="muted">
            {t('resilience.trend.roundOn', { wave: roundOf(current.waveCode), date: formatDate(current.submittedAt) })}
          </AppText>
        </View>
      ) : null}

      <View style={styles.chart} onLayout={onLayout}>
        {/* The bands and their gridlines, under everything else. */}
        <Svg width={width} height={CHART_HEIGHT} style={StyleSheet.absoluteFill} pointerEvents="none">
          {bandExtents(scale).map((extent, index) => {
            const top = yOf(extent.to, scale, box);
            const bottom = yOf(extent.from, scale, box);
            return index % 2 === 1 ? (
              <Rect
                key={extent.code}
                x={Math.min(svgX(plotStart), svgX(plotEnd))}
                y={top}
                width={plotEnd - plotStart}
                height={bottom - top}
                fill={colors.surfaceMuted}
              />
            ) : null;
          })}
          {[scale.minimum, scale.maximum, ...bandExtents(scale).slice(1).map((extent) => extent.from)].map((value) => (
            <Line
              key={`grid-${value}`}
              x1={svgX(plotStart)}
              x2={svgX(plotEnd)}
              y1={yOf(value, scale, box)}
              y2={yOf(value, scale, box)}
              stroke={colors.border}
              strokeWidth={borderWidths.hairline}
            />
          ))}
        </Svg>

        {/* The axis labels at the start edge and the band names at the end edge, in the reader's language. */}
        <View style={StyleSheet.absoluteFill} pointerEvents="none" accessible={false} importantForAccessibility="no-hide-descendants">
          {axisTicks(scale).map((value) => (
            <View key={`tick-${value}`} style={[styles.tick, { top: yOf(value, scale, box) - 10 }]}>
              <AppText variant="caption" tone="muted" maxFontSizeMultiplier={1.3}>
                {formatNumber(value)}
              </AppText>
            </View>
          ))}
          {bandExtents(scale).map((extent) => {
            const top = yOf(extent.to, scale, box);
            return yOf(extent.from, scale, box) - top >= MIN_BAND_LABEL_HEIGHT ? (
              <View key={`band-${extent.code}`} style={[styles.bandName, { top: top + 2 }]}>
                <AppText variant="caption" tone="muted" maxFontSizeMultiplier={1.3}>
                  {band(extent.code)}
                </AppText>
              </View>
            ) : null;
          })}
        </View>

        {/* The family's line and markers, one run per segment; the chosen round's marker is larger. */}
        <View
          style={StyleSheet.absoluteFill}
          accessible
          accessibilityRole="image"
          accessibilityLabel={t('resilience.trend.chartLabel', {
            count: formatNumber(points.length),
            min: formatNumber(scale.minimum),
            max: formatNumber(scale.maximum),
            value: latest ? valueOf(latest) : '',
            date: latest ? formatDate(latest.submittedAt) : '',
          })}
          testID="trend-plot"
        >
          <Svg width={width} height={CHART_HEIGHT} pointerEvents="none">
            {runsOf(points).map((run) =>
              run.length > 1 ? (
                <Polyline
                  key={`run-${run[0]}`}
                  points={run.map((index) => `${svgX(at(index).x)},${at(index).y}`).join(' ')}
                  fill="none"
                  stroke={colors.primary}
                  strokeWidth={2}
                  strokeLinejoin="round"
                  strokeLinecap="round"
                />
              ) : null,
            )}
            {points.map((point, index) => (
              <Circle
                key={point.sessionId}
                cx={svgX(at(index).x)}
                cy={at(index).y}
                r={point === current ? SELECTED_RADIUS : MARKER_RADIUS}
                fill={colors.primary}
                stroke={colors.surface}
                strokeWidth={2}
              />
            ))}
          </Svg>
        </View>

        {/* A touch target over each marker, so a round can be chosen by tap or by a screen reader. */}
        {points.map((point, index) => (
          <Pressable
            key={`target-${point.sessionId}`}
            accessibilityRole="button"
            accessibilityLabel={t('resilience.trend.point', {
              wave: roundOf(point.waveCode),
              date: formatDate(point.submittedAt),
              value: valueOf(point),
            })}
            accessibilityHint={t('resilience.trend.pointHint')}
            accessibilityState={{ selected: point === current }}
            onPress={() => setChosenId(point.sessionId)}
            hitSlop={spacing.xs}
            style={[
              styles.target,
              { start: at(index).x - minTouchTarget / 2, top: at(index).y - minTouchTarget / 2 },
            ]}
          />
        ))}
      </View>

      <View
        style={[styles.dates, points.length === 1 ? styles.datesSingle : null]}
        accessible={false}
        importantForAccessibility="no-hide-descendants"
      >
        <AppText variant="caption" tone="muted">
          {points[0] ? formatDate(points[0].submittedAt, 'monthYear') : ''}
        </AppText>
        {points.length > 1 && latest ? (
          <AppText variant="caption" tone="muted">
            {formatDate(latest.submittedAt, 'monthYear')}
          </AppText>
        ) : null}
      </View>

      {points.length === 1 ? (
        <AppText variant="caption" tone="muted">
          {t('resilience.trend.single')}
        </AppText>
      ) : null}
      {segments > 1 ? (
        <AppText variant="caption" tone="muted">
          {t('resilience.trend.rulesChanged')}
        </AppText>
      ) : null}
      {notShown}

      <AppButton
        label={listShown ? t('resilience.trend.hideList') : t('resilience.trend.showList')}
        variant="tertiary"
        onPress={() => setListShown((shown) => !shown)}
        accessibilityState={{ expanded: listShown }}
      />
      {listShown ? (
        <View style={styles.list} testID="trend-list">
          {[...points].reverse().map((point) => (
            <DetailRow
              key={point.sessionId}
              layout="inline"
              label={t('resilience.trend.roundOn', { wave: roundOf(point.waveCode), date: formatDate(point.submittedAt) })}
              value={valueOf(point)}
            />
          ))}
        </View>
      ) : null}
    </AppCard>
  );
}

/** A band's name in the reader's language when the specification words it (D-103); any other code as recorded. */
function useBandName() {
  const { t, i18n } = useTranslation();
  return (code: string) => (i18n.exists(`result.tiers.${code}`) ? t(`result.tiers.${code}`) : code);
}

const styles = StyleSheet.create({
  figure: { flexDirection: 'row', alignItems: 'baseline', gap: spacing.sm, flexWrap: 'wrap' },
  chart: { height: CHART_HEIGHT },
  tick: { position: 'absolute', start: 0, width: AXIS_WIDTH - spacing.sm, alignItems: 'flex-end' },
  bandName: { position: 'absolute', end: spacing.xs },
  target: { position: 'absolute', width: minTouchTarget, height: minTouchTarget, borderRadius: minTouchTarget / 2 },
  dates: { flexDirection: 'row', justifyContent: 'space-between', gap: spacing.md, marginStart: AXIS_WIDTH },
  datesSingle: { justifyContent: 'center' },
  list: { gap: spacing.sm },
});
