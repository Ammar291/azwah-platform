import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { isApiError } from '@/api/client';
import { useParticipantApi } from '@/api/participant/ParticipantApiContext';
import { invalidateParticipantData } from '@/api/participant/queries';
import { useLeaveFlow } from '@/components/useLeaveFlow';
import { demoToolsEnabled } from '@/config';
import { AppButton } from '@/design-system/AppButton';
import { Notice } from '@/design-system/Notice';
import { spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { useAssessmentEngine } from './AssessmentEngineContext';

type Problem = 'offline' | 'failed';

/**
 * DEMONSTRATIONS ONLY (D-144). "New survey": the server opens a new check-in round for the family at once (or hands back
 * the one still open), and its questionnaire opens, where Low, Medium or High finish it; the next result adds a point to
 * the trend chart. Shown only while demo tools are on, as a quiet text button with no explanation (D-142). Everything is
 * decided on the server: who answers comes from its approved eligibility rules.
 */
export function NewCheckInButton() {
  const { t } = useLocalization();
  const api = useParticipantApi();
  const engine = useAssessmentEngine();
  const queryClient = useQueryClient();
  const leaveFlow = useLeaveFlow();
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<Problem | null>(null);

  if (!demoToolsEnabled) {
    return null;
  }

  const sessionFor = async (assignmentId: string, activeSessionId: string | null): Promise<string> => {
    if (activeSessionId) return activeSessionId;
    try {
      return (await api.startSession(assignmentId)).id;
    } catch (error) {
      // Started a moment ago (another tap, another phone): open that session.
      const started = isApiError(error) && error.code === 'sessions.session_already_exists'
        ? (await api.getAssignment(assignmentId)).activeSessionId
        : null;
      if (started) return started;
      throw error;
    }
  };

  const open = () => {
    if (busy) return;
    if (engine.connectivity.getState() === 'offline') {
      setProblem('offline');
      return;
    }
    setBusy(true);
    setProblem(null);
    void (async () => {
      try {
        const checkIn = await api.openDemoCheckIn();
        const sessionId = await sessionFor(checkIn.assignmentId, checkIn.activeSessionId);
        void invalidateParticipantData(queryClient);
        leaveFlow({ pathname: '/sessions/[sessionId]', params: { sessionId } });
      } catch (error) {
        setProblem(isApiError(error) && (error.kind === 'network' || error.kind === 'timeout') ? 'offline' : 'failed');
      } finally {
        setBusy(false);
      }
    })();
  };

  return (
    <View style={styles.body} testID="new-check-in">
      <AppButton
        label={t('newCheckIn.action')}
        accessibilityLabel={t('newCheckIn.label')}
        variant="tertiary"
        loading={busy}
        onPress={open}
      />
      {problem ? <Notice tone="danger" message={t(`newCheckIn.${problem}`)} announce /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  body: { gap: spacing.sm },
});
