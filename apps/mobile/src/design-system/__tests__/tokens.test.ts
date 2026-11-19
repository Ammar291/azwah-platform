import { contrastRatio } from '../contrast';
import { colors, minTouchTarget, statusColors } from '../tokens';

const TEXT = 4.5;
const CONTROL = 3;

describe('colour tokens', () => {
  it.each([
    ['text on background', colors.text, colors.background],
    ['text on surface', colors.text, colors.surface],
    ['secondary text on background', colors.textSecondary, colors.background],
    ['muted text on background', colors.textMuted, colors.background],
    ['muted text on muted surface', colors.textMuted, colors.surfaceMuted],
    ['muted text on a selected option', colors.textMuted, colors.primarySoft],
    ['text on a selected option', colors.text, colors.primarySoft],
    ['button text on primary', colors.onPrimary, colors.primary],
    ['links and tertiary buttons on background', colors.primary, colors.background],
    ['button text on danger', colors.onPrimary, statusColors.danger],
    ['success text on surface', statusColors.success, colors.surface],
    ['success text on its notice', statusColors.success, statusColors.successSoft],
    ['warning text on surface', statusColors.warning, colors.surface],
    ['warning text on its notice', statusColors.warning, statusColors.warningSoft],
    ['danger text on surface', statusColors.danger, colors.surface],
    ['danger text on its notice', statusColors.danger, statusColors.dangerSoft],
    ['info text on its notice', statusColors.info, statusColors.infoSoft],
  ])('%s meets 4.5:1', (_name, foreground, background) => {
    expect(contrastRatio(foreground, background)).toBeGreaterThanOrEqual(TEXT);
  });

  it.each([
    ['input and option borders on surface', colors.borderStrong, colors.surface],
    ['input and option borders on background', colors.borderStrong, colors.background],
    ['selected option border on its fill', colors.primary, colors.primarySoft],
    ['danger border on surface', statusColors.danger, colors.surface],
  ])('%s meets 3:1', (_name, foreground, background) => {
    expect(contrastRatio(foreground, background)).toBeGreaterThanOrEqual(CONTROL);
  });

  it('computes known ratios', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contrastRatio('#ffffff', '#ffffff')).toBeCloseTo(1, 5);
  });

  it('keeps touch targets at least 44 points (48 here)', () => {
    expect(minTouchTarget).toBeGreaterThanOrEqual(44);
  });
});
