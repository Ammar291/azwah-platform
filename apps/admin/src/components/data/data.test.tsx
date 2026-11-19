import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { displayCount } from '@/lib/display';
import { renderWithProviders } from '@/test/render';

import { ChartFigure } from './chart-figure';
import { Field, NativeInput } from './field';
import { Pagination } from './pagination';
import { StatusBadge } from './status-badge';
import { SuppressedValue } from './suppressed-value';

describe('displayCount', () => {
  it('shows a count at or above the minimum group size, and zero', () => {
    expect(displayCount(10, false, 10)).toEqual({ kind: 'value', value: 10 });
    expect(displayCount(0, false, 10)).toEqual({ kind: 'value', value: 0 });
  });

  it('hides a suppressed cell', () => {
    expect(displayCount(null, true, 10)).toEqual({ kind: 'suppressed' });
  });

  it('hides a small count even if it arrives unflagged', () => {
    expect(displayCount(3, false, 10)).toEqual({ kind: 'suppressed' });
    expect(displayCount(9, false, 10)).toEqual({ kind: 'suppressed' });
  });

  it('hides a missing count', () => {
    expect(displayCount(null, false, 10)).toEqual({ kind: 'suppressed' });
  });
});

describe('SuppressedValue', () => {
  it('shows the number when it may be shown', () => {
    renderWithProviders(<SuppressedValue value={1234} suppressed={false} minimumGroupSize={10} />);

    expect(screen.getByText('1,234')).toBeInTheDocument();
  });

  it('says why a value is hidden', () => {
    renderWithProviders(<SuppressedValue value={4} suppressed={false} minimumGroupSize={10} />);

    expect(screen.getByText('Hidden')).toHaveAttribute(
      'title',
      'Hidden to protect privacy: this count covers fewer than 10 people, or it is hidden along with one that does',
    );
    expect(screen.queryByText('4')).toBeNull();
  });

  it('never calls a hidden value small: the platform also hides a larger count next to a single small one', () => {
    renderWithProviders(<SuppressedValue value={null} suppressed minimumGroupSize={10} />);

    expect(screen.getByText('Hidden')).toBeInTheDocument();
    expect(screen.queryByText(/Fewer than/i)).toBeNull();
  });

  it('says it in Arabic', () => {
    renderWithProviders(<SuppressedValue value={null} suppressed minimumGroupSize={10} />, { language: 'ar' });

    expect(screen.getByText('مخفي')).toHaveAttribute('title', 'مخفي حمايةً للخصوصية: يشمل هذا العدد أقل من 10 أشخاص، أو أُخفي مع عدد يشمل ذلك');
  });
});

describe('Pagination', () => {
  it('announces the page and disables previous on the first page', async () => {
    const onPage = vi.fn();
    renderWithProviders(<Pagination page={1} pageSize={20} totalCount={45} onPage={onPage} />);

    expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
    expect(screen.getByText('45 in total')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
    await userEvent.setup().click(screen.getByRole('button', { name: 'Next' }));
    expect(onPage).toHaveBeenCalledWith(2);
  });

  it('disables next on the last page', () => {
    renderWithProviders(<Pagination page={3} pageSize={20} totalCount={45} onPage={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Previous' })).toBeEnabled();
  });

  it('treats an empty result as one page', () => {
    renderWithProviders(<Pagination page={1} pageSize={20} totalCount={0} onPage={vi.fn()} />);

    expect(screen.getByText('Page 1 of 1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
  });
});

describe('StatusBadge', () => {
  it('shows the status as text, never colour alone', () => {
    renderWithProviders(<StatusBadge kind="assignment" value="completed" />);

    expect(screen.getByText('Completed')).toBeInTheDocument();
  });

  it('translates statuses', () => {
    renderWithProviders(<StatusBadge kind="export" value="running" />, { language: 'ar' });

    expect(screen.getByText('قيد الإعداد')).toBeInTheDocument();
  });
});

describe('ChartFigure', () => {
  it('captions the chart, summarises it for screen readers and offers the data as a table', async () => {
    renderWithProviders(
      <ChartFigure
        title="Completions per month"
        summary="Completions per month, from 12 in January to 18 in March."
        table={{ columns: ['Month', 'Completed'], rows: [['Jan 2026', '12']] }}
      >
        <svg />
      </ChartFigure>,
    );

    expect(screen.getByRole('figure', { name: 'Completions per month' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Completions per month, from 12 in January to 18 in March.' })).toBeInTheDocument();
    await userEvent.setup().click(screen.getByRole('button', { name: 'Show as table' }));
    expect(screen.getByRole('table')).toHaveTextContent('Jan 2026');
    expect(screen.getByRole('button', { name: 'Hide table' })).toHaveAttribute('aria-expanded', 'true');
  });

  it('draws the chart left to right in Arabic too, so axis labels keep their anchors, and keeps its note in the page direction', () => {
    renderWithProviders(
      <ChartFigure title="العنوان" summary="ملخص" table={{ columns: [], rows: [] }} note={<p>ملاحظة</p>}>
        <svg />
      </ChartFigure>,
      { language: 'ar' },
    );

    expect(screen.getByRole('img', { name: 'ملخص' })).toHaveAttribute('dir', 'ltr');
    expect(screen.getByText('ملاحظة').closest('[dir]')).toBeNull();
  });

  it('takes its name from a heading that already shows the title, without a second caption or a card of its own', () => {
    renderWithProviders(
      <article>
        <h3 id="card-title">household_assessment, version 1</h3>
        <ChartFigure title="household_assessment, version 1" captionId="card-title" summary="Summary." table={{ columns: ['Band'], rows: [] }}>
          <svg />
        </ChartFigure>
      </article>,
    );

    const figure = screen.getByRole('figure', { name: 'household_assessment, version 1' });
    expect(figure.querySelector('figcaption')).toBeNull();
    expect(figure.className).not.toMatch(/border/);
    expect(screen.getAllByText('household_assessment, version 1')).toHaveLength(1);
  });
});

describe('Field', () => {
  it('gives the control its hint as its accessible description', () => {
    renderWithProviders(
      <Field id="code" label="Questionnaire code" hint="Lowercase letters and underscores.">
        <NativeInput id="code" />
      </Field>,
    );

    expect(screen.getByLabelText('Questionnaire code')).toHaveAccessibleDescription('Lowercase letters and underscores.');
  });

  it('gives the control its error instead while there is one', () => {
    renderWithProviders(
      <Field id="code" label="Questionnaire code" hint="Lowercase letters and underscores." error="Use lowercase letters.">
        <NativeInput id="code" />
      </Field>,
    );

    expect(screen.getByLabelText('Questionnaire code')).toHaveAccessibleDescription('Use lowercase letters.');
  });
});
