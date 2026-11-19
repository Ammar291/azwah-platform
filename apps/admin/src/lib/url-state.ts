'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { useCallback, useEffect, useMemo, useRef } from 'react';

import { toSearch, type Query } from './api/search';

/**
 * Filters kept in the URL, so a view can be shared, bookmarked and reloaded as it is. `parse` must accept any query
 * string (unknown or malformed values fall back to defaults); `serialize` drops defaults so URLs stay short.
 *
 * Pass an updater (`current => next`) when changing one filter of several: the URL changes only once the router has
 * finished the navigation, so a second change made before then would otherwise start from the old URL and undo the
 * first (a "from" date lost when the "to" date followed quickly). The updater starts from the last value asked for.
 */
export function useSearchState<T extends object>(
  parse: (params: URLSearchParams) => T,
  serialize: (value: T) => Query,
): [T, (next: T | ((current: T) => T)) => void] {
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const text = params.toString();
  const value = useMemo(() => parse(new URLSearchParams(text)), [parse, text]);
  const requested = useRef(value);
  useEffect(() => {
    requested.current = value;
  }, [value]);
  const setValue = useCallback(
    (next: T | ((current: T) => T)) => {
      const resolved = typeof next === 'function' ? next(requested.current) : next;
      requested.current = resolved;
      router.replace(`${pathname}${toSearch(serialize(resolved))}`, { scroll: false });
    },
    [pathname, router, serialize],
  );
  return [value, setValue];
}

const DAY = /^\d{4}-\d{2}-\d{2}$/;

/** A `YYYY-MM-DD` value from the URL, or null when absent or not a real day. */
export function dayParam(params: URLSearchParams, key: string): string | null {
  const value = params.get(key);
  return value && DAY.test(value) && !Number.isNaN(Date.parse(`${value}T00:00:00Z`)) ? value : null;
}

export function oneOf<T extends string>(params: URLSearchParams, key: string, allowed: readonly T[]): T | null {
  const value = params.get(key);
  return value !== null && (allowed as readonly string[]).includes(value) ? (value as T) : null;
}

export function positiveInt(params: URLSearchParams, key: string): number | null {
  const value = Number(params.get(key));
  return Number.isInteger(value) && value >= 1 ? value : null;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function guidParam(params: URLSearchParams, key: string): string | null {
  const value = params.get(key);
  return value && GUID.test(value) ? value : null;
}
