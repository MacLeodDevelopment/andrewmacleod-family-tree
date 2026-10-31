import { useEffect, useState } from 'react'

/** The value, once it has stopped changing for `delayMs`. Keeps typing from using up the rate limit. */
export function useDebounced<T>(value: T, delayMs: number): T {
  const [settled, setSettled] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return settled
}
