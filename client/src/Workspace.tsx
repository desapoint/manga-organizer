import { Inbox, Library, LogOut, SlidersHorizontal } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Catalog } from './Catalog';
import type { CatalogView } from './Catalog';
import { Brand, Button } from './ui';

type Destination = { id: CatalogView; href: string; label: string; icon: LucideIcon };

const destinations = [
  { id: 'library', href: '#library', label: 'Library catalog', icon: Library },
  { id: 'import', href: '#intake', label: 'Import intake', icon: Inbox },
  { id: 'settings', href: '#settings', label: 'Library settings', icon: SlidersHorizontal },
] as const satisfies readonly Destination[];

function currentDestination() {
  return destinations.find(item => item.href === window.location.hash) ?? destinations[0];
}

function NavigationItem({ destination: { href, label, icon: Icon }, current }: { destination: Destination; current: boolean }) {
  return (
    <a href={href} aria-current={current ? 'page' : undefined}
      className="group flex min-h-16 min-w-0 flex-col items-center justify-center gap-1 rounded-xl border border-transparent
        px-1 py-1.5 text-center text-[.72rem] leading-tight font-semibold text-muted no-underline transition-colors
        hover:bg-nav-hover hover:text-ink motion-reduce:transition-none
        aria-[current=page]:border-nav-line aria-[current=page]:bg-nav-active aria-[current=page]:text-ink
        desktop:min-h-12 desktop:flex-row desktop:justify-start desktop:gap-2.5 desktop:px-2 desktop:text-start desktop:text-sm">
      <span className="grid size-7 shrink-0 place-items-center rounded-lg text-nav-icon group-aria-[current=page]:bg-accent group-aria-[current=page]:text-accent-ink desktop:size-8">
        <Icon className="size-4.5" aria-hidden="true" />
      </span>
      <span>{label}</span>
    </a>
  );
}

export function Workspace({ serverId, signingOut, error, onSignOut, onSignedOut }: {
  serverId: string;
  signingOut: boolean;
  error: string;
  onSignOut: () => void;
  onSignedOut: () => void;
}) {
  const [view, setView] = useState(currentDestination);

  useEffect(() => {
    const handleNavigation = () => {
      setView(currentDestination());
      window.scrollTo(0, 0);
    };
    window.addEventListener('hashchange', handleNavigation);
    return () => window.removeEventListener('hashchange', handleNavigation);
  }, []);

  return (
    <div className="min-h-svh desktop:grid desktop:grid-cols-[15rem_minmax(0,1fr)]">
      <aside className="sticky top-0 z-10 grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-2
        border-b border-line bg-sidebar px-3 pt-[max(.625rem,env(safe-area-inset-top))] pb-2
        desktop:flex desktop:h-svh desktop:flex-col desktop:items-stretch desktop:gap-7 desktop:overflow-y-auto
        desktop:border-e desktop:border-b-0 desktop:p-4" aria-label="Manga Organizer">
        <a className="col-start-1 row-start-1 flex w-fit rounded-xl no-underline desktop:mx-1 desktop:mt-1" href="#library">
          <Brand />
        </a>
        <nav className="col-span-full row-start-2 grid grid-cols-3 gap-1.5 desktop:grid-cols-1 desktop:gap-1" aria-label="Library views">
          {destinations.map(item => (
            <NavigationItem key={item.id} destination={item} current={view.id === item.id} />
          ))}
        </nav>
        <Button variant="ghost" icon={LogOut} className="col-start-2 row-start-1 desktop:mt-auto desktop:justify-start"
          onClick={onSignOut} disabled={signingOut}>
          {signingOut ? 'Signing out…' : 'Sign out'}
        </Button>
      </aside>
      <main className="min-w-0 p-[clamp(1rem,3vw,3.25rem)] wrap-anywhere">
        {error && <p role="alert" className="error">{error}</p>}
        <Catalog view={view.id} title={view.label} onSignedOut={onSignedOut} />
        {view.id === 'settings' && (
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
