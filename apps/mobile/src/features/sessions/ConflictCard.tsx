import { useState } from 'react';
import { StyleSheet, View } from 'react-native';

import type { PublishedItem } from '@/api/participant/schemas';
import { AppButton } from '@/design-system/AppButton';
import { Notice } from '@/design-system/Notice';
import { spacing } from '@/design-system/tokens';
import { useLocalization } from '@/i18n/useLocalization';

import type { AnswerConflict } from './answerSync';

export interface ConflictCardProps {
  item: PublishedItem;
  conflict: AnswerConflict;
  onKeepLocal: () => Promise<void>;
  onKeepServer: () => Promise<void>;
}

/**
 * An answer changed somewhere else after it was chosen on this phone (decision M-40). Nothing is overwritten until
 * the person picks one; each button names the answer it keeps.
 */
export function ConflictCard({ item, conflict, onKeepLocal, onKeepServer }: ConflictCardProps) {
  const { t, pickText } = useLocalization();
  const [busy, setBusy] = useState(false);
  const label = (optionId: string) => {
    const option = item.options.find((candidate) => candidate.id === optionId);
    return option ? pickText(option.label) : t('conflict.unknownAnswer');
  };
  const run = (action: () => Promise<void>) => async () => {
    if (busy) return;
    setBusy(true);
    try {
      await action();
    } finally {
      setBusy(false);
    }
  };
  return (
    <View style={styles.container} testID={`conflict-${item.id}`}>
      <Notice
        tone="warning"
        title={t('conflict.title')}
        message={t('conflict.message', { server: label(conflict.server), local: label(conflict.local) })}
        announce
      />
      <AppButton
        variant="secondary"
        label={t('conflict.keepServer', { answer: label(conflict.server) })}
        disabled={busy}
        onPress={() => void run(onKeepServer)()}
      />
      <AppButton
        variant="secondary"
        label={t('conflict.keepLocal', { answer: label(conflict.local) })}
        disabled={busy}
        onPress={() => void run(onKeepLocal)()}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: spacing.sm },
});
