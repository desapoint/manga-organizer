import { useEffect, useState } from 'react';
import { Catalog } from './Catalog';
import type { CatalogView } from './Catalog';

const views: { id: CatalogView; href: string; label: string }[] = [
  { id: 'library', href: '#library', label: 'Library catalog' },
  { id: 'import', href: '#intake', label: 'Import intake' },
  { id: 'settings', href: '#settings', label: 'Library settings' },
];

function currentView() {
  return views.find(view => view.href === window.location.hash)?.id ?? 'library';
}

export function Workspace({ serverId, signingOut, error, onSignOut, onSignedOut }: {
  serverId: string;
  signingOut: boolean;
  error: string;
  onSignOut: () => void;
  onSignedOut: () => void;
}) {
  const [view, setView] = useState(currentView);

  useEffect(() => {
    const handleNavigation = () => {
      setView(currentView());
      window.scrollTo(0, 0);
    };
    window.addEventListener('hashchange', handleNavigation);
    return () => window.removeEventListener('hashchange', handleNavigation);
  }, []);

  return (
    <div className="workspace">
      <aside className="workspace-sidebar" aria-label="Manga Organizer">
        <a className="workspace-brand" href="#library">
          <span className="workspace-mark" aria-hidden="true" />
          Manga Organizer
        </a>
        <nav className="workspace-navigation" aria-label="Library views">
          {views.map(item => (
            <a key={item.id} href={item.href} aria-current={view === item.id ? 'page' : undefined}>{item.label}</a>
          ))}
        </nav>
        <button type="button" className="secondary workspace-signout" onClick={onSignOut} disabled={signingOut}>
          {signingOut ? 'Signing out…' : 'Sign out'}
        </button>
      </aside>
      <main className="workspace-content">
        {error && <p role="alert" className="error">{error}</p>}
        <Catalog view={view} onSignedOut={onSignedOut} />
        {view === 'settings' && (
          <details className="server-details">
            <summary>Server details</summary>
            <dl>
              <dt>Server ID</dt>
              <dd><code>{serverId}</code></dd>
            </dl>
          </details>
        )}
      </main>
    </div>
  );
}
