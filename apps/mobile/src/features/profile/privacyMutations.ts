import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';

import { useParticipantApi } from '@/api/participant/ParticipantApiContext';
import { participantKeys } from '@/api/participant/queries';
import type { CreatePrivacyRequest } from '@/api/participant/schemas';
import { useAppLock } from '@/security/AppLockContext';
import { expoDataFileSharing, exportFileName, type DataFileSharing } from '@/storage/exportFiles';

/** This phone has no share sheet, so the copy of the data cannot leave the app. */
export class SharingUnavailableError extends Error {
  constructor() {
    super('Sharing is not available on this phone.');
    this.name = 'SharingUnavailableError';
  }
}

/**
 * Downloads the person's data and hands it to the share sheet as indented JSON. `data` is Zod-validated by the API
 * client; the file is written from the body as the server sent it, so no field is stripped from the person's copy.
 * The export is never kept: not in the query cache, not in the mutation's result, and the file is deleted afterwards.
 *
 * - Sharing is checked first, so a phone that cannot share does not spend one of the hour's exports.
 * - `networkMode: 'always'`: offline it fails at once with the network state instead of pausing, so a tap can never
 *   be sent later on its own and open the share sheet over the lock screen.
 * - The share sheet opens only if the app is still unlocked and in the foreground at that moment.
 */
export function useDataExport(files: DataFileSharing = expoDataFileSharing) {
  const api = useParticipantApi();
  const { t } = useTranslation();
  const { mayRevealContent } = useAppLock();
  return useMutation({
    networkMode: 'always',
    mutationFn: async (): Promise<void> => {
      if (!(await files.isAvailable())) {
        throw new SharingUnavailableError();
      }
      const { data, raw } = await api.exportMyData();
      await files.shareJson(exportFileName(data.generatedAt), JSON.stringify(raw, null, 2), {
        dialogTitle: t('profile.data.export.shareTitle'),
        mayShare: mayRevealContent,
      });
    },
  });
}

/** Sends a privacy request; the list is read again whatever the answer (a 409 means one is already in it). */
export function useCreatePrivacyRequest() {
  const api = useParticipantApi();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreatePrivacyRequest) => api.createPrivacyRequest(request),
    onSettled: () => queryClient.invalidateQueries({ queryKey: participantKeys.privacyRequests() }),
  });
}
