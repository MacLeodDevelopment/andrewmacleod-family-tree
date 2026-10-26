/// <reference types="vitest/config" />
import { existsSync, readFileSync } from 'node:fs'
import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv, type Plugin } from 'vite'
import { securityHeaders, staticWebAppConfig } from './build/staticWebApp.ts'

// https://vite.dev/config/
export default defineConfig(({ mode, isPreview }) => {
  // VITE_* settings from .env files and the environment (the deploy workflow sets them).
  const env = loadEnv(mode, process.cwd(), 'VITE_')
  const headers = securityHeaders({ apiBaseUrl: env.VITE_API_BASE_URL })

  // Forward API calls to the local API (`dotnet run --project src/FamilyTree.Api`).
  const proxy = {
    '/health': 'http://localhost:5080',
    '/api': 'http://localhost:5080',
  }

  return {
    plugins: [react(), writeStaticWebAppConfig(headers)],
    server: { proxy },
    // `npm run preview` serves the production build with exactly the headers Azure will
    // (read back from the build's staticwebapp.config.json, not recalculated), on the
    // port registered for sign-in, so the policy can be tried locally.
    preview: { port: 5173, strictPort: true, headers: isPreview ? builtHeaders() : {}, proxy },
    test: {
      environment: 'jsdom',
      setupFiles: './src/test/setup.ts',
    },
  }
})

/** The headers in the last build's staticwebapp.config.json. */
function builtHeaders(): Record<string, string> {
  const path = 'dist/staticwebapp.config.json'
  if (!existsSync(path)) {
    throw new Error(`${path} not found. Run \`npm run build\` before \`npm run preview\`.`)
  }
  return JSON.parse(readFileSync(path, 'utf8')).globalHeaders
}

/** Writes dist/staticwebapp.config.json, which Azure Static Web Apps reads when serving the site. */
function writeStaticWebAppConfig(headers: Record<string, string>): Plugin {
  return {
    name: 'static-web-app-config',
    apply: 'build',
    generateBundle() {
      this.emitFile({
        type: 'asset',
        fileName: 'staticwebapp.config.json',
        source: `${JSON.stringify(staticWebAppConfig(headers), null, 2)}\n`,
      })
    },
  }
}
