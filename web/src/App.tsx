import { Account } from './features/account/Account.tsx'
import { ApiStatus } from './features/status/ApiStatus.tsx'

export default function App() {
  return (
    <>
      <header className="site-header">
        <strong>Family Tree</strong>
        <Account />
      </header>
      <main className="container">
        <h1>MacLeod Family Tree</h1>
        <p>A private family tree for invited family members.</p>
        <ApiStatus />
      </main>
      <footer className="site-footer">
        <p>&copy; Andrew MacLeod</p>
      </footer>
    </>
  )
}
