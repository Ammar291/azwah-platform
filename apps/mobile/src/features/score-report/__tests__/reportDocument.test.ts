import type { ReportAuthority, ReportService, ScoreReport } from '@/api/participant/schemas';
import { authorityMarkSvg } from '@/features/services/authorityMarks';

import { escapeHtml, reportDocument, reportFileName, type ReportDocumentWords } from '../reportDocument';

const REPORT: ScoreReport = {
  sessionId: '0199e0a1-0000-7000-8000-000000000001',
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
  nextSteps: [{ sectionCode: 'wealth_debt', area: 'A synthetic area', reason: 'A synthetic reason.', steps: ['First step.', 'Second step.'], standing: 0.5, authorities: [] }],
  modelName: 'synthetic-model',
  generatedAt: '2026-11-05T09:00:00Z',
};

const WORDS: ReportDocumentWords = {
  title: "Your family's report",
  generatedOn: 'Written on 5 November 2026',
  totalLabel: 'Total score',
  bandLabel: 'Band',
  bandName: (code) => ({ vulnerable: 'Vulnerable', transitional: 'Transitional', flourishing: 'Flourishing' })[code] ?? code,
  scaleTitle: 'Where your score stands',
  range: (min, max) => `${min}–${max}`,
  yourFamily: 'Your family: 96',
  summary: 'Summary',
  strengths: 'Strengths',
  weaknesses: 'Weaknesses',
  flowTitle: 'Your next steps',
  flowStep: (number, area) => `Step ${number}: ${area}`,
  whereToGo: 'Where to go',
  noAuthorities: 'Not added yet.',
  emirate: (code) => ({ abu_dhabi: 'Abu Dhabi', dubai: 'Dubai' })[code] ?? code,
  separator: ', ',
  privateProvider: 'A private provider.',
  websiteLabel: 'Website:',
  phoneLabel: 'Phone:',
  checked: (date) => `Checked on ${date}`,
  flowNotice: 'Azwah contacts nobody.',
  notice: 'An AI model wrote this.',
  emergency: 'In an emergency, call 999.',
  formatNumber: String,
};

describe('the report document', () => {
  it('holds the recorded score, the scale, every section and the notice', () => {
    const html = reportDocument(REPORT, WORDS);

    for (const text of [
      '96',
      'Transitional',
      'Where your score stands',
      '39–78',
      '118–156',
      'Your family: 96',
      'A synthetic summary.',
      'A synthetic strength',
      'A synthetic weakness',
      'A synthetic area',
      'A synthetic reason.',
      'First step.',
      'Second step.',
      'An AI model wrote this.',
      'In an emergency, call 999.',
    ]) {
      expect(html).toContain(text);
    }
  });

  describe('the next steps', () => {
    const SERVICE: ReportService = {
      code: 'synthetic_service',
      name: 'A synthetic service',
      summary: 'What the service is.',
      need: 'Family counselling',
      procedure: ['Book a visit.', 'Bring your papers.'],
      documents: ['An identity card.'],
      website: 'https://example.org/synthetic/service',
      via: 'A portal',
    };
    const AUTHORITY: ReportAuthority = {
      code: 'tamm',
      kind: 'government',
      name: 'A synthetic ministry',
      purpose: 'Helps with synthetic things.',
      emirates: ['abu_dhabi', 'dubai'],
      services: [SERVICE],
      website: 'https://example.org/synthetic',
      phone: '+971 2 000 0000',
      checkedOn: '2026-01-15',
    };
    const withAuthority = (authorities: ScoreReport['nextSteps'][number]['authorities']): ScoreReport => ({
      ...REPORT,
      nextSteps: [{ ...REPORT.nextSteps[0]!, authorities }],
    });

    it('sets out each step with where to go and how to reach them, compactly (D-145)', () => {
      const html = reportDocument(withAuthority([AUTHORITY]), WORDS);

      for (const text of [
        'Your next steps',
        'Step 1: A synthetic area',
        'A synthetic ministry',
        'Abu Dhabi, Dubai',
        'A synthetic service',
        'https://example.org/synthetic/service',
        'https://example.org/synthetic',
        '+971 2 000 0000',
        'Checked on 2026-01-15',
        'Azwah contacts nobody.',
      ]) {
        expect(html).toContain(text);
      }
      // The service's own page holds the procedure; the report leaves it, and the long descriptions, to All services.
      for (const text of ['Helps with synthetic things.', 'What the service is.', 'Family counselling', 'Book a visit.', 'An identity card.', 'A portal']) {
        expect(html).not.toContain(text);
      }
    });

    it('draws Azwah\'s own symbol inline, with nothing to load', () => {
      const html = reportDocument(withAuthority([AUTHORITY]), WORDS);

      expect(html).toContain(authorityMarkSvg('tamm', 28));
      expect(html).toContain("default-src 'none'");
    });

    it('says when a provider is private, and only then', () => {
      expect(reportDocument(withAuthority([{ ...AUTHORITY, kind: 'private' }]), WORDS)).toContain('A private provider.');
      expect(reportDocument(withAuthority([AUTHORITY]), WORDS)).not.toContain('A private provider.');
    });

    it('says plainly when the graph reached no organisation', () => {
      const html = reportDocument(withAuthority([]), WORDS);

      expect(html).toContain('Not added yet.');
      expect(html).not.toContain('class="authority"');
    });

    it('never lets the graph\'s text or a code become markup either', () => {
      const html = reportDocument(
        withAuthority([
          {
            ...AUTHORITY,
            code: '<script>y</script>',
            name: '<script>x</script>',
            services: [{ ...SERVICE, name: '<iframe>', website: '<img src=x>' }],
          },
        ]),
        WORDS,
      );

      expect(html).not.toMatch(/<script|<img|<iframe|<b>/i);
    });

    it('leaves the whole section out when there are no steps', () => {
      const html = reportDocument({ ...REPORT, nextSteps: [] }, WORDS);

      expect(html).not.toContain('Your next steps');
      expect(html).not.toContain('Azwah contacts nobody.');
    });
  });

  it('marks only the recorded band as the family’s', () => {
    expect(reportDocument(REPORT, WORDS).match(/Your family: 96/g)).toHaveLength(1);
    expect(reportDocument({ ...REPORT, tierCode: 'other' }, WORDS)).not.toContain('Your family: 96');
  });

  it('runs left to right in English and right to left in Arabic', () => {
    expect(reportDocument(REPORT, WORDS)).toContain('<html lang="en" dir="ltr">');
    expect(reportDocument({ ...REPORT, language: 'ar' }, WORDS)).toContain('<html lang="ar" dir="rtl">');
  });

  it('positions the marker by logical start', () => {
    const html = reportDocument(REPORT, WORDS);

    expect(html).toMatch(/inset-inline-start: [\d.]+%/);
    expect(html).not.toMatch(/(?:^|[\s;{])(?:left|right):/m);
  });

  it('never lets a model’s text become markup', () => {
    const html = reportDocument(
      {
        ...REPORT,
        summary: '<script>alert(1)</script>',
        strengths: [{ title: '<img src=x onerror=alert(1)>', detail: '"quoted" & \'more\'' }],
        nextSteps: [{ sectionCode: 'wealth_debt', area: '</h3><iframe>', reason: '<b>', steps: ['<a href="https://example.org">link</a>'], standing: 0.5, authorities: [] }],
      },
      WORDS,
    );

    expect(html).not.toMatch(/<script|<img|<iframe|<a /i);
    expect(html).toContain('&lt;script&gt;alert(1)&lt;/script&gt;');
    expect(html).toContain('&quot;quoted&quot; &amp; &#39;more&#39;');
  });

  it('forbids every outside resource', () => {
    const html = reportDocument(REPORT, WORDS);

    expect(html).toContain(`content="default-src 'none'; style-src 'unsafe-inline'"`);
    expect(html).not.toMatch(/https?:\/\//);
  });

  it('leaves out a section the model had nothing for', () => {
    const html = reportDocument({ ...REPORT, strengths: [] }, WORDS);

    expect(html).not.toContain('<h2>Strengths</h2>');
    expect(html).toContain('<h2>Weaknesses</h2>');
  });
});

describe('the helpers', () => {
  it('escapes the five characters that matter in HTML', () => {
    expect(escapeHtml(`<>&"'`)).toBe('&lt;&gt;&amp;&quot;&#39;');
  });

  it('names the file by date alone', () => {
    expect(reportFileName('2026-11-05T09:00:00Z')).toBe('azwah-report-2026-11-05.pdf');
  });
});
