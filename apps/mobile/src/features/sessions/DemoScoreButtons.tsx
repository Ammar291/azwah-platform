import { useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { isApiError } from '@/api/client';
import { useParticipantApi } from '@/api/participant/ParticipantApiContext';
import type { DemoLevel } from '@/api/participant/schemas';
import { useLeaveFlow } from '@/components/useLeaveFlow';
import { AppButton } from '@/design-system/AppButton';
import { ButtonRow } from '@/design-system/ButtonRow';
import { Notice } from '@/design-system/Notice';
import { spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import { useAssessmentEngine } from './AssessmentEngineContext';
import { createIdempotencyKey } from './idempotencyKey';

const LEVELS: readonly DemoLevel[] = ['low', 'medium', 'high'];

type Problem = 'offline' | 'failed';

interface DemoScoreButtonsProps {
  sessionId: string;
  disabled: boolean;
}

/**
 * DEMONSTRATIONS ONLY (D-130, D-143). One tap of "Low", "Medium" or "High" finishes the questionnaire: the server chooses
 * the answers its approved scoring rules put in that band, saves them, submits and scores the session, and the result
 * opens. The app chooses and computes nothing. A session the server already closed opens its result too. A quiet row of
 * text buttons with no explanation (D-142); screen readers hear what each does.
 */
export function DemoScoreButtons({ sessionId, disabled }: DemoScoreButtonsProps) {
  const { t } = useLocalization();
  const api = useParticipantApi();
  const engine = useAssessmentEngine();
  const leaveFlow = useLeaveFlow();
  const [busy, setBusy] = useState<DemoLevel | null>(null);
  const [problem, setProblem] = useState<Problem | null>(null);

  const finish = (level: DemoLevel) => {
    if (busy) return;
    if (engine.connectivity.getState() === 'offline') {
      setProblem('offline');
      return;
    }
    setBusy(level);
    setProblem(null);
    void (async () => {
      try {
        await api.submitDemoAnswers(sessionId, level, createIdempotencyKey());
      } catch (error) {
        const closed = isApiError(error) && error.code === 'sessions.not_in_progress';
        if (!closed) {
          setBusy(null);
          setProblem(isApiError(error) && (error.kind === 'network' || error.kind === 'timeout') ? 'offline' : 'failed');
          return;
        }
      }
      // Submitted on the server: the questionnaire and its review are done with, so the result opens over Home.
      leaveFlow({ pathname: '/sessions/[sessionId]/result', params: { sessionId } });
      void engine.submittedOnServer(sessionId);
    })();
  };

  return (
    <View style={styles.body} testID="demo-score-buttons">
      <ButtonRow>
        {LEVELS.map((level) => (
          <AppButton
            key={level}
            label={t(`session.demo.${level}`)}
            accessibilityLabel={t(`session.demo.${level}Label`)}
            variant="tertiary"
            loading={busy === level}
            disabled={disabled || (busy !== null && busy !== level)}
            onPress={() => finish(level)}
          />
        ))}
      </ButtonRow>
      {problem ? <Notice tone="danger" message={t(`session.demo.${problem}`)} announce /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  body: { gap: spacing.sm },
});
