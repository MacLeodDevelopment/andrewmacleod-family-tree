import { Link, Route, Routes } from 'react-router'
import { Account } from './features/account/Account.tsx'
import { AddPersonPage } from './features/people/AddPersonPage.tsx'
import { EditPersonPage } from './features/people/EditPersonPage.tsx'
import { PeopleListPage } from './features/people/PeopleListPage.tsx'
import { PersonPage } from './features/people/PersonPage.tsx'
import { RequireAdmin } from './features/people/RequireAdmin.tsx'
import { ApiStatus } from './features/status/ApiStatus.tsx'
import { adminRole, useMe } from './shared/api/me.ts'

function Home() {
  return (
    <>
      <h1>MacLeod Family Tree</h1>
      <p>A private family tree for invited family members.</p>
      <ApiStatus />
    </>
  )
}

/** The site title, and links to what the signed-in user can use. */
function SiteNav() {
  const me = useMe()

  return (
    <nav className="site-nav" aria-label="Main">
      <Link className="site-title" to="/">
        Family Tree
      </Link>
      {me.data?.roles.includes(adminRole) && <Link to="/people">People</Link>}
    </nav>
  )
}

export default function App() {
  return (
    <>
      <header className="site-header">
        <SiteNav />
        <Account />
      </header>
      <main className="container">
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/people" element={<RequireAdmin><PeopleListPage /></RequireAdmin>} />
          <Route path="/people/new" element={<RequireAdmin><AddPersonPage /></RequireAdmin>} />
          <Route path="/people/:id" element={<RequireAdmin><PersonPage /></RequireAdmin>} />
          <Route path="/people/:id/edit" element={<RequireAdmin><EditPersonPage /></RequireAdmin>} />
          <Route path="*" element={<p role="alert">This page doesn't exist.</p>} />
        </Routes>
      </main>
      <footer className="site-footer">
        <p>&copy; Andrew MacLeod</p>
      </footer>
    </>
  )
}
