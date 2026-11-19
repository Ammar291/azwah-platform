import { screen, userEvent } from '@testing-library/react-native';

import type { Assignment } from '@/api/participant/schemas';
import { renderWithProviders } from '@/test-utils/renderWithProviders';

import { AssessmentCard } from '../AssessmentCard';
import { LanguageSwitcher } from '../LanguageSwitcher';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));

const assignment: Assignment = {
  id: '0199e0a1-0000-7000-8000-00000000a001',
  memberId: '0199e0a1-0000-7000-8000-00000000a002',
  instrumentCode: 'sample',
  instrumentName: { en: 'Sample questionnaire', ar: 'استبيان تجريبي' },
  versionNumber: 1,
  sectionCode: 'A',
  sectionTitle: { en: 'Section A', ar: 'القسم أ' },
  waveId: '0199e0a1-0000-7000-8000-00000000a003',
  waveCode: 'W1',
  status: 'in_progress',
  assignedAt: '2026-10-01T00:00:00Z',
  expiresAt: '2026-12-01T00:00:00Z',
  completedAt: null,
  activeSessionId: null,
  instrumentKind: 'family_voices',
};

describe('AssessmentCard', () => {
  it('names the questionnaire, its section and status in words, and opens it', async () => {
    const onPress = jest.fn();
    const user = userEvent.setup();
    await renderWithProviders(<AssessmentCard assignment={assignment} onPress={onPress} />);
    const card = screen.getByRole('button', { name: 'Sample questionnaire, Section A, In progress' });

    expect(screen.getByText('Round W1')).toBeOnTheScreen();
    expect(screen.getByText('Due 1 December 2026')).toBeOnTheScreen();
    expect(screen.getByTestId('icon-info', { includeHiddenElements: true })).toBeOnTheScreen();
    expect(card).toHaveProp('accessibilityHint', 'Continue');
    await user.press(card);
    expect(onPress).toHaveBeenCalledTimes(1);
  });

  it('uses the Arabic text the server sent', async () => {
    await renderWithProviders(<AssessmentCard assignment={assignment} onPress={jest.fn()} />, { language: 'ar' });

    expect(screen.getByRole('button', { name: 'استبيان تجريبي, القسم أ, قيد الإجابة' })).toBeOnTheScreen();
    expect(screen.getByText(/2026/)).not.toHaveTextContent(/[٠-٩]/);
  });

  it('shows no due date once finished', async () => {
    await renderWithProviders(
      <AssessmentCard assignment={{ ...assignment, status: 'completed' }} onPress={jest.fn()} />,
    );

    expect(screen.queryByText(/^Due /)).toBeNull();
    expect(screen.getByText('Completed')).toBeOnTheScreen();
  });
});

describe('LanguageSwitcher', () => {
  it('offers the other language by its own name', async () => {
    const user = userEvent.setup();
    const { changeLanguage } = await renderWithProviders(<LanguageSwitcher variant="button" />);

    await user.press(screen.getByRole('button', { name: 'العربية' }));

    expect(changeLanguage).toHaveBeenCalledWith('ar');
  });

  it('lists both languages and marks the current one', async () => {
    const user = userEvent.setup();
    const { changeLanguage } = await renderWithProviders(<LanguageSwitcher />, { language: 'ar' });

    expect(screen.getByRole('radio', { name: 'العربية' })).toBeChecked();
    await user.press(screen.getByRole('radio', { name: 'English' }));
    expect(changeLanguage).toHaveBeenCalledWith('en');
  });

  it('does nothing when the current language is chosen again', async () => {
    const user = userEvent.setup();
    const { changeLanguage } = await renderWithProviders(<LanguageSwitcher />);

    await user.press(screen.getByRole('radio', { name: 'English' }));

    expect(changeLanguage).not.toHaveBeenCalled();
  });
});
