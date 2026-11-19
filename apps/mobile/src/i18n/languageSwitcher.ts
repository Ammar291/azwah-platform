import type { i18n as I18n } from 'i18next';

import type { SettingsRepository } from '@/storage/settings';

import { applyDirection } from './applyDirection';
import type { Language } from './languages';

/** How long a requested reload may take before the switch concludes it did not happen. */
export const RELOAD_GRACE_MS = 5_000;

export interface LanguageSwitcherOptions {
  settings: SettingsRepository;
  i18n: I18n;
  onChanged: (language: Language) => void;
  now?: () => number;
  /** Runs `task` after `ms` milliseconds; tests pass their own. */
  later?: (task: () => Promise<void>, ms: number) => void;
}

function runLater(task: () => Promise<void>, ms: number): void {
  setTimeout(() => void task(), ms);
}

/**
 * Changes the language, stores it and applies the layout direction. While a switch is running further requests are
 * ignored, and once a reload has been requested they stay ignored until the reload ends this JavaScript instance, so
 * a double tap cannot toggle twice or reload twice. A reload can be skipped without an error (`reloadAppAsync` then
 * just resolves): if the app is still running after RELOAD_GRACE_MS, the switch drops the reload's mark and accepts
 * requests again, instead of leaving the person stuck in the language they just chose.
 */
export function createLanguageSwitcher({
  settings,
  i18n,
  onChanged,
  now = Date.now,
  later = runLater,
}: LanguageSwitcherOptions) {
  let busy = false;

  return async function switchTo(next: Language): Promise<void> {
    if (busy) {
      return;
    }
    busy = true;
    let reloading = false;
    try {
      await settings.set('language', next);
      await i18n.changeLanguage(next);
      onChanged(next);
      // The reload this may need is part of the choice, not a new start: the next start reads this mark once and
      // does not lock the session it restores (bootstrap.ts, src/security).
      await settings.set('language_changed_at', String(now()));
      reloading = (await applyDirection(next, settings, null)).kind === 'reload';
      if (!reloading) {
        await settings.remove('language_changed_at');
      }
    } finally {
      if (reloading) {
        later(async () => {
          try {
            await settings.remove('language_changed_at');
          } finally {
            busy = false;
          }
        }, RELOAD_GRACE_MS);
      } else {
        busy = false;
      }
    }
  };
}
