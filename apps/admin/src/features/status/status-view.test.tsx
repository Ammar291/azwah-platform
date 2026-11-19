import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { messages } from '@/lib/i18n';

import { StatusView } from './status-view';

describe('StatusView', () => {
  it('shows a ready connection without a retry button', () => {
    render(<StatusView status="ready" messages={messages.en} onRetry={vi.fn()} />);

    expect(screen.getByRole('heading', { level: 2, name: 'Server connection' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Connected, database ready');
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('offers a retry when the server cannot be reached', () => {
    const onRetry = vi.fn();
    render(<StatusView status="unreachable" messages={messages.en} onRetry={onRetry} />);

    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('gives every state an icon beside its words, and a retry button of the usual size', () => {
    for (const status of ['checking', 'ready', 'not_ready', 'unreachable', 'not_configured', 'mock'] as const) {
      const { unmount } = render(<StatusView status={status} messages={messages.en} onRetry={vi.fn()} />);
      expect(screen.getByRole('status').querySelector('svg[aria-hidden="true"]')).not.toBeNull();
      unmount();
    }
    render(<StatusView status="unreachable" messages={messages.en} onRetry={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Try again' })).toHaveClass('h-9');
  });

  it('says plainly when the dashboard runs on mock data', () => {
    render(<StatusView status="mock" messages={messages.en} onRetry={vi.fn()} />);

    expect(screen.getByRole('status')).toHaveTextContent('Development mock data: the platform API is not used');
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('renders Arabic messages', () => {
    render(<StatusView status="not_configured" messages={messages.ar} onRetry={vi.fn()} />);

    expect(screen.getByRole('status')).toHaveTextContent('لم يُضبط عنوان الخادم');
  });
});
