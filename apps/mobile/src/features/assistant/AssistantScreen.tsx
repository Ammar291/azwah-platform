import { useRouter } from 'expo-router';
import { useEffect } from 'react';
import { ActivityIndicator, Keyboard, StyleSheet, View } from 'react-native';

import { ASSISTANT_MAX_MESSAGE_LENGTH } from '@/api/participant/participantApi';
import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppInput } from '@/design-system/AppInput';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { Notice } from '@/design-system/Notice';
import { borderWidths, colors, radii, spacing } from '@/design-system/tokens';
import { CrisisSupportCard } from '@/features/safety/CrisisSupportCard';
import { openPage } from '@/features/services/openPage';
import { useLocalization } from '@/i18n/useLocalization';

import type { ChatMessage } from './chatStore';
import { SuggestedActions } from './SuggestedActions';
import type { AssistantChat } from './useAssistantChat';

const EXAMPLES = ['checkIn', 'familyTime', 'support'] as const;

/**
 * The Azwah assistant (D-108, D-122): what it uses to answer and where the conversation is kept, then the conversation,
 * including the lines of calls with the consultant (D-123). Replies are the model's text, shown as written; under a
 * reply the app shows the screens it suggested as buttons with the app's own labels, and the directory entries it
 * named as cards that open their links. The app decides nothing from them.
 */
export function AssistantConversation({ chat }: { chat: AssistantChat }) {
  const { t } = useLocalization();
  const router = useRouter();
  const unavailable = chat.available === false || chat.problem === 'unavailable';

  // The card sits in the conversation, where the screen follows the end; the keyboard closes so it has room.
  useEffect(() => {
    if (chat.crisisSupport) Keyboard.dismiss();
  }, [chat.crisisSupport]);

  return (
    <>
      <AppCard variant="muted">
        <Badge label={t('assistant.badge')} />
        <AppText>{t('assistant.about')}</AppText>
        <AppText variant="caption" tone="secondary">
          {t(chat.persistence === 'device' ? 'assistant.keptOnPhone' : 'assistant.keptInMemory')}
        </AppText>
        <View style={styles.emergency}>
          <AppText variant="bodyStrong">
            {t('assistant.emergency')}
          </AppText>
          <AppButton label={t('assistant.emergencyLink')} variant="secondary" onPress={() => router.push('/emergency')} />
        </View>
      </AppCard>

      {unavailable ? (
        <Notice
          tone="warning"
          title={t('assistant.unavailable.title')}
          message={t('assistant.unavailable.message')}
          action={{ label: t('assistant.unavailable.action'), onPress: () => router.push('/help') }}
          testID="assistant-unavailable"
        />
      ) : null}

      {chat.loaded && chat.messages.length === 0 && !unavailable ? (
        <View style={styles.examples}>
          <AppText variant="label">{t('assistant.examples.title')}</AppText>
          {EXAMPLES.map((example) => (
            <AppCard
              key={example}
              onPress={() => chat.setDraft(t(`assistant.examples.${example}`))}
              accessibilityLabel={t(`assistant.examples.${example}`)}
              accessibilityHint={t('assistant.examples.hint')}
            >
              <AppText tone="secondary">{t(`assistant.examples.${example}`)}</AppText>
            </AppCard>
          ))}
        </View>
      ) : null}

      <View style={styles.messages} accessibilityLabel={t('assistant.conversation')}>
        {chat.messages.map((message) => (
          <Bubble key={message.id} message={message} />
        ))}
        {chat.sending ? (
          <View style={[styles.bubble, styles.theirs, styles.thinking]} accessibilityLiveRegion="polite">
            <ActivityIndicator color={colors.primary} />
            <AppText tone="secondary">{t('assistant.thinking')}</AppText>
          </View>
        ) : null}
      </View>

      {chat.crisisSupport ? <CrisisSupportCard onDismiss={chat.dismissCrisisSupport} /> : null}

      {chat.problem && chat.problem !== 'unavailable' ? (
        <Notice
          tone={chat.problem === 'unanswered' ? 'info' : 'danger'}
          title={chat.problem === 'unexpected' ? t('errors.kinds.unexpected.title') : undefined}
          message={
            chat.problem === 'unexpected'
              ? t('errors.kinds.unexpected.message')
              : t(`assistant.problems.${chat.problem}`)
          }
          action={{ label: t('common.retry'), onPress: chat.retry }}
          announce={chat.problem !== 'unanswered'}
          testID="assistant-problem"
        />
      ) : null}
    </>
  );
}

function Bubble({ message }: { message: ChatMessage }) {
  const { t } = useLocalization();
  const mine = message.role === 'user';
  return (
    <View style={[styles.item, mine ? styles.itemMine : styles.itemTheirs]}>
      {message.channel === 'voice' ? (
        <AppText variant="caption" tone="muted" testID="assistant-from-call">
          {t('assistant.fromCall')}
        </AppText>
      ) : null}
      <View
        style={[styles.bubble, mine ? styles.mine : styles.theirs]}
        accessible
        accessibilityLabel={`${t(mine ? 'assistant.you' : 'assistant.title')}: ${message.text}`}
        testID={`assistant-message-${message.role}`}
      >
        <AppText tone={mine ? 'inverse' : 'default'} selectable>
          {message.text}
        </AppText>
      </View>
      {!mine && message.sources?.length ? <ReplySources sources={message.sources} /> : null}
      {!mine && message.actions?.length ? <SuggestedActions actions={message.actions} here="assistant" /> : null}
    </View>
  );
}

/** The directory entries a reply named (D-122): information only, each opening its own page. */
function ReplySources({ sources }: { sources: NonNullable<ChatMessage['sources']> }) {
  const { t } = useLocalization();
  return (
    <View style={styles.sources}>
      <AppText variant="caption" tone="secondary">
        {t('assistant.sources.title')}
      </AppText>
      {sources.map((source) => (
        <AppCard
          key={source.id}
          onPress={() => void openPage(source.url)}
          accessibilityLabel={t('assistant.sources.label', { name: source.name, provider: source.provider })}
          accessibilityHint={t('assistant.sources.hint')}
          testID={`assistant-source-${source.id}`}
        >
          <AppText variant="bodyStrong">{source.name}</AppText>
          {source.provider ? (
            <AppText variant="caption" tone="secondary">
              {source.provider}
            </AppText>
          ) : null}
        </AppCard>
      ))}
    </View>
  );
}

/** Where questions are typed. Sending waits for the reply; the clear action is in the header. */
export function AssistantComposer({ chat }: { chat: AssistantChat }) {
  const { t } = useLocalization();
  const blocked = chat.available === false;
  return (
    <View style={styles.composer}>
      <View style={styles.grow}>
        <AppInput
          label={t('assistant.composer.label')}
          hint={t('assistant.composer.privacy')}
          placeholder={t('assistant.composer.placeholder')}
          value={chat.draft}
          onChangeText={chat.setDraft}
          maxLength={ASSISTANT_MAX_MESSAGE_LENGTH}
          multiline
          disabled={blocked}
          testID="assistant-input"
        />
      </View>
      <AppButton
        label={t('assistant.composer.send')}
        disabled={blocked || chat.sending || !chat.draft.trim()}
        onPress={chat.send}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  grow: { flex: 1 },
  // The emergency line, then the way to the numbers as a full-width button: a safety route, easy to hit.
  emergency: {
    gap: spacing.sm,
    paddingTop: spacing.md,
    borderTopWidth: borderWidths.hairline,
    borderTopColor: colors.border,
  },
  examples: { gap: spacing.sm },
  messages: { gap: spacing.sm },
  item: { gap: spacing.xs, maxWidth: '88%' },
  // The person's messages sit at the end edge, replies at the start edge; both follow the language.
  itemMine: { alignSelf: 'flex-end', alignItems: 'flex-end' },
  itemTheirs: { alignSelf: 'flex-start', alignItems: 'flex-start' },
  sources: { gap: spacing.xs, alignSelf: 'stretch' },
  bubble: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radii.lg,
  },
  mine: { backgroundColor: colors.primary, borderBottomEndRadius: radii.sm },
  theirs: {
    backgroundColor: colors.surface,
    borderWidth: borderWidths.hairline,
    borderColor: colors.border,
    borderBottomStartRadius: radii.sm,
  },
  thinking: { alignSelf: 'flex-start', maxWidth: '88%', flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  composer: { flexDirection: 'row', alignItems: 'flex-end', gap: spacing.sm },
});
