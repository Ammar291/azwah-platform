import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { renderWithProviders } from '@/test/render';

import { WelcomeView } from './welcome-view';

describe('WelcomeView', () => {
  it('greets once, then says what the role is missing', () => {
    renderWithProviders(<WelcomeView />);

    expect(screen.getByRole('heading', { level: 1, name: 'Welcome' })).toBeInTheDocument();
    expect(screen.getAllByRole('heading', { name: 'Welcome' })).toHaveLength(1);
    expect(screen.getByRole('heading', { level: 2, name: 'No sections for your role yet' })).toBeInTheDocument();
  });

  it('reads in Arabic', () => {
    renderWithProviders(<WelcomeView />, { language: 'ar' });

    expect(screen.getAllByRole('heading', { name: 'مرحبًا' })).toHaveLength(1);
    expect(screen.getByRole('heading', { level: 2, name: 'لا توجد أقسام لدورك بعد' })).toBeInTheDocument();
  });
});
