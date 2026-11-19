export type QueryValue = string | number | boolean | null | undefined;
export type Query = Record<string, QueryValue>;

/** A query string with empty values dropped and keys sorted, so equal filters give equal URLs and cache keys. */
export function toSearch(values: Query): string {
  const params = new URLSearchParams();
  for (const key of Object.keys(values).sort()) {
    const value = values[key];
    if (value === null || value === undefined || value === '') continue;
    params.set(key, String(value));
  }
  const text = params.toString();
  return text ? `?${text}` : '';
}

const DOWNLOAD = /^\/api\/v1\/admin\/exports\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\/file$/i;

/** The BFF address for an export's download path, or null for anything that is not one. */
export function downloadHref(downloadPath: string | null): string | null {
  const match = downloadPath ? DOWNLOAD.exec(downloadPath) : null;
  return match ? `/bff/admin/exports/${match[1]}/file` : null;
}
