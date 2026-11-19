import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { stubBff } from '@/test/bff';
import { renderWithProviders } from '@/test/render';

import { StatusPanel } from './status-panel';

describe('StatusPanel', () => {
  it('asks the dashboard server for the API health, not the API itself', async () => {
    const bff = stubBff({ health: { status: 'ready' } });

    renderWithProviders(<StatusPanel />);

    expect(await screen.findByText('Connected, database ready')).toBeInTheDocument();
    expect(bff.calls.map((call) => call.url.pathname)).toEqual(['/bff/health']);
  });

  it('shows the server as unreachable when the health check itself fails', async () => {
    stubBff({ health: new Response(null, { status: 502 }) });

    renderWithProviders(<StatusPanel />);

    expect(await screen.findByText("Can't reach the server")).toBeInTheDocument();
  });
});
