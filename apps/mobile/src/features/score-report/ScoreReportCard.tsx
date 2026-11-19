import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useTranslation } from 'react-i18next';

import { useAssistantAvailability } from '@/api/participant/queries';
import type { ScoreReport } from '@/api/participant/schemas';
import { useSingleFlight } from '@/components/useSingleFlight';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { Notice } from '@/design-system/Notice';
import { spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';
import { useAppLock } from '@/security/AppLockContext';
import { isShareInterrupted } from '@/storage/exportFiles';

import { NextStepsFlow } from './NextStepsFlow';
import { reportDocument, reportFileName, type ReportDocumentWords } from './reportDocument';
import { expoReportFiles, type ReportFiles } from './reportFiles';
import { useFinishReport, useKeepReport, useScoreReport, type RecordedScore } from './useScoreReport';

export interface ScoreReportCardProps {
  sessionId: string;
  /** The total, band and scale the server recorded. */
  recorded: RecordedScore;
  /** Tests pass a fake for the phone's printer and folders. */
  files?: ReportFiles;
}

type DownloadState = 'idle' | 'saving' | 'saved' | 'interrupted' | 'failed' | 'unavailable';

/**
 * The written report on the recorded score (D-112, D-113). If the person agreed to have it written as they answered, it
 * is already here, joined with the score at once. Otherwise they can ask for it, after being told their answers go to an
 * AI model; nothing is sent by opening the screen. It is labelled as a model's writing, sits apart from the recorded score
 * and changes nothing. Without a model the card says only that no explanation exists yet.
 */
export function ScoreReportCard({ sessionId, recorded, files = expoReportFiles }: ScoreReportCardProps) {
  const { t } = useLocalization();
  const availability = useAssistantAvailability();
  const state = useScoreReport(sessionId, recorded);
  const { loaded, report, writtenAhead, creating, problem, create } = state;
  const [finishFailed, setFinishFailed] = useState(false);
  const [finished, setFinished] = useState<ScoreReport | null>(null);
  const shown = report ?? finished;

  if (!loaded) return null;
  if (shown) {
    return <ReportView report={shown} files={files} creating={creating} problem={problem} onCreateAgain={create} />;
  }
  if (writtenAhead && !finishFailed) {
    return (
      <FinishingReport
        sessionId={sessionId}
        recorded={recorded}
        onFinished={setFinished}
        onFailed={() => setFinishFailed(true)}
      />
    );
  }
  if (!availability.data?.scoreReport) {
    return null;
  }
  return (
    <AppCard testID="report-offer">
      <AppText variant="heading" accessibilityRole="header">
        {t('scoreReport.title')}
      </AppText>
      <AppText>{t('scoreReport.offer')}</AppText>
      <ReportProblemNotice problem={problem} />
      <AppButton label={t('scoreReport.create')} loading={creating} onPress={create} />
      {creating ? (
        <AppText variant="caption" tone="muted" accessibilityLiveRegion="polite">
          {t('scoreReport.creating')}
        </AppText>
      ) : null}
    </AppCard>
  );
}

/** The parts written as the person answered, joined with the recorded score. Usually this shows for no time at all. */
function FinishingReport({
  sessionId,
  recorded,
  onFinished,
  onFailed,
}: {
  sessionId: string;
  recorded: RecordedScore;
  onFinished: (report: ScoreReport) => void;
  onFailed: () => void;
}) {
  const { t } = useLocalization();
  const keep = useKeepReport();
  const state = useFinishReport(sessionId, recorded, (report) => {
    onFinished(report);
    keep(sessionId, report);
  });
  useEffect(() => {
    if (state === 'not_finished') onFailed();
    // `onFailed` comes from a parent render; only the state matters.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state]);
  return (
    <AppCard testID="report-finishing">
      <AppText variant="heading" accessibilityRole="header">
        {t('scoreReport.title')}
      </AppText>
      <AppText accessibilityLiveRegion="polite">{t('scoreReport.finishing')}</AppText>
    </AppCard>
  );
}

function ReportProblemNotice({ problem }: { problem: ReturnType<typeof useScoreReport>['problem'] }) {
  const { t } = useLocalization();
  return problem ? <Notice tone="warning" message={t(`scoreReport.problems.${problem}`)} announce /> : null;
}

function ReportView({
  report,
  files,
  creating,
  problem,
  onCreateAgain,
}: {
  report: ScoreReport;
  files: ReportFiles;
  creating: boolean;
  problem: ReturnType<typeof useScoreReport>['problem'];
  onCreateAgain: () => void;
}) {
  const { t, formatDate } = useLocalization();
  const generatedOn = formatDate(report.generatedAt, 'dateTime');

  return (
    <AppCard testID="report">
      {/* The badge marks it as AI writing once; the date stands alone under the title (D-145). */}
      <Badge tone="info" label={t('scoreReport.badge')} />
      <AppText variant="heading" accessibilityRole="header">
        {t('scoreReport.title')}
      </AppText>
      <AppText variant="caption" tone="muted">
        {generatedOn}
      </AppText>
      <AppText>{report.summary}</AppText>

      <PointSection title={t('scoreReport.strengths')} points={report.strengths} />
      <PointSection title={t('scoreReport.weaknesses')} points={report.weaknesses} />

      <NextStepsFlow steps={report.nextSteps} />

      <Notice tone="info" message={t('scoreReport.notice')} />
      <AppText variant="label">{t('scoreReport.emergency')}</AppText>

      <DownloadButton report={report} files={files} generatedOn={generatedOn} />

      <ReportProblemNotice problem={problem} />
      <AppButton label={t('scoreReport.createAgain')} variant="secondary" loading={creating} onPress={onCreateAgain} />
    </AppCard>
  );
}

/** "Written by AI, <date>": the provider's model name is not shown (D-142). */
function useWrittenBy(generatedOn: string): string {
  const { t } = useLocalization();
  return t('scoreReport.writtenOn', { date: generatedOn });
}

function PointSection({ title, points }: { title: string; points: ScoreReport['strengths'] }) {
  if (points.length === 0) return null;
  return (
    <View style={styles.section}>
      <AppText variant="bodyStrong" accessibilityRole="header">
        {title}
      </AppText>
      {points.map((point, index) => (
        <View key={`${index}-${point.title}`} style={styles.entry}>
          <AppText variant="label">{point.title}</AppText>
          <AppText tone="secondary">{point.detail}</AppText>
        </View>
      ))}
    </View>
  );
}

/** "Download": the PDF, made on the phone from the words on screen, so it works offline and sends nothing anywhere. */
function DownloadButton({ report, files, generatedOn }: { report: ScoreReport; files: ReportFiles; generatedOn: string }) {
  const { t, formatNumber, formatDate } = useLocalization();
  const { i18n } = useTranslation();
  const { mayRevealContent } = useAppLock();
  const once = useSingleFlight();
  const [state, setState] = useState<DownloadState>('idle');
  const writtenBy = useWrittenBy(generatedOn);

  const download = async () => {
    setState('saving');
    if (!(await files.isAvailable())) {
      setState('unavailable');
      return;
    }
    const words: ReportDocumentWords = {
      title: t('scoreReport.title'),
      generatedOn: writtenBy,
      totalLabel: t('result.totalScore'),
      bandLabel: t('result.tier'),
      bandName: (code) => (i18n.exists(`result.tiers.${code}`) ? t(`result.tiers.${code}`) : code),
      scaleTitle: t('result.scale.title'),
      range: (min, max) => t('result.scale.range', { min: formatNumber(min), max: formatNumber(max) }),
      yourFamily: t('result.scale.yourFamily', { total: formatNumber(report.totalScore) }),
      summary: t('scoreReport.summary'),
      strengths: t('scoreReport.strengths'),
      weaknesses: t('scoreReport.weaknesses'),
      flowTitle: t('scoreReport.flow.title'),
      flowStep: (number, area) => t('scoreReport.flow.step', { number: formatNumber(number), area }),
      whereToGo: t('scoreReport.flow.whereToGo'),
      noAuthorities: t('scoreReport.flow.noAuthorities'),
      emirate: (code) => t(`emergency.coverage.${code}`),
      separator: t('services.authorities.separator'),
      privateProvider: t('services.authorities.private'),
      websiteLabel: t('scoreReport.flow.websiteLabel'),
      phoneLabel: t('scoreReport.flow.phoneLabel'),
      checked: (date) => t('scoreReport.flow.checked', { date: formatDate(date, 'date') }),
      flowNotice: t('scoreReport.flow.notice'),
      notice: t('scoreReport.notice'),
      emergency: t('scoreReport.emergency'),
      formatNumber,
    };
    try {
      const outcome = await files.savePdf(reportFileName(report.generatedAt), reportDocument(report, words), {
        dialogTitle: t('scoreReport.shareTitle'),
        mayShare: mayRevealContent,
      });
      setState(outcome === 'saved' ? 'saved' : 'idle');
    } catch (error) {
      setState(isShareInterrupted(error) ? 'interrupted' : 'failed');
    }
  };

  return (
    <View style={styles.section}>
      <AppText variant="caption" tone="muted">
        {t('scoreReport.downloadNote')}
      </AppText>
      {state === 'saved' ? <Notice tone="success" message={t('scoreReport.download.saved')} announce /> : null}
      {state === 'interrupted' ? <Notice tone="warning" message={t('scoreReport.download.interrupted')} announce /> : null}
      {state === 'failed' ? <Notice tone="warning" message={t('scoreReport.download.failed')} announce /> : null}
      {state === 'unavailable' ? <Notice tone="warning" message={t('scoreReport.download.unavailable')} announce /> : null}
      <AppButton label={t('scoreReport.download.action')} loading={state === 'saving'} onPress={() => void once(download)} />
    </View>
  );
}

const styles = StyleSheet.create({
  section: { gap: spacing.sm },
  entry: { gap: spacing.xxs },
});
