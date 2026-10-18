import { apiUrl } from './apiUrl.ts'

export async function isApiHealthy(): Promise<boolean> {
  try {
    const response = await fetch(apiUrl('/health'))
    return response.ok && (await response.text()) === 'Healthy'
  } catch {
    return false
  }
}
