// Empty in local development, where Vite proxies API paths to the local API.
const baseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

export function apiUrl(path: string): string {
  return new URL(path, baseUrl || window.location.origin).toString()
}
