import { useRouter } from 'expo-router';
import { useRef } from 'react';
import { Linking, ScrollView, StyleSheet, View } from 'react-native';

import { AppButton } from '@/design-system/AppButton';
import { AppCard } from '@/design-system/AppCard';
import { AppText } from '@/design-system/AppText';
import { Badge } from '@/design-system/Badge';
import { ButtonRow } from '@/design-system/ButtonRow';
import { Icon } from '@/design-system/Icon';
import { LoadingState } from '@/design-system/LoadingState';
import { Notice } from '@/design-system/Notice';
import { Section } from '@/design-system/Section';
import { colors, radii, spacing } from '@/design-system/tokens';
import { SuggestedActions } from '@/features/assistant/SuggestedActions';
import { CrisisSupportCard } from '@/features/safety/CrisisSupportCard';
import { useLocalization } from '@/i18n/useLocalization';

import { useConsultantCall, type Caption, type CallState } from './useConsultantCall';
import type { VoiceAudio } from './voiceAudio';

const ORB = 132;
/** The transcript scrolls within this height, so the controls stay near. */
const TRANSCRIPT_HEIGHT = 360;

/**
 * The Azwah consultant (D-109, D-124): a live, spoken conversation. The person starts it, can mute or end it, and sees
 * captions of the whole call, which are added to the assistant chat when it ends (D-123). Screens the consultant
 * suggests appear as buttons; opening one ends the call. Without a connected model the server says so and nothing
 * starts. `voice` replaces the phone's audio (tests).
 */
export function ConsultantView({ voice }: { voice?: VoiceAudio }) {
  const { t } = useLocalization();
  const router = useRouter();
  const { state, start, end, toggleMute, dismissCrisisSupport } = useConsultantCall(voice);

  return (
    <>
      <Badge label={t('consultant.badge')} />
      <AppText>{t('consultant.about')}</AppText>

      {state.crisisSupport ? <CrisisSupportCard onDismiss={dismissCrisisSupport} onLeave={end} /> : null}

      <AppCard>
        <CallPanel state={state} onStart={() => void start()} onEnd={end} onToggleMute={toggleMute} />
      </AppCard>

      {state.status === 'live' && state.echoCancellation === false ? (
        <Notice tone="info" message={t('consultant.headphones')} testID="consultant-headphones" />
      ) : null}

      {state.actions.length > 0 ? (
        <Section title={t('consultant.suggested')}>
          <SuggestedActions actions={state.actions} here="consultant" layout="stretch" onOpen={end} />
        </Section>
      ) : null}

      {state.status === 'unavailable' ? (
        <Notice
          tone="warning"
          title={t('consultant.unavailable.title')}
          message={t('consultant.unavailable.message')}
          action={{ label: t('consultant.unavailable.action'), onPress: () => router.push('/help') }}
          announce
          testID="consultant-unavailable"
        />
      ) : null}
      {state.status === 'permission_denied' ? (
        <Notice
          tone="warning"
          title={t('consultant.microphone.title')}
          message={t('consultant.microphone.message')}
          action={{ label: t('consultant.microphone.action'), onPress: () => void Linking.openSettings() }}
          announce
          testID="consultant-microphone"
        />
      ) : null}
      {state.status === 'failed' ? (
        <Notice
          tone="danger"
          title={t('consultant.failed.title')}
          message={t(state.failure === 'connection' || state.failure === 'not_configured' ? 'consultant.failed.connection' : 'consultant.failed.message')}
          announce
          testID="consultant-failed"
        />
      ) : null}

      {state.captions.some((caption) => caption.text.trim()) ? (
        <Section title={t('consultant.captions.title')}>
          <Transcript captions={state.captions} />
        </Section>
      ) : null}

      <AppCard variant="muted">
        <AppText variant="bodyStrong">{t('consultant.emergency')}</AppText>
        <AppButton label={t('consultant.emergencyLink')} variant="secondary" onPress={() => router.push('/emergency')} />
      </AppCard>
    </>
  );
}

interface CallPanelProps {
  state: CallState;
  onStart: () => void;
  onEnd: () => void;
  onToggleMute: () => void;
}

function CallPanel({ state, onStart, onEnd, onToggleMute }: CallPanelProps) {
  const { t } = useLocalization();
  if (state.status === 'starting' || state.status === 'connecting') {
    // Never a spinner with no way out: the person can stop waiting, and the call gives up by itself after a while.
    return (
      <View style={styles.waiting}>
        <LoadingState label={t('consultant.connecting')} />
        <AppButton label={t('consultant.end')} variant="secondary" onPress={onEnd} />
      </View>
    );
  }
  if (state.status !== 'live') {
    return (
      <View style={styles.panel}>
        <Orb active={false} />
        <AppText variant="bodyStrong" style={styles.centred}>
          {t(state.status === 'ended' ? 'consultant.ended' : 'consultant.ready')}
        </AppText>
        {/* Stretched: a button that shrinks to its words is where Android split labels over two lines. */}
        <View style={styles.stretch}>
          <AppButton
            label={t(state.status === 'idle' ? 'consultant.start' : 'consultant.startAgain')}
            onPress={onStart}
            testID="consultant-start"
          />
        </View>
        <AppText variant="caption" tone="muted" style={styles.centred}>
          {t('consultant.privacy')}
        </AppText>
      </View>
    );
  }
  const now = state.muted ? 'muted' : state.speaking === 'consultant' ? 'speaking' : state.speaking === 'user' ? 'hearing' : 'listening';
  return (
    <View style={styles.panel}>
      <Orb active={now === 'speaking' || now === 'hearing'} />
      <AppText variant="bodyStrong" style={styles.centred} accessibilityLiveRegion="polite" testID="consultant-status">
        {t(`consultant.live.${now}`)}
      </AppText>
      <View style={styles.stretch}>
        <ButtonRow>
          <AppButton
            label={t(state.muted ? 'consultant.unmute' : 'consultant.mute')}
            variant="secondary"
            onPress={onToggleMute}
          />
          <AppButton label={t('consultant.end')} variant="danger" onPress={onEnd} />
        </ButtonRow>
      </View>
    </View>
  );
}

/** The whole call so far, newest at the end and kept in view; only the newest line is announced. */
function Transcript({ captions }: { captions: readonly Caption[] }) {
  const { t } = useLocalization();
  const list = useRef<ScrollView>(null);
  const shown = captions.filter((caption) => caption.text.trim());
  return (
    <ScrollView
      ref={list}
      style={styles.transcript}
      contentContainerStyle={styles.captions}
      nestedScrollEnabled
      onContentSizeChange={() => list.current?.scrollToEnd({ animated: true })}
      accessibilityLabel={t('consultant.captions.title')}
      testID="consultant-transcript"
    >
      {shown.map((caption, index) => (
        <View
          key={caption.id}
          style={styles.caption}
          accessibilityLiveRegion={index === shown.length - 1 ? 'polite' : 'none'}
        >
          <AppText variant="label" tone="secondary">
            {t(caption.speaker === 'user' ? 'consultant.captions.you' : 'consultant.captions.consultant')}
          </AppText>
          <AppText tone={caption.final ? 'default' : 'secondary'}>{caption.text}</AppText>
        </View>
      ))}
    </ScrollView>
  );
}

/** A microphone in a circle; brighter while someone is talking. Decorative: the status line says the same in words. */
function Orb({ active }: { active: boolean }) {
  return (
    <View
      style={[styles.orb, active ? styles.orbActive : null]}
      accessible={false}
      importantForAccessibility="no-hide-descendants"
    >
      <Icon name="mic" size={52} color={active ? colors.onPrimary : colors.primary} />
    </View>
  );
}

const styles = StyleSheet.create({
  panel: { alignItems: 'center', gap: spacing.md, paddingVertical: spacing.md },
  waiting: { gap: spacing.sm },
  centred: { textAlign: 'center' },
  orb: {
    width: ORB,
    height: ORB,
    borderRadius: radii.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.primarySoft,
  },
  orbActive: { backgroundColor: colors.primary },
  stretch: { alignSelf: 'stretch' },
  transcript: { maxHeight: TRANSCRIPT_HEIGHT },
  captions: { gap: spacing.sm },
  caption: { gap: spacing.xxs },
});
