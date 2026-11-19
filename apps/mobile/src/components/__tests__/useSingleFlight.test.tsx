import { act, renderHook } from '@testing-library/react-native';

import { useSingleFlight } from '../useSingleFlight';

describe('useSingleFlight', () => {
  it('ignores a second start while the first action is still running, even within the same frame', async () => {
    const { result } = await renderHook(() => useSingleFlight());
    let finish: () => void = () => undefined;
    const action = jest.fn(() => new Promise<void>((resolve) => (finish = resolve)));

    let first: Promise<void> = Promise.resolve();
    await act(async () => {
      first = result.current(action);
      void result.current(action);
      void result.current(action);
    });
    expect(action).toHaveBeenCalledTimes(1);

    await act(async () => {
      finish();
      await first;
    });
    await act(async () => {
      void result.current(action);
    });
    expect(action).toHaveBeenCalledTimes(2);
  });

  it('runs again after an action fails', async () => {
    const { result } = await renderHook(() => useSingleFlight());
    const failing = jest.fn(async () => {
      throw new Error('refused');
    });

    await act(async () => {
      await expect(result.current(failing)).rejects.toThrow('refused');
    });
    await act(async () => {
      await expect(result.current(failing)).rejects.toThrow('refused');
    });

    expect(failing).toHaveBeenCalledTimes(2);
  });
});
