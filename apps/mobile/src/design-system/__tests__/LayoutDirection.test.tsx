import { fireEvent, render, screen } from '@testing-library/react-native';
import { StyleSheet } from 'react-native';

import { Icon } from '../Icon';
import { LayoutDirectionProvider } from '../LayoutDirection';

const pointsBackwards = (testID: string) => {
  const style = StyleSheet.flatten(screen.getByTestId(testID, { includeHiddenElements: true }).props.style) as {
    transform?: object[];
  };
  return JSON.stringify(style.transform ?? []).includes('"scaleX":-1');
};

const layoutAt = (x: number) => ({ nativeEvent: { layout: { x, y: 0, width: 1, height: 1 } } });

describe('LayoutDirectionProvider', () => {
  it('mirrors directional icons when the native layout turns out to run right to left', async () => {
    await render(
      <LayoutDirectionProvider>
        <Icon name="chevron-forward" />
      </LayoutDirectionProvider>,
    );
    expect(pointsBackwards('icon-chevron-forward')).toBe(false);

    await fireEvent(screen.getByTestId('layout-direction-marker', { includeHiddenElements: true }), 'layout', layoutAt(1));

    expect(pointsBackwards('icon-chevron-forward')).toBe(true);
  });

  it('keeps left-to-right glyphs when the marker sits at the left edge', async () => {
    await render(
      <LayoutDirectionProvider>
        <Icon name="chevron-back" />
      </LayoutDirectionProvider>,
    );

    await fireEvent(screen.getByTestId('layout-direction-marker', { includeHiddenElements: true }), 'layout', layoutAt(0));

    expect(pointsBackwards('icon-chevron-back')).toBe(true);
  });

  it('never mirrors glyphs that have no direction', async () => {
    await render(
      <LayoutDirectionProvider>
        <Icon name="check" />
      </LayoutDirectionProvider>,
    );

    await fireEvent(screen.getByTestId('layout-direction-marker', { includeHiddenElements: true }), 'layout', layoutAt(1));

    expect(pointsBackwards('icon-check')).toBe(false);
  });
});
