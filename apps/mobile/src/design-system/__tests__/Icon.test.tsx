import { render, screen } from '@testing-library/react-native';
import { I18nManager, StyleSheet } from 'react-native';

import { Icon, type IconName } from '../Icon';

const ALL: IconName[] = [
  'chevron-forward',
  'chevron-back',
  'check',
  'alert',
  'info',
  'plus',
  'close',
  'dot',
  'fingerprint',
  'lock',
  'chat',
  'menu',
  'home',
  'mic',
  'people',
  'heart',
  'grid',
  'settings',
  'sign-out',
];

const isMirrored = (name: IconName) => {
  const style = StyleSheet.flatten(screen.getByTestId(`icon-${name}`, { includeHiddenElements: true }).props.style) as {
    transform?: object[];
  };
  return JSON.stringify(style.transform ?? []).includes('"scaleX":-1');
};

describe('Icon', () => {
  afterEach(() => jest.restoreAllMocks());

  it('draws every glyph, hidden from screen readers', async () => {
    for (const name of ALL) {
      await render(<Icon name={name} />);
      const icon = screen.getByTestId(`icon-${name}`, { includeHiddenElements: true });
      expect([name, icon.props.importantForAccessibility]).toEqual([name, 'no-hide-descendants']);
      expect([name, icon.children.length > 0]).toEqual([name, true]);
    }
  });

  it('points the sign-out arrow to the end edge in both directions, and leaves the menu glyphs alone', async () => {
    await render(<Icon name="sign-out" />);
    expect(isMirrored('sign-out')).toBe(false);

    jest.replaceProperty(I18nManager, 'isRTL', true);
    await render(<Icon name="sign-out" />);
    expect(isMirrored('sign-out')).toBe(true);
    await render(<Icon name="home" />);
    expect(isMirrored('home')).toBe(false);
  });
});
