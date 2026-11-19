import { Modal, Pressable, StyleSheet, View } from 'react-native';

import { AppButton } from './AppButton';
import { AppText } from './AppText';
import { Notice } from './Notice';
import { colors, maxContentWidth, radii, spacing } from './tokens';

export interface ConfirmationDialogProps {
  visible: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
  /** Confirms with a danger button, for withdrawing consent or signing out. */
  destructive?: boolean;
  /** Shows progress on the confirm button and blocks dismissal while the action runs. */
  busy?: boolean;
  /** The confirmed action failed: said inside the dialog (and announced), which stays open to retry or cancel. */
  problem?: { title?: string; message: string } | null;
}

/**
 * Asks before an action that is hard to undo. The system back button and a tap outside cancel. Buttons stack so long
 * Arabic labels never truncate; the safe choice (cancel) is never the filled button.
 */
export function ConfirmationDialog({
  visible,
  title,
  message,
  confirmLabel,
  cancelLabel,
  onConfirm,
  onCancel,
  destructive = false,
  busy = false,
  problem = null,
}: ConfirmationDialogProps) {
  const cancel = () => {
    if (!busy) {
      onCancel();
    }
  };
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={cancel} statusBarTranslucent>
      <View style={styles.backdrop}>
        <Pressable
          style={StyleSheet.absoluteFill}
          onPress={cancel}
          accessible={false}
          importantForAccessibility="no"
        />
        <View style={styles.dialog} accessibilityViewIsModal testID="confirmation-dialog">
          <AppText variant="heading" accessibilityRole="header">
            {title}
          </AppText>
          <AppText tone="secondary">{message}</AppText>
          {problem ? <Notice tone="danger" title={problem.title} message={problem.message} announce /> : null}
          <View style={styles.actions}>
            <AppButton
              label={confirmLabel}
              variant={destructive ? 'danger' : 'primary'}
              loading={busy}
              onPress={onConfirm}
            />
            <AppButton label={cancelLabel} variant="secondary" disabled={busy} onPress={cancel} />
          </View>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: colors.scrim, justifyContent: 'center', padding: spacing.xl },
  dialog: {
    width: '100%',
    maxWidth: maxContentWidth,
    alignSelf: 'center',
    backgroundColor: colors.surface,
    borderRadius: radii.lg,
    padding: spacing.xl,
    gap: spacing.md,
  },
  actions: { gap: spacing.sm, marginTop: spacing.sm },
});
