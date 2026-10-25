import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles.css'
import App from './App.tsx'
import { notConfiguredAuth } from './shared/auth/auth.ts'
import { AuthContext } from './shared/auth/authContext.ts'
import { readSignInConfig } from './shared/auth/config.ts'
import { createMsalAuth } from './shared/auth/msalAuth.ts'

// Work out who's signed in before rendering. This also completes a sign-in when the
// page is loading on the way back from Microsoft's sign-in page.
const signInConfig = readSignInConfig()
const auth = signInConfig ? await createMsalAuth(signInConfig) : notConfiguredAuth

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthContext.Provider value={auth}>
      <App />
    </AuthContext.Provider>
  </StrictMode>,
)
