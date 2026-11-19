/**
 * Runs async tasks one after another, in the order they were queued. A failed task does not stop the next. Stores
 * use it so a read-check-write (such as "same option chosen again changes nothing") is never interleaved.
 */
export function createSerialQueue() {
  let tail: Promise<unknown> = Promise.resolve();
  return function run<T>(task: () => Promise<T>): Promise<T> {
    const result = tail.then(task, task);
    tail = result.catch(() => undefined);
    return result;
  };
}
