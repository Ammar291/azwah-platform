import { useSyncExternalStore } from 'react';
import { vi } from 'vitest';

/**
 * An in-memory stand-in for `next/navigation`, so views that keep their filters in the URL can be tested: `replace`
 * changes the search parameters and re-renders whoever reads them. Use with
 * `vi.mock('next/navigation', async () => (await import('@/test/navigation')).nextNavigation)`.
 */
let pathname = '/';
let search = new URLSearchParams();
const listeners = new Set<() => void>();

function emit() {
  for (const listener of listeners) listener();
}

const replace = vi.fn((url: string) => {
  const [path, query = ''] = url.split('?');
  pathname = path || pathname;
  search = new URLSearchParams(query);
  emit();
});

export const navigation = {
  replace,
  /** Starts a test at a path with a query string. */
  reset(path: string, query = '') {
    pathname = path;
    search = new URLSearchParams(query);
    replace.mockClear();
    emit();
  },
  search: () => search,
};

export const nextNavigation = {
  usePathname: () => useSyncExternalStore(subscribe, () => pathname),
  useSearchParams: () => useSyncExternalStore(subscribe, () => search),
  useRouter: () => ({ replace, push: replace, refresh: vi.fn(), back: vi.fn(), forward: vi.fn(), prefetch: vi.fn() }),
  redirect: vi.fn(),
  notFound: vi.fn(),
};

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
