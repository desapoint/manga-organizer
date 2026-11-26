import { Ellipsis, Inbox, LayoutGrid, Library, LogOut, PanelLeftClose, PanelLeftOpen, SlidersHorizontal } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import type { Appearance } from './appearance';
import { Catalog } from './Catalog';
import type { CatalogView } from './Catalog';
import { Reader } from './Reader';
import { Brand, Button, SectionHeader, SegmentedControl } from './ui';

type Destination = { id: CatalogView; href: string; label: string; title: string; section: string; icon: LucideIcon };

const destinations = [
  { id: 'overview', href: '#overview', label: 'Overview', title: 'Your next chapter awaits.', section: 'Library', icon: LayoutGrid },
  { id: 'library', href: '#library', label: 'Library', title: 'Library catalog', section: 'Library', icon: Library },
  { id: 'import', href: '#intake', label: 'Import intake', title: 'Import intake', section: 'Management', icon: Inbox },
  { id: 'settings', href: '#settings', label: 'Settings', title: 'Library settings', section: 'System', icon: SlidersHorizontal },
] as const satisfies readonly Destination[];

const sidebarSections = ['Library', 'Management', 'System'] as const;
const settings = destinations[3];

function currentDestination(hash: string) {
  return destinations.find(item => item.href === hash) ?? destinations[0];
}

function NavigationItem({ destination: { href, label, icon: Icon }, current, mobile, compact }: {
  destination: Destination; current: boolean; mobile?: boolean; compact?: boolean;
}) {
  const layout = mobile
    ? 'min-h-14 flex-col justify-center gap-1 px-1 py-1 text-center text-[.72rem]'
    : 'min-h-11 gap-2.5 px-2 py-1.5 text-sm';
  return (
    <a href={href} aria-current={current ? 'page' : undefined} title={compact ? label : undefined}
      className={`group flex min-w-0 items-center rounded-xl border border-transparent font-semibold leading-tight text-muted no-underline
        transition-colors duration-200 hover:bg-nav-hover hover:text-ink motion-reduce:transition-none
        aria-[current=page]:border-nav-line aria-[current=page]:bg-nav-active aria-[current=page]:text-ink ${layout}`}>
      <span className="grid size-7 shrink-0 place-items-center rounded-lg text-nav-icon group-aria-[current=page]:bg-accent group-aria-[current=page]:text-accent-ink">
        <Icon className="size-4" aria-hidden="true" />
      </span>
      <span className={compact ? 'sr-only' : undefined}>{label}</span>
    </a>
  );
}

function MobileNavigation({ view, signingOut, onSignOut }: { view: CatalogView; signingOut: boolean; onSignOut: () => void }) {
  const overflow = useRef<HTMLDetailsElement>(null);

  function closeOverflow() {
    if (overflow.current) overflow.current.open = false;
  }

  useEffect(() => {
    closeOverflow();
    window.addEventListener('hashchange', closeOverflow);
    return () => window.removeEventListener('hashchange', closeOverflow);
  }, []);

  return (
    <nav aria-label="Library views" className="fixed inset-x-0 bottom-0 z-20 grid grid-cols-4 gap-1 border-t border-line bg-sidebar
      px-[max(.5rem,env(safe-area-inset-left))] pt-1.5 pb-[max(.5rem,env(safe-area-inset-bottom))] desktop:hidden">
      {destinations.filter(item => item.id !== 'settings').map(item => (
        <NavigationItem key={item.id} destination={item} current={view === item.id} mobile />
      ))}
      <details ref={overflow} className="group relative" onKeyDown={event => {
        if (event.key === 'Escape' && overflow.current?.open) {
          closeOverflow();
          overflow.current.querySelector('summary')?.focus();
        }
      }}>
        <summary data-current={view === 'settings'}
          className="flex min-h-14 cursor-pointer list-none flex-col items-center justify-center gap-1 rounded-xl border border-transparent
            px-1 py-1 text-center text-[.72rem] leading-tight font-semibold text-muted hover:bg-nav-hover
            data-[current=true]:border-nav-line data-[current=true]:bg-nav-active data-[current=true]:text-ink [&::-webkit-details-marker]:hidden">
          <span className="grid size-7 place-items-center rounded-lg text-nav-icon"><Ellipsis className="size-4" aria-hidden="true" /></span>
          More
        </summary>
        <div className="absolute end-0 bottom-full mb-2 grid w-48 max-w-[calc(100vw-1rem)] gap-1 rounded-xl border border-line
          bg-elevated p-2 shadow-lg">
          <div onClick={closeOverflow}><NavigationItem destination={settings} current={view === 'settings'} /></div>
          <Button variant="ghost" align="start" icon={LogOut} onClick={onSignOut} disabled={signingOut}>
            {signingOut ? 'Signing out…' : 'Sign out'}
          </Button>
        </div>
      </details>
    </nav>
  );
}

function SettingsDetails({ appearance, onAppearanceChange, serverId }: {
  appearance: Appearance; onAppearanceChange: (appearance: Appearance) => void; serverId: string;
}) {
  return (
    <>
      <section className="surface grid gap-4 p-4 sm:p-5" aria-labelledby="appearance-heading">
        <SectionHeader id="appearance-heading" title="Appearance" />
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span>Theme</span>
          <SegmentedControl label="Theme" value={appearance.theme}
            options={[{ value: 'ink', label: 'Ink' }, { value: 'paper', label: 'Paper' }]}
            onChange={theme => onAppearanceChange({ ...appearance, theme })} />
        </div>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span>Accent</span>
          <SegmentedControl label="Accent" value={appearance.accent}
            options={[{ value: 'violet', label: 'Violet' }, { value: 'mint', label: 'Mint' }, { value: 'coral', label: 'Coral' }]}
            onChange={accent => onAppearanceChange({ ...appearance, accent })} />
        </div>
      </section>
      <section className="surface grid gap-3 p-4 sm:p-5" aria-labelledby="server-details">
        <SectionHeader id="server-details" title="Primary server" />
        <dl className="m-0">
          <dt className="text-sm">Server ID</dt>
          <dd><code>{serverId}</code></dd>
        </dl>
      </section>
    </>
  );
}

export function Workspace({ serverId, signingOut, error, onSignOut, onSignedOut, appearance, onAppearanceChange }: {
  serverId: string;
  signingOut: boolean;
  error: string;
  onSignOut: () => void;
  onSignedOut: () => void;
  appearance: Appearance;
  onAppearanceChange: (appearance: Appearance) => void;
}) {
  const [hash, setHash] = useState(() => window.location.hash);
  const [compact, setCompact] = useState(false);
  const view = currentDestination(hash);

  useEffect(() => {
    const handleNavigation = () => {
      setHash(window.location.hash);
      window.scrollTo(0, 0);
    };
    window.addEventListener('hashchange', handleNavigation);
    return () => window.removeEventListener('hashchange', handleNavigation);
  }, []);

  const reader = /^#read\/([a-f0-9]{32})(\/intake)?$/i.exec(hash);
  if (reader?.[1]) {
    return <Reader key={reader[1]} contentId={reader[1]} backToIntake={!!reader[2]} onSignedOut={onSignedOut} />;
  }

  return (
    <div className={`min-h-svh desktop:grid ${compact ? 'desktop:grid-cols-[4.5rem_minmax(0,1fr)]' : 'desktop:grid-cols-[14.5rem_minmax(0,1fr)]'}`}>
      <header className="border-b border-line bg-sidebar px-4 pt-[max(.75rem,env(safe-area-inset-top))] pb-3 desktop:hidden">
        <a href="#overview" className="inline-flex rounded-xl no-underline"><Brand /></a>
      </header>
      <aside aria-label="Manga Organizer" className="sticky top-0 hidden h-svh flex-col gap-5 overflow-y-auto border-e border-line bg-sidebar
        px-3.5 pt-5 pb-4 desktop:flex">
        <a href="#overview" className="flex min-h-11 items-center rounded-xl no-underline"><Brand compact={compact} /></a>
        <nav id="sidebar-navigation" className="grid gap-5" aria-label="Library views">
          {sidebarSections.map(section => (
            <div key={section} className="grid gap-1.5">
              {!compact && <span className="px-2 text-[.65rem] font-semibold tracking-[.16em] text-muted uppercase">{section}</span>}
              {destinations.filter(item => item.section === section).map(item => (
                <NavigationItem key={item.id} destination={item} current={view.id === item.id} compact={compact} />
              ))}
            </div>
          ))}
        </nav>
        <div className="mt-auto grid gap-1 border-t border-line pt-3">
          <Button variant="ghost" align={compact ? 'center' : 'start'} icon={compact ? PanelLeftOpen : PanelLeftClose}
            aria-expanded={!compact} aria-controls="sidebar-navigation" aria-label={compact ? 'Expand sidebar' : 'Collapse sidebar'}
            title={compact ? 'Expand sidebar' : undefined} size={compact ? 'compact' : 'regular'} onClick={() => setCompact(value => !value)}>
            {!compact && 'Collapse sidebar'}
          </Button>
          <Button variant="ghost" align={compact ? 'center' : 'start'} icon={LogOut} onClick={onSignOut} disabled={signingOut}
            aria-label={compact ? 'Sign out' : undefined} title={compact ? 'Sign out' : undefined} size={compact ? 'compact' : 'regular'}>
            {!compact && (signingOut ? 'Signing out…' : 'Sign out')}
          </Button>
        </div>
      </aside>
      <main className="min-w-0 px-[clamp(1rem,3vw,3rem)] pt-[clamp(1.5rem,4vw,3rem)]
        pb-[calc(6rem+env(safe-area-inset-bottom))] wrap-anywhere desktop:pb-10">
        {error && <p role="alert" className="error">{error}</p>}
        <Catalog view={view.id} title={view.title} section={view.section} onSignedOut={onSignedOut}>
          {view.id === 'settings' && <SettingsDetails appearance={appearance} onAppearanceChange={onAppearanceChange} serverId={serverId} />}
        </Catalog>
      </main>
      <MobileNavigation view={view.id} signingOut={signingOut} onSignOut={onSignOut} />
    </div>
  );
}
