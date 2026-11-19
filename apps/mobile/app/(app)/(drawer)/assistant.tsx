import { useState } from 'react';
import { useTranslation } from 'react-i18next';

import { AppScreen } from '@/components/AppScreen';
import { ConfirmationDialog } from '@/design-system/ConfirmationDialog';
import { AssistantComposer, AssistantConversation } from '@/features/assistant/AssistantScreen';
import { useAssistantChat } from '@/features/assistant/useAssistantChat';

export default function Assistant() {
  const { t } = useTranslation();
  const chat = useAssistantChat();
  const [confirmingClear, setConfirmingClear] = useState(false);
  return (
    <AppScreen
      title={t('assistant.title')}
      menu
      headerAction={
        chat.messages.length > 0 ? { label: t('assistant.clear.action'), onPress: () => setConfirmingClear(true) } : undefined
      }
      footer={<AssistantComposer chat={chat} />}
      keyboardAware
      followEnd
    >
      <AssistantConversation chat={chat} />
      <ConfirmationDialog
        visible={confirmingClear}
        destructive
        title={t('assistant.clear.title')}
        message={t('assistant.clear.message')}
        confirmLabel={t('assistant.clear.confirm')}
        cancelLabel={t('common.cancel')}
        onConfirm={() => void chat.clear().finally(() => setConfirmingClear(false))}
        onCancel={() => setConfirmingClear(false)}
      />
    </AppScreen>
  );
}
