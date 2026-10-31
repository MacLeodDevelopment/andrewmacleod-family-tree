import { QueryClient } from '@tanstack/react-query'
import { ApiError } from './client.ts'

/**
 * Keeps data fetched from the API, and refreshes it when it changes. Failures are retried
 * in case the API or database is waking up, except refusals, which won't change on retry.
 */
export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: (failures, error) => failures < 2 && !(error instanceof ApiError && error.status < 500),
      },
    },
  })
}
