/// <reference types="vite/client" />

// Build-time settings (see .env.example). All optional: without the Entra ones the
// app runs with sign-in turned off.
interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
  readonly VITE_ENTRA_CLIENT_ID?: string
  readonly VITE_ENTRA_TENANT_ID?: string
}
