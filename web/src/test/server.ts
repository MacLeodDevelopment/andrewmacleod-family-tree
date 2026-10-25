import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'

/**
 * A pretend API for tests. By default it's healthy; tests add or override handlers
 * with `server.use(...)`, and those are reset after each test.
 */
export const server = setupServer(http.get('*/health', () => HttpResponse.text('Healthy')))
