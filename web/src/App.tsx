import { Link, Route, Routes } from 'react-router'
import { Account } from './features/account/Account.tsx'
import { AccountLinksPage } from './features/people/AccountLinksPage.tsx'
import { AddPersonPage } from './features/people/AddPersonPage.tsx'
import { EditPersonPage } from './features/people/EditPersonPage.tsx'
import { PeopleListPage } from './features/people/PeopleListPage.tsx'
import { PersonPage } from './features/people/PersonPage.tsx'
import { RequireAdmin, RequireMember } from './features/people/RequireAdmin.tsx'
import { ProfilePage } from './features/profile/ProfilePage.tsx'
import { SearchPage } from './features/profile/SearchPage.tsx'
import { ApiStatus } from './features/status/ApiStatus.tsx'
import { adminRole, useMe } from './shared/api/me.ts'
import { useMyLink } from './shared/api/myLink.ts'

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
  const link = useMyLink()

  return (
    <nav className="site-nav" aria-label="Main">
      <Link className="site-title" to="/">
        Family Tree
      </Link>
      {me.data && me.data.roles.length > 0 && <Link to="/search">Search</Link>}
      {link.data?.isApproved && <Link to={`/family/${link.data.personId}`}>My page</Link>}
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
          <Route path="/search" element={<RequireMember><SearchPage /></RequireMember>} />
          <Route path="/family/:id" element={<RequireMember><ProfilePage /></RequireMember>} />
          <Route path="/people" element={<RequireAdmin><PeopleListPage /></RequireAdmin>} />
          <Route path="/people/accounts" element={<RequireAdmin><AccountLinksPage /></RequireAdmin>} />
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
