import { act, renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { dayParam, useSearchState } from './url-state';

/** A router whose navigation has not finished yet: `replace` is recorded, but the URL the page reads stays the same. */
const replace = vi.fn<(url: string) => void>();
vi.mock('next/navigation', () => ({
  usePathname: () => '/overview',
  useSearchParams: () => new URLSearchParams('from=2026-01-01'),
  useRouter: () => ({ replace }),
}));

interface DayWindow {
  from: string | null;
  to: string | null;
}

const parse = (params: URLSearchParams): DayWindow => ({ from: dayParam(params, 'from'), to: dayParam(params, 'to') });
const serialize = (value: DayWindow) => ({ ...value });

describe('useSearchState', () => {
  it('keeps both of two changes made before the URL catches up', () => {
    const { result } = renderHook(() => useSearchState(parse, serialize));

    act(() => {
      result.current[1]((current) => ({ ...current, from: '2026-02-01' }));
      result.current[1]((current) => ({ ...current, to: '2026-03-31' }));
    });

    expect(replace).toHaveBeenLastCalledWith('/overview?from=2026-02-01&to=2026-03-31', { scroll: false });
  });

  it('still takes a whole value', () => {
    const { result } = renderHook(() => useSearchState(parse, serialize));

    act(() => result.current[1]({ from: null, to: '2026-05-01' }));

    expect(replace).toHaveBeenLastCalledWith('/overview?to=2026-05-01', { scroll: false });
  });
});
