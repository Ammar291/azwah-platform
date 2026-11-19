import { act, render, screen } from '@testing-library/react-native';
import type { ReactElement } from 'react';
import { AccessibilityInfo } from 'react-native';

import type { Language } from '@/i18n/languages';
import { createTestProviders } from '@/test-utils/renderWithProviders';

import { BrandSplash, SPLASH_TIMING } from '../BrandSplash';

const BOTH_SCENES = SPLASH_TIMING.welcomeMs + SPLASH_TIMING.wordmarkMs;

async function renderSplash(ui: ReactElement, language: Language = 'en') {
  const { wrap } = await createTestProviders({ language, persona: null });
  const rendered = await render(wrap(ui));
  return { ...rendered, rerender: (next: ReactElement) => rendered.rerender(wrap(next)) };
}

beforeEach(() => {
  jest.useFakeTimers();
});

afterEach(() => {
  jest.useRealTimers();
  jest.restoreAllMocks();
});

describe('BrandSplash', () => {
  it('welcomes in the app’s language, with the meaning and the four pillars', async () => {
    await renderSplash(<BrandSplash ready={false} onDone={jest.fn()} />);

    expect(screen.getByText('Welcome to Azwah')).toBeOnTheScreen();
    expect(screen.getByText('Preparing your space…')).toBeOnTheScreen();
    expect(screen.getAllByText('The family and kin who stand by you').length).toBeGreaterThan(0);
    for (const pillar of ['Relationships', 'Care', 'Empowerment', 'Hope']) {
      expect(screen.getByText(pillar)).toBeOnTheScreen();
    }
    expect(screen.getByRole('progressbar', { name: 'Welcome to Azwah. Opening the app.' })).toBeOnTheScreen();
  });

  it('plays both scenes even when the app is ready at once, then finishes', async () => {
    const onDone = jest.fn();
    await renderSplash(<BrandSplash ready onDone={onDone} />);

    await act(async () => jest.advanceTimersByTime(BOTH_SCENES - 100));
    expect(onDone).not.toHaveBeenCalled();

    await act(async () => jest.advanceTimersByTime(100));
    await act(async () => jest.advanceTimersByTime(SPLASH_TIMING.exitMs + 50));
    expect(onDone).toHaveBeenCalledTimes(1);
  });

  it('waits for the app to be ready before it fades out', async () => {
    const onDone = jest.fn();
    const { rerender } = await renderSplash(<BrandSplash ready={false} onDone={onDone} />);

    await act(async () => jest.advanceTimersByTime(BOTH_SCENES * 3));
    expect(onDone).not.toHaveBeenCalled();

    await rerender(<BrandSplash ready onDone={onDone} />);
    await act(async () => jest.advanceTimersByTime(SPLASH_TIMING.exitMs + 50));
    expect(onDone).toHaveBeenCalledTimes(1);
  });

  it('speaks Arabic', async () => {
    await renderSplash(<BrandSplash ready={false} onDone={jest.fn()} />, 'ar');

    expect(screen.getByText('أهلاً بك في عزوة')).toBeOnTheScreen();
    expect(screen.getByText('التمكين')).toBeOnTheScreen();
  });

  it('still finishes with reduced motion on', async () => {
    jest.spyOn(AccessibilityInfo, 'isReduceMotionEnabled').mockResolvedValue(true);
    const onDone = jest.fn();
    await renderSplash(<BrandSplash ready onDone={onDone} />);

    await act(async () => jest.advanceTimersByTime(BOTH_SCENES));
    await act(async () => jest.advanceTimersByTime(SPLASH_TIMING.exitMs + 50));

    expect(onDone).toHaveBeenCalledTimes(1);
  });
});
