import type { ReportAuthority, ReportService, ScoreReport } from '@/api/participant/schemas';
import { authorityMarkSvg } from '@/features/services/authorityMarks';
import { bandShare, scalePosition } from '@/features/sessions/scalePosition';

/** The translated words a report's document uses, so this module stays a pure function of its input. */
export interface ReportDocumentWords {
  title: string;
  generatedOn: string;
  totalLabel: string;
  bandLabel: string;
  bandName: (code: string) => string;
  scaleTitle: string;
  range: (min: number, max: number) => string;
  yourFamily: string;
  summary: string;
  strengths: string;
  weaknesses: string;
  flowTitle: string;
  flowStep: (number: number, area: string) => string;
  whereToGo: string;
  noAuthorities: string;
  emirate: (code: string) => string;
  separator: string;
  privateProvider: string;
  websiteLabel: string;
  phoneLabel: string;
  checked: (date: string) => string;
  flowNotice: string;
  notice: string;
  emergency: string;
  formatNumber: (value: number) => string;
}

/** Everything in the document that did not come from this app is text, never markup. */
export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

const BAND_COLORS = ['#e3ece6', '#4f7e6a', '#114332'];

function bandColor(index: number, count: number): string {
  if (count <= 1) return '#4f7e6a';
  return BAND_COLORS[Math.round((index / (count - 1)) * (BAND_COLORS.length - 1))] ?? '#4f7e6a';
}

function pointList(points: ScoreReport['strengths']): string {
  return points
    .map((point) => `<div class="entry"><h3>${escapeHtml(point.title)}</h3><p>${escapeHtml(point.detail)}</p></div>`)
    .join('');
}

/**
 * The report as a self-contained page for the phone's PDF printer. It draws the scale with plain CSS that follows the
 * language's direction. It loads nothing: a content policy forbids every outside resource, and every value is escaped,
 * so a model's text cannot add markup, reach the network or run anything.
 */
export function reportDocument(report: ScoreReport, words: ReportDocumentWords): string {
  const { scale } = report;
  const position = scalePosition(report.totalScore, scale) * 100;
  const segments = scale.bands
    .map(
      (band, index) =>
        `<div class="segment" style="flex-grow:${bandShare(band, scale)};background:${bandColor(index, scale.bands.length)}"></div>`,
    )
    .join('');
  const bands = scale.bands
    .map((band, index) => {
      const current = band.code === report.tierCode;
      return `<tr class="${current ? 'current' : ''}"><td><span class="swatch" style="background:${bandColor(index, scale.bands.length)}"></span>${escapeHtml(words.bandName(band.code))}</td><td class="range">${escapeHtml(words.range(band.min, band.max))}</td><td>${current ? escapeHtml(words.yourFamily) : ''}</td></tr>`;
    })
    .join('');
  // Compact (D-145): where to go and how to reach them; each service's own page, printed, holds its procedure.
  const service = (entry: ReportService) =>
    `<li>${escapeHtml(entry.name)}${entry.website ? ` <span class="ltr muted">${escapeHtml(entry.website)}</span>` : ''}</li>`;
  const contact = (label: string, value: string | null) =>
    value ? `<span class="contact"><span class="label">${escapeHtml(label)}</span> <span class="ltr">${escapeHtml(value)}</span></span>` : '';
  const authority = (entry: ReportAuthority) => `<div class="authority">
        <div class="heading">${authorityMarkSvg(entry.code, 28)}<div><h4>${escapeHtml(entry.name)}</h4><p class="muted">${escapeHtml(entry.emirates.map(words.emirate).join(words.separator))}${entry.kind === 'private' ? ` · ${escapeHtml(words.privateProvider)}` : ''}</p></div></div>
        ${entry.services.length > 0 ? `<ul>${entry.services.map(service).join('')}</ul>` : ''}
        ${entry.website || entry.phone ? `<p>${contact(words.websiteLabel, entry.website)}${contact(words.phoneLabel, entry.phone)}</p>` : ''}
        <p class="muted">${escapeHtml(words.checked(entry.checkedOn))}</p>
      </div>`;
  const nextSteps = report.nextSteps
    .map(
      (step, index) => `<div class="entry">
        <h3>${escapeHtml(words.flowStep(index + 1, step.area))}</h3>
        <p>${escapeHtml(step.reason)}</p>
        <ul>${step.steps.map((text) => `<li>${escapeHtml(text)}</li>`).join('')}</ul>
        <p class="label">${escapeHtml(words.whereToGo)}</p>
        ${step.authorities.length > 0 ? step.authorities.map(authority).join('') : `<p class="muted">${escapeHtml(words.noAuthorities)}</p>`}
      </div>`,
    )
    .join('');
  const flow = nextSteps ? `${nextSteps}<p class="muted">${escapeHtml(words.flowNotice)}</p>` : '';
  const section = (title: string, body: string) => (body ? `<section><h2>${escapeHtml(title)}</h2>${body}</section>` : '');

  return `<!doctype html>
<html lang="${report.language}" dir="${report.language === 'ar' ? 'rtl' : 'ltr'}">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
<title>${escapeHtml(words.title)}</title>
<style>
  @page { margin: 40px; }
  * { box-sizing: border-box; }
  body { margin: 0; color: #0b2b1f; font: 12pt/1.6 sans-serif; }
  h1 { font-size: 22pt; line-height: 1.3; margin: 0; color: #114332; }
  h2 { font-size: 15pt; margin: 22px 0 8px; color: #114332; border-bottom: 2px solid #ddd9cd; padding-bottom: 4px; break-after: avoid; }
  h3 { font-size: 12pt; margin: 0 0 2px; break-after: avoid; }
  h4 { font-size: 11pt; margin: 0 0 2px; break-after: avoid; }
  .heading { display: flex; gap: 10px; align-items: center; margin-bottom: 6px; }
  .heading svg { flex-shrink: 0; }
  .service { margin: 8px 0; }
  p { margin: 0 0 6px; }
  ul, ol { margin: 0 0 6px; padding-inline-start: 20px; }
  .authority { margin: 6px 0; padding: 8px 10px; border: 1px solid #ddd9cd; break-inside: avoid; }
  .contact { margin-inline-end: 16px; }
  .ltr { unicode-bidi: isolate; direction: ltr; }
  .entry { margin-bottom: 12px; break-inside: avoid; }
  .label { font-weight: 600; color: #3e4c45; }
  .muted { color: #5e6a63; font-size: 10pt; }
  .figures { display: flex; gap: 32px; margin: 16px 0; }
  .figure strong { display: block; font-size: 18pt; line-height: 1.2; color: #114332; }
  .scale { position: relative; display: flex; align-items: center; gap: 2px; height: 20px; margin-top: 8px; }
  .segment { height: 14px; flex-basis: 0; border: 1px solid #7d8781; }
  .marker { position: absolute; top: 0; inset-inline-start: ${position}%; margin-inline-start: -10px; width: 20px; height: 20px; border-radius: 50%; background: #0b2b1f; border: 2px solid #fff; }
  .ends { display: flex; justify-content: space-between; }
  table { width: 100%; border-collapse: collapse; margin-top: 8px; }
  td { padding: 4px 0; text-align: start; }
  .current td { font-weight: 600; }
  .range { unicode-bidi: isolate; }
  .swatch { display: inline-block; width: 12px; height: 12px; margin-inline-end: 8px; border: 1px solid #7d8781; }
  .notice { margin-top: 24px; padding: 12px; background: #f5f3ec; break-inside: avoid; }
</style>
</head>
<body>
  <h1>${escapeHtml(words.title)}</h1>
  <p class="muted">${escapeHtml(words.generatedOn)}</p>
  <div class="figures">
    <div class="figure"><span class="muted">${escapeHtml(words.totalLabel)}</span><strong>${escapeHtml(words.formatNumber(report.totalScore))}</strong></div>
    <div class="figure"><span class="muted">${escapeHtml(words.bandLabel)}</span><strong>${escapeHtml(words.bandName(report.tierCode))}</strong></div>
  </div>
  <section>
    <h2>${escapeHtml(words.scaleTitle)}</h2>
    <div class="scale">${segments}<div class="marker"></div></div>
    <div class="ends muted"><span>${escapeHtml(words.formatNumber(scale.minimum))}</span><span>${escapeHtml(words.formatNumber(scale.maximum))}</span></div>
    <table>${bands}</table>
  </section>
  ${section(words.summary, `<p>${escapeHtml(report.summary)}</p>`)}
  ${section(words.strengths, pointList(report.strengths))}
  ${section(words.weaknesses, pointList(report.weaknesses))}
  ${section(words.flowTitle, flow)}
  <div class="notice"><p>${escapeHtml(words.notice)}</p><p class="label">${escapeHtml(words.emergency)}</p></div>
</body>
</html>`;
}

/** The file's name holds only the date the report was written, never anything about the household. */
export function reportFileName(generatedAt: string): string {
  return `azwah-report-${generatedAt.slice(0, 10)}.pdf`;
}
