import { useState } from 'react';

import { useAuth } from '@/auth/AuthContext';
import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';
import { useDraftStore } from '@/drafts/DraftStoreContext';
import { useLocalization } from '@/i18n/useLocalization';

export interface SignOutDialogProps {
  visible: boolean;
  onClose: () => void;
}

/**
 * Asks before signing out, and says what happens to answers not yet sent (on this phone, when drafts are kept on it).
 * Used by the side menu and Settings. A sign-out that fails says so in the dialog, which stays open: the person is
 * still signed in, and can try again or cancel.
 */
export function SignOutDialog({ visible, onClose }: SignOutDialogProps) {
  const { t } = useLocalization();
  const { provider } = useAuth();
  const drafts = useDraftStore();
  const [signingOut, setSigningOut] = useState(false);
  const [failed, setFailed] = useState(false);

  const cancel = () => {
    setFailed(false);
    onClose();
  };

  const signOut = async () => {
    setSigningOut(true);
    setFailed(false);
    try {
      await provider.signOut();
      onClose();
    } catch {
      setFailed(true);
    } finally {
      setSigningOut(false);
    }
  };

  return (
    <ConfirmationDialog
      visible={visible}
      destructive
      title={t('profile.signOutTitle')}
      message={t(drafts.persistence === 'device' ? 'profile.signOutMessageDevice' : 'profile.signOutMessage')}
      confirmLabel={t('profile.signOut')}
      cancelLabel={t('common.cancel')}
      busy={signingOut}
      problem={
        failed ? { title: t('errors.kinds.unexpected.title'), message: t('errors.kinds.unexpected.message') } : null
      }
      onConfirm={() => void signOut()}
      onCancel={cancel}
    />
  );
}
