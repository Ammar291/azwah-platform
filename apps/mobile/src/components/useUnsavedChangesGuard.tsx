import { useNavigation, type NativeStackNavigationProp } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';

type StackNavigation = NativeStackNavigationProp<Record<string, object | undefined>>;
type LeaveAction = Parameters<StackNavigation['dispatch']>[0];

/**
 * Asks before a screen with unsaved changes is left. The header's back button, the system back button and the back
 * gesture all remove the screen; each is held until the person chooses to discard their changes or keep editing.
 * The iOS swipe-back gesture is turned off while there are changes, since a native gesture cannot be held. Call
 * `allowLeave` just before navigating away on purpose (after a successful save).
 */
export function useUnsavedChangesGuard(hasChanges: boolean) {
  const navigation = useNavigation<StackNavigation>();
  const { t } = useTranslation();
  const [held, setHeld] = useState<LeaveAction | null>(null);
  const allowed = useRef(false);

  useEffect(() => {
    navigation.setOptions({ gestureEnabled: !hasChanges });
    if (!hasChanges) {
      return undefined;
    }
    return navigation.addListener('beforeRemove', (event) => {
      if (allowed.current) {
        return;
      }
      event.preventDefault();
      setHeld(event.data.action);
    });
  }, [hasChanges, navigation]);

  const allowLeave = useCallback(() => {
    allowed.current = true;
  }, []);

  const dialog = (
    <ConfirmationDialog
      visible={held !== null}
      destructive
      title={t('unsavedChanges.title')}
      message={t('unsavedChanges.message')}
      confirmLabel={t('unsavedChanges.discard')}
      cancelLabel={t('unsavedChanges.keepEditing')}
      onConfirm={() => {
        const action = held;
        allowed.current = true;
        setHeld(null);
        if (action) {
          navigation.dispatch(action);
        }
      }}
      onCancel={() => setHeld(null)}
    />
  );

  return { dialog, allowLeave };
}
