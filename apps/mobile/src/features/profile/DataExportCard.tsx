import { isApiError } from '@/api/client';
import { ProblemState } from '@/components/ProblemState';
import { useSingleFlight } from '@/components/useSingleFlight';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Notice } from '@/design-system/Notice';
import { useLocalization } from '@/i18n/useLocalization';

import { isShareInterrupted, type DataFileSharing } from '@/storage/exportFiles';

import { SharingUnavailableError, useDataExport } from './privacyMutations';

/**
 * "Download a copy of my data": what the file holds and that where it goes is the person's choice, said before the
 * button; then the share sheet. A phone without one, the hourly limit, a lock or a trip to the background before the
 * sheet opened, and other problems (offline among them) each say so in words.
 */
export function DataExportCard({ files }: { files?: DataFileSharing }) {
  const { t } = useLocalization();
  const exportData = useDataExport(files);
  const once = useSingleFlight();

  return (
    <AppCard testID="data-export">
      <AppText variant="bodyStrong">{t('profile.data.export.title')}</AppText>
      <AppText tone="secondary">{t('profile.data.export.contents')}</AppText>
      <AppText tone="secondary">{t('profile.data.export.yourChoice')}</AppText>
      {exportData.error ? <ExportProblem error={exportData.error} /> : null}
      <AppButton
        label={t('profile.data.export.action')}
        variant="secondary"
        loading={exportData.isPending}
        onPress={() => void once(() => exportData.mutateAsync().catch(() => undefined))}
      />
    </AppCard>
  );
}

function ExportProblem({ error }: { error: unknown }) {
  const { t } = useLocalization();
  if (error instanceof SharingUnavailableError) {
    return (
      <Notice
        tone="warning"
        title={t('profile.data.export.unavailableTitle')}
        message={t('profile.data.export.unavailable')}
        announce
      />
    );
  }
  if (isShareInterrupted(error)) {
    return <Notice tone="warning" message={t('profile.data.export.interrupted')} announce />;
  }
  if (isApiError(error) && error.kind === 'rate_limited') {
    return (
      <Notice
        tone="warning"
        title={t('errors.kinds.rate_limited.title')}
        message={t('profile.data.export.rateLimited')}
        announce
      />
    );
  }
  return <ProblemState error={error} variant="inline" />;
}
