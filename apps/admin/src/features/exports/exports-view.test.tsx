import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { Toaster } from '@/components/ui/sonner';
import { problem, stubBff } from '@/test/bff';
import { catalogBody, exportJob, paged, WAVE_H1, wavesBody } from '@/test/fixtures';
import { renderWithProviders } from '@/test/render';

import { ExportsView } from './exports-view';

vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation);

function view(pollMs?: number) {
  return renderWithProviders(
    <>
      <ExportsView pollMs={pollMs} />
      <Toaster />
    </>,
  );
}

const created = exportJob({ status: 'queued', rowCount: null, finishedAt: null, expiresAt: null, downloadPath: null });

/** jsdom has no object URLs; the download button makes one for the file it fetched. */
function stubObjectUrls() {
  const createObjectURL = vi.fn(() => 'blob:export');
  const revokeObjectURL = vi.fn();
  Object.assign(URL, { createObjectURL, revokeObjectURL });
  return { createObjectURL, revokeObjectURL };
}

function postsTo(bff: ReturnType<typeof stubBff>) {
  return bff.calls.filter((call) => call.url.pathname === '/bff/admin/exports' && call.init.method === 'POST');
}

describe('export request form', () => {
  it('asks for a dataset and at least one field before sending anything', async () => {
    const bff = stubBff({ 'admin/exports/catalog': catalogBody, 'admin/exports': paged([]), 'admin/waves': wavesBody });
    const user = userEvent.setup();
    view();

    await user.click(await screen.findByRole('button', { name: 'Request export' }));
    expect(screen.getByText('Choose a dataset.')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Dataset'), 'score_results');
    await user.click(screen.getByRole('button', { name: 'Request export' }));
    expect(screen.getByText('Choose at least one field.', { selector: 'p' })).toBeInTheDocument();
    expect(postsTo(bff)).toHaveLength(0);
  });

  it('offers only the fields of the chosen dataset, none chosen, and resets them when the dataset changes', async () => {
    stubBff({ 'admin/exports/catalog': catalogBody, 'admin/exports': paged([]), 'admin/waves': wavesBody });
    const user = userEvent.setup();
    view();

    await user.selectOptions(await screen.findByLabelText('Dataset'), 'score_results');
    const fields = screen.getByRole('group', { name: 'Fields' });
    const boxes = within(fields).getAllByRole('checkbox');
    expect(boxes.map((box) => box.getAttribute('value'))).toEqual(['participant_pseudonym', 'tier_code', 'score_status']);
    expect(boxes.every((box) => !(box as HTMLInputElement).checked)).toBe(true);

    await user.click(within(fields).getByRole('checkbox', { name: /Band code/ }));
    await user.selectOptions(screen.getByLabelText('Dataset'), 'assessment_sessions');

    const sessionBoxes = within(screen.getByRole('group', { name: 'Fields' })).getAllByRole('checkbox');
    expect(sessionBoxes.map((box) => box.getAttribute('value'))).toEqual(['participant_pseudonym', 'wave_code', 'session_status']);
    expect(sessionBoxes.every((box) => !(box as HTMLInputElement).checked)).toBe(true);
  });

  it('sends the request with one idempotency key, and reuses it when retrying after a network failure', async () => {
    let attempts = 0;
    const bff = stubBff({
      'admin/exports/catalog': catalogBody,
      'admin/waves': wavesBody,
      'admin/exports': (_url: URL, init: RequestInit) => {
        if (init.method !== 'POST') return paged([]);
        attempts += 1;
        if (attempts === 1) throw new TypeError('Failed to fetch');
        return Response.json(created, { status: 202 });
      },
    });
    const user = userEvent.setup();
    view();

    await user.selectOptions(await screen.findByLabelText('Dataset'), 'score_results');
    await user.click(screen.getByRole('checkbox', { name: /Band code/ }));
    await user.selectOptions(screen.getByLabelText('Wave'), WAVE_H1.id);
    await user.click(screen.getByRole('button', { name: 'Request export' }));
    expect(await screen.findAllByText('The export was not requested.')).toHaveLength(2);
    await user.click(screen.getByRole('button', { name: 'Request export' }));
    expect(await screen.findByText(/Export requested/)).toBeInTheDocument();

    const posts = postsTo(bff);
    expect(posts).toHaveLength(2);
    const keys = posts.map((post) => new Headers(post.init.headers).get('idempotency-key'));
    expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/);
    expect(keys[1]).toBe(keys[0]);
    expect(JSON.parse(String(posts[1].init.body))).toEqual({
      dataset: 'score_results',
      columns: ['tier_code'],
      filters: { waveId: WAVE_H1.id, instrumentCode: null },
    });
  });

  it('uses a new key for the next request', async () => {
    const bff = stubBff({
      'admin/exports/catalog': catalogBody,
      'admin/waves': wavesBody,
      'admin/exports': (_url: URL, init: RequestInit) => (init.method === 'POST' ? Response.json(created, { status: 202 }) : paged([])),
    });
    const user = userEvent.setup();
    view();

    for (let index = 0; index < 2; index++) {
      await user.selectOptions(await screen.findByLabelText('Dataset'), 'score_results');
      await user.click(screen.getByRole('checkbox', { name: /Band code/ }));
      await user.click(screen.getByRole('button', { name: 'Request export' }));
      await waitFor(() => expect(postsTo(bff)).toHaveLength(index + 1));
    }

    const keys = postsTo(bff).map((post) => new Headers(post.init.headers).get('idempotency-key'));
    expect(keys[1]).not.toBe(keys[0]);
  });

  it('explains that storage is not configured in this environment', async () => {
    stubBff({
      'admin/exports/catalog': catalogBody,
      'admin/waves': wavesBody,
      'admin/exports': (_url: URL, init: RequestInit) =>
        init.method === 'POST' ? problem(409, 'analytics.export_storage_not_configured') : paged([]),
    });
    const user = userEvent.setup();
    view();

    await user.selectOptions(await screen.findByLabelText('Dataset'), 'score_results');
    await user.click(screen.getByRole('checkbox', { name: /Band code/ }));
    await user.click(screen.getByRole('button', { name: 'Request export' }));

    expect(await screen.findAllByText(/Exports are not set up in this environment/)).not.toHaveLength(0);
  });

  it.each([
    [409, 'analytics.export_limit_reached', /Wait for one to finish/],
    [409, 'analytics.export_daily_limit_reached', /in 24 hours/],
    [429, 'requests.rate_limited', /Too many export requests in a short time/],
    [403, 'bff.cross_origin', /opened at a different address from the one it is set up for/],
  ])('explains a refusal under the export quotas (%i %s)', async (status, code, message) => {
    stubBff({
      'admin/exports/catalog': catalogBody,
      'admin/waves': wavesBody,
      'admin/exports': (_url: URL, init: RequestInit) => (init.method === 'POST' ? problem(status, code) : paged([])),
    });
    const user = userEvent.setup();
    view();

    await user.selectOptions(await screen.findByLabelText('Dataset'), 'score_results');
    await user.click(screen.getByRole('checkbox', { name: /Band code/ }));
    await user.click(screen.getByRole('button', { name: 'Request export' }));

    expect(await screen.findAllByText(message)).not.toHaveLength(0);
  });

  it('refuses a malformed questionnaire code', async () => {
    const bff = stubBff({ 'admin/exports/catalog': catalogBody, 'admin/exports': paged([]), 'admin/waves': wavesBody });
    const user = userEvent.setup();
    view();

    await user.selectOptions(await screen.findByLabelText('Dataset'), 'score_results');
    await user.click(screen.getByRole('checkbox', { name: /Band code/ }));
    await user.type(screen.getByLabelText('Questionnaire code (optional)'), 'Not A Code');
    await user.click(screen.getByRole('button', { name: 'Request export' }));

    expect(screen.getByText('Use lowercase letters, digits and underscores, starting with a letter.')).toBeInTheDocument();
    expect(postsTo(bff)).toHaveLength(0);
  });

  it('cannot request anything when the catalog cannot be loaded', async () => {
    stubBff({ 'admin/exports/catalog': problem(500), 'admin/exports': paged([]), 'admin/waves': wavesBody });
    view();

    expect(await screen.findByText(/The list of allowed fields could not be loaded/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Request export' })).toBeNull();
  });
});

describe('export list', () => {
  it('shows each export with its status, and a download link only when the file can be downloaded', async () => {
    stubBff({
      'admin/exports/catalog': catalogBody,
      'admin/waves': wavesBody,
      'admin/exports': paged([
        exportJob(),
        exportJob({ id: '0190f0e2-2222-7000-8000-000000000002', status: 'expired', downloadPath: null }),
        exportJob({ id: '0190f0e2-2222-7000-8000-000000000003', status: 'failed', errorCode: 'analytics.export_too_large', downloadPath: null, rowCount: null }),
      ]),
    });
    view();

    const table = await screen.findByRole('table', { name: 'Your exports' });
    const [ready, expired, failed] = within(table).getAllByRole('row').slice(1);
    expect(within(ready).getByText('Ready')).toBeInTheDocument();
    expect(within(ready).getByRole('button', { name: /Download the CSV/ })).toBeEnabled();
    expect(within(ready).getByText('2 fields')).toBeInTheDocument();
    expect(within(expired).queryByRole('button')).toBeNull();
    expect(within(failed).getByText('Too many rows: narrow the filters')).toBeInTheDocument();
    expect(within(failed).queryByRole('button')).toBeNull();
    // The note about the times sits under the list it explains.
    expect(table.compareDocumentPosition(screen.getByText('Dates and times are in UTC.')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('counts one field in the singular', async () => {
    stubBff({ 'admin/exports/catalog': catalogBody, 'admin/waves': wavesBody, 'admin/exports': paged([exportJob({ columns: ['tier_code'] })]) });
    view();

    expect(await screen.findByText('1 field')).toBeInTheDocument();
  });

  it('saves the file it fetched through the dashboard server', async () => {
    stubObjectUrls();
    const saved: { href: string; download: string }[] = [];
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      saved.push({ href: this.href, download: this.download });
    });
    const bff = stubBff({
      'admin/exports/catalog': catalogBody,
      'admin/waves': wavesBody,
      'admin/exports': paged([exportJob()]),
      'admin/exports/0190f0e2-2222-7000-8000-000000000001/file': () =>
        new Response('participant_pseudonym,tier_code\r\n', {
          headers: { 'content-type': 'text/csv', 'content-disposition': 'attachment; filename="score_results-0190f0e2.csv"' },
        }),
    });
    view();

    await userEvent.setup().click(await screen.findByRole('button', { name: /Download the CSV/ }));

    await waitFor(() => expect(saved).toEqual([{ href: 'blob:export', download: 'score_results-0190f0e2.csv' }]));
    expect(bff.calls.some((call) => call.url.pathname === '/bff/admin/exports/0190f0e2-2222-7000-8000-000000000001/file')).toBe(true);
    expect(screen.queryByRole('alert')).toBeNull();
    click.mockRestore();
  });

  it('shows an error instead of saving the answer when the download is refused', async () => {
    const { createObjectURL } = stubObjectUrls();
    stubBff({
      'admin/exports/catalog': catalogBody,
      'admin/waves': wavesBody,
      'admin/exports': paged([exportJob()]),
      'admin/exports/0190f0e2-2222-7000-8000-000000000001/file': problem(401, 'bff.signed_out'),
    });
    view();

    await userEvent.setup().click(await screen.findByRole('button', { name: /Download the CSV/ }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The file could not be downloaded. If your session has ended, sign in again; otherwise try again.');
    expect(createObjectURL).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /Download the CSV/ })).toBeEnabled();
  });

  it('reads the list again while an export is being written', async () => {
    let reads = 0;
    stubBff({
      'admin/exports/catalog': catalogBody,
      'admin/waves': wavesBody,
      'admin/exports': () => {
        reads += 1;
        return paged([reads < 2 ? exportJob({ status: 'running', downloadPath: null }) : exportJob()]);
      },
    });
    view(30);

    const table = await screen.findByRole('table', { name: 'Your exports' });
    expect(within(table).getByText('Being written')).toBeInTheDocument();
    expect(await within(table).findByText('Ready')).toBeInTheDocument();
    const settled = reads;
    await new Promise((resolve) => setTimeout(resolve, 120));
    expect(reads).toBe(settled);
  });

  it('shows an empty list as an invitation to request one', async () => {
    stubBff({ 'admin/exports/catalog': catalogBody, 'admin/exports': paged([]), 'admin/waves': wavesBody });
    view();

    expect(await screen.findByRole('heading', { name: 'No exports yet' })).toBeInTheDocument();
  });
});
