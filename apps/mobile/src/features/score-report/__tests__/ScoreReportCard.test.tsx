import { screen, userEvent, waitFor } from '@testing-library/react-native';

import { ApiError } from '@/api/client';
import type { ParticipantApi } from '@/api/participant/participantApi';
import type { ScoreReport, ScoreReportSummary } from '@/api/participant/schemas';
import { ShareInterruptedError } from '@/storage/exportFiles';
import { renderWithProviders, settleQueries } from '@/test-utils/renderWithProviders';
import { answerEverything, leadSession } from '@/test-utils/sessionWorlds';

import { planReport } from '../reportPlan';
import { createMemoryScoreReportStore } from '../reportStore';
import { ScoreReportCard } from '../ScoreReportCard';
import type { ReportFiles } from '../reportFiles';

jest.mock('expo-router', () => jest.requireActual('@/test-utils/expoRouterMock'));
const { setSearchParams } = jest.requireMock<typeof import('@/test-utils/expoRouterMock')>('expo-router');

const SESSION = '0199e0a1-0000-7000-8000-000000000001';

const REPORT: ScoreReport = {
  sessionId: SESSION,
  language: 'en',
  totalScore: 96,
  tierCode: 'transitional',
  scale: {
    minimum: 39,
    maximum: 156,
    bands: [
      { code: 'vulnerable', min: 39, max: 78 },
      { code: 'transitional', min: 79, max: 117 },
      { code: 'flourishing', min: 118, max: 156 },
    ],
  },
  summary: 'A synthetic summary.',
  strengths: [{ title: 'A synthetic strength', detail: 'Shown by the answers.' }],
  weaknesses: [{ title: 'A synthetic weakness', detail: 'Needs attention.' }],
  nextSteps: [{ sectionCode: 'wealth_debt', area: 'A synthetic area', reason: 'A synthetic reason.', steps: ['A synthetic step.'], standing: 0.5, authorities: [] }],
  modelName: 'synthetic-model',
  generatedAt: '2026-11-05T09:00:00Z',
};

const RECORDED = { totalScore: REPORT.totalScore, tierCode: REPORT.tierCode, scale: REPORT.scale };

/** The fixture API with its model answers replaced; `scoreReport` says whether a model is connected. */
async function apiWith(overrides: Partial<ParticipantApi> = {}, scoreReport = true): Promise<ParticipantApi> {
  const { api } = await leadSession();
  return {
    ...api,
    getAssistantAvailability: async () => ({ assistant: false, consultant: false, scoreReport }),
    createScoreReport: jest.fn(async () => REPORT),
    ...overrides,
  };
}

const fakeFiles = (overrides: Partial<ReportFiles> = {}): ReportFiles => ({
  isAvailable: jest.fn(async () => true),
  savePdf: jest.fn(async () => 'saved' as const),
  ...overrides,
});

afterEach(settleQueries);

describe('a report written as the person answered', () => {
  /** The lead's own session, with every question answered on the server, and the parts a model wrote for those answers. */
  async function answeredWorld(options: { writeParts?: boolean } = {}) {
    const lead = await leadSession();
    await answerEverything(lead.api, lead.sessionId);
    setSearchParams({ sessionId: lead.sessionId });
    const reports = createMemoryScoreReportStore();
    await reports.setChoice(lead.sessionId, 'on');
    const session = await lead.api.getSession(lead.sessionId);
    const assignment = await lead.api.getAssignment(session.assignmentId);
    const version = await lead.api.getPublishedVersion(assignment.instrumentCode, session.versionNumber);
    const plan = planReport(version, session.sectionCode, await lead.api.getAnswers(lead.sessionId));
    if (options.writeParts !== false) {
      for (const planned of plan.parts) {
        await reports.savePart(
          lead.sessionId,
          'en',
          planned.sectionCode === null
            ? { key: planned.key, fingerprint: planned.fingerprint, kind: 'summary', summary: { language: 'en', summary: 'A summary written ahead.', modelName: 'synthetic-model', generatedAt: REPORT.generatedAt } }
            : {
                key: planned.key,
                fingerprint: planned.fingerprint,
                kind: 'section',
                part: {
                  sectionCode: planned.sectionCode,
                  language: 'en',
                  strengths: [{ title: `Strength in ${planned.sectionCode}`, detail: 'D' }],
                  weaknesses: [],
                  nextSteps: [],
                  modelName: 'synthetic-model',
                  generatedAt: REPORT.generatedAt,
                  standing: 0.5,
                },
              },
        );
      }
    }
    return { ...lead, reports, parts: plan.parts };
  }

  it('is ready at once, with no call to a model, and keeps the score the server recorded', async () => {
    const { api, sessionId, reports } = await answeredWorld();
    const createScoreReport = jest.spyOn(api, 'createScoreReport');
    const createScoreReportPart = jest.spyOn(api, 'createScoreReportPart');
    const createScoreReportSummary = jest.spyOn(api, 'createScoreReportSummary');
    await renderWithProviders(<ScoreReportCard sessionId={sessionId} recorded={RECORDED} files={fakeFiles()} />, { api, reports });

    expect(await screen.findByText('A summary written ahead.')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Download PDF' })).toBeOnTheScreen();
    expect(createScoreReport).not.toHaveBeenCalled();
    expect(createScoreReportPart).not.toHaveBeenCalled();
    expect(createScoreReportSummary).not.toHaveBeenCalled();
    await waitFor(async () => {
      const saved = await reports.get(sessionId, 'en');
      expect(saved).toMatchObject({ totalScore: REPORT.totalScore, tierCode: REPORT.tierCode, summary: 'A summary written ahead.' });
      expect(await reports.listParts(sessionId, 'en')).toEqual([]);
    });
  });

  it('writes what is missing, saying so, and shows the report when it arrives', async () => {
    const { api, sessionId, reports, parts } = await answeredWorld({ writeParts: false });
    let finish: (summary: ScoreReportSummary) => void = () => undefined;
    const createScoreReportSummary = jest.fn(() => new Promise<ScoreReportSummary>((resolve) => (finish = resolve)));
    const createScoreReportPart = jest.fn(api.createScoreReportPart);
    await renderWithProviders(<ScoreReportCard sessionId={sessionId} recorded={RECORDED} />, {
      api: { ...api, createScoreReportSummary, createScoreReportPart },
      reports,
    });

    expect(await screen.findByText('Finishing your report…')).toBeOnTheScreen();
    await waitFor(() => expect(createScoreReportSummary).toHaveBeenCalledTimes(1));
    finish({ language: 'en', summary: 'A summary written late.', modelName: 'synthetic-model', generatedAt: REPORT.generatedAt });

    expect(await screen.findByText('A summary written late.')).toBeOnTheScreen();
    expect(createScoreReportPart).toHaveBeenCalledTimes(parts.length - 1);
  });

  it('offers the whole-report call when the missing parts cannot be written', async () => {
    const { api, sessionId, reports } = await answeredWorld({ writeParts: false });
    await renderWithProviders(<ScoreReportCard sessionId={sessionId} recorded={RECORDED} />, {
      api: {
        ...api,
        getAssistantAvailability: async () => ({ assistant: false, consultant: false, scoreReport: true }),
        createScoreReportPart: () => Promise.reject(new ApiError('server', 502)),
        createScoreReportSummary: () => Promise.reject(new ApiError('server', 502)),
      },
      reports,
    });

    expect(await screen.findByRole('button', { name: 'Create my report' })).toBeOnTheScreen();
  });
});

describe('asking for a report', () => {
  it('offers the report without naming an AI provider, and sends nothing by opening (D-142)', async () => {
    const api = await apiWith();
    await renderWithProviders(<ScoreReportCard sessionId={SESSION} recorded={RECORDED} />, { api });

    expect(await screen.findByTestId('report-offer')).toBeOnTheScreen();
    expect(screen.queryByText(/OpenAI/)).toBeNull();
    expect(api.createScoreReport).not.toHaveBeenCalled();
  });

  it('names no model on the report it writes, and still says AI wrote it (D-142)', async () => {
    const user = userEvent.setup();
    const api = await apiWith();
    await renderWithProviders(<ScoreReportCard sessionId={SESSION} recorded={RECORDED} />, { api });

    await user.press(await screen.findByRole('button', { name: 'Create my report' }));

    expect(await screen.findByTestId('report')).toBeOnTheScreen();
    expect(screen.getByText('Written by AI')).toBeOnTheScreen();
    expect(screen.queryByText(new RegExp(REPORT.modelName))).toBeNull();
    // Still labelled as AI writing that may be wrong: a product notice, not a demonstration one.
    expect(screen.getByText(/may be wrong/)).toBeOnTheScreen();
  });

  it('offers nothing and promises nothing when no model is connected', async () => {
    const api = await apiWith({}, false);
    const { queryClient } = await renderWithProviders(<ScoreReportCard sessionId={SESSION} recorded={RECORDED} />, { api });

    await waitFor(() => expect(queryClient.isFetching()).toBe(0));
    expect(screen.queryByTestId('report-offer')).toBeNull();
    expect(screen.queryByRole('button')).toBeNull();
    expect(screen.queryByText(/written up|not switched on|not available/i)).toBeNull();
  });

  it('writes the report in the reader’s language on a tap, shows it and saves it', async () => {
    const user = userEvent.setup();
    const api = await apiWith();
    const reports = createMemoryScoreReportStore();
    await renderWithProviders(<ScoreReportCard sessionId={SESSION} recorded={RECORDED} />, { api, reports, language: 'ar' });

    await user.press(await screen.findByRole('button', { name: 'أنشئ تقريري' }));

    expect(await screen.findByTestId('report')).toBeOnTheScreen();
    expect(api.createScoreReport).toHaveBeenCalledWith(SESSION, 'ar');
    await waitFor(async () => expect(await reports.get(SESSION, 'en')).toEqual(REPORT));
  });

  it('asks only once while a report is being written', async () => {
    const user = userEvent.setup();
    let finish: (report: ScoreReport) => void = () => undefined;
    const createScoreReport = jest.fn(() => new Promise<ScoreReport>((resolve) => (finish = resolve)));
    const api = await apiWith({ createScoreReport });
    await renderWithProviders(<ScoreReportCard sessionId={SESSION} recorded={RECORDED} />, { api });

    const button = await screen.findByRole('button', { name: 'Create my report' });
    await user.press(button);
    expect(await screen.findByText('Up to a minute. Keep this screen open.')).toBeOnTheScreen();
    await user.press(button);

    expect(createScoreReport).toHaveBeenCalledTimes(1);
    finish(REPORT);
    expect(await screen.findByTestId('report')).toBeOnTheScreen();
  });

  it.each([
    ['server', 503, "Reports aren't available now."],
    ['server', 502, "Couldn't write it. Try again."],
    ['network', undefined, "Can't reach Azwah. Check your connection."],
    ['timeout', undefined, "Can't reach Azwah. Check your connection."],
    ['rate_limited', 429, "Too many reports this hour. Try later."],
  ] as const)('says so in words on %s %s, and can try again', async (kind, status, message) => {
    const user = userEvent.setup();
    const createScoreReport = jest.fn<Promise<ScoreReport>, [string, 'en' | 'ar']>().mockRejectedValueOnce(new ApiError(kind, status)).mockResolvedValue(REPORT);
    const api = await apiWith({ createScoreReport });
    await renderWithProviders(<ScoreReportCard sessionId={SESSION} recorded={RECORDED} />, { api });

    await user.press(await screen.findByRole('button', { name: 'Create my report' }));
    expect(await screen.findByText(message)).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Create my report' }));
    expect(await screen.findByTestId('report')).toBeOnTheScreen();
  });
});

describe('a written report', () => {
  async function shown(files: ReportFiles = fakeFiles(), overrides: Partial<ScoreReport> = {}) {
    const api = await apiWith();
    const reports = createMemoryScoreReportStore();
    await reports.save({ ...REPORT, ...overrides });
    await renderWithProviders(<ScoreReportCard sessionId={SESSION} recorded={RECORDED} files={files} />, { api, reports });
    await screen.findByTestId('report');
    return { api, reports };
  }

  it('shows strengths, weaknesses, where and how to improve, labelled as written by AI', async () => {
    await shown();

    // Marked as AI writing once, by the badge; the caption under the title is the date alone (D-145).
    expect(screen.getAllByText(/Written by AI/)).toHaveLength(1);
    expect(screen.queryByText(/synthetic-model/)).toBeNull();
    expect(screen.getByText('A synthetic summary.')).toBeOnTheScreen();
    expect(screen.getByText('Strengths')).toBeOnTheScreen();
    expect(screen.getByText('A synthetic strength')).toBeOnTheScreen();
    expect(screen.getByText('Weaknesses')).toBeOnTheScreen();
    expect(screen.getByText('A synthetic weakness')).toBeOnTheScreen();
    expect(screen.getByText('Your next steps')).toBeOnTheScreen();
    expect(screen.getByText('Step 1: A synthetic area')).toBeOnTheScreen();
    expect(screen.getByText('A synthetic reason.')).toBeOnTheScreen();
    expect(screen.getByText('• A synthetic step.')).toBeOnTheScreen();
    expect(screen.getByText(/doesn't change your score/)).toBeOnTheScreen();
    expect(screen.getByText('In an emergency, call 999.')).toBeOnTheScreen();
  });

  it('comes from the phone when it is opened again, with no new request', async () => {
    const { api } = await shown();

    expect(api.createScoreReport).not.toHaveBeenCalled();
  });

  it('stays readable when the model has been disconnected since', async () => {
    const api = await apiWith({}, false);
    const reports = createMemoryScoreReportStore();
    await reports.save(REPORT);
    await renderWithProviders(<ScoreReportCard sessionId={SESSION} recorded={RECORDED} />, { api, reports });

    expect(await screen.findByText('A synthetic summary.')).toBeOnTheScreen();
  });

  it('leaves out a section the model had nothing for', async () => {
    await shown(fakeFiles(), { strengths: [] });

    expect(screen.queryByText('Strengths')).toBeNull();
    expect(screen.getByText('Weaknesses')).toBeOnTheScreen();
  });

  it('can be written again, replacing the saved one', async () => {
    const user = userEvent.setup();
    const { api, reports } = await shown();
    jest.mocked(api.createScoreReport).mockResolvedValue({ ...REPORT, summary: 'A newer summary.' });

    await user.press(screen.getByRole('button', { name: 'Write a new report' }));

    expect(await screen.findByText('A newer summary.')).toBeOnTheScreen();
    await waitFor(async () => expect((await reports.get(SESSION, 'en'))?.summary).toBe('A newer summary.'));
  });
});

describe('downloading a report', () => {
  async function shown(files: ReportFiles) {
    const reports = createMemoryScoreReportStore();
    await reports.save(REPORT);
    await renderWithProviders(<ScoreReportCard sessionId={SESSION} recorded={RECORDED} files={files} />, { api: await apiWith(), reports });
    return screen.findByRole('button', { name: 'Download PDF' });
  }

  it('warns that the file is unprotected before the button', async () => {
    await shown(fakeFiles());

    expect(screen.getByText('Anyone with the file can read it.')).toBeOnTheScreen();
  });

  it('makes a PDF from the report, named by date, and confirms it was saved', async () => {
    const user = userEvent.setup();
    const files = fakeFiles();
    await user.press(await shown(files));

    expect(await screen.findByText('Report saved.')).toBeOnTheScreen();
    const [name, html, options] = jest.mocked(files.savePdf).mock.calls[0]!;
    expect(name).toBe('azwah-report-2026-11-05.pdf');
    expect(html).toContain('A synthetic summary.');
    expect(html).toContain('Transitional');
    expect(html).toContain('Your family: 96');
    expect(html).toContain('In an emergency, call 999.');
    expect(options.dialogTitle).toBe('Your Azwah report');
    expect(options.mayShare).toEqual(expect.any(Function));
  });

  it('says nothing when the person closes the chooser', async () => {
    const user = userEvent.setup();
    const files = fakeFiles({ savePdf: jest.fn(async () => 'cancelled' as const) });
    await user.press(await shown(files));

    await waitFor(() => expect(files.savePdf).toHaveBeenCalled());
    expect(screen.queryByText('Report saved.')).toBeNull();
    expect(screen.queryByText("Couldn't save. Try again.")).toBeNull();
  });

  it.each([
    ['a phone that cannot save', fakeFiles({ isAvailable: jest.fn(async () => false) }), "This phone can't save files."],
    ['a failure', fakeFiles({ savePdf: jest.fn(() => Promise.reject(new Error('Disk full'))) }), "Couldn't save. Try again."],
    ['a lock', fakeFiles({ savePdf: jest.fn(() => Promise.reject(new ShareInterruptedError())) }), 'Not saved. Try again.'],
  ])('says so in words on %s', async (_name, files, message) => {
    const user = userEvent.setup();
    await user.press(await shown(files));

    expect(await screen.findByText(message)).toBeOnTheScreen();
  });
});
