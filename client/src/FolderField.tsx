import { ArrowUp, Check, ChevronRight, FolderClosed, FolderOpen, HardDrive, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Button, SearchField } from './ui';

type Folder = { name: string; path: string };
type Locations = {
  path: string | null;
  parent: string | null;
  selectable: boolean;
  folders: Folder[];
  suggestions: Folder[];
  breadcrumbs: Folder[];
};

type Props = {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  required?: boolean;
  setupToken?: string;
};

export function FolderField({ id, label, value, onChange, required, setupToken }: Props) {
  const [browsing, setBrowsing] = useState(false);
  const [path, setPath] = useState<string | null>(null);
  const [visits, setVisits] = useState(() => new Map<string, Locations>());
  const [filter, setFilter] = useState('');
  const [error, setError] = useState('');
  const cacheKey = JSON.stringify([setupToken, path]);
  const locations = visits.get(cacheKey);
  const search = filter.trim().toLocaleLowerCase();
  const folders = locations?.folders.filter(folder => folder.name.toLocaleLowerCase().includes(search)) ?? [];

  function openLocation(next: string | null) {
    setError('');
    setFilter('');
    setPath(next);
  }

  useEffect(() => {
    if (!browsing) return;
    const request = new AbortController();
    const endpoint = setupToken ? '/api/setup/folders' : '/api/library/folders';
    const query = path ? `?${new URLSearchParams({ path })}` : '';

    async function load() {
      const response = await fetch(endpoint + query, {
        signal: request.signal,
        headers: setupToken ? { 'X-Setup-Token': setupToken } : undefined,
      });
      if (!response.ok) {
        const details = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(details?.message ?? 'Could not browse folders on the server.');
      }
      const listing = await response.json() as Locations;
      if (request.signal.aborted) return;
      setVisits(previous => {
        const next = new Map(previous);
        next.delete(cacheKey);
        next.set(cacheKey, listing);
        if (next.size > 24) next.delete(next.keys().next().value!);
        return next;
      });
    }

    void load().catch(error => {
      if (request.signal.aborted) return;
      setVisits(previous => {
        const next = new Map(previous);
        next.delete(cacheKey);
        return next;
      });
      setError(error instanceof Error ? error.message : 'Could not browse folders.');
    });
    return () => request.abort();
  }, [browsing, cacheKey, path, setupToken]);

  function folderLinks(items: Folder[]) {
    return (
      <ul className="folder-links" role="list">
        {items.map(folder => (
          <li key={folder.path}>
            <Button variant="secondary" align="start" icon={FolderClosed} className="w-full text-start wrap-anywhere" title={folder.path}
              onClick={() => openLocation(folder.path)}>{folder.name}</Button>
          </li>
        ))}
      </ul>
    );
  }

  return (
    <>
      <label htmlFor={id}>{label}</label>
      <div className="folder-field">
        <input id={id} name="libraryPath" value={value} onChange={event => onChange(event.target.value)}
          autoComplete="off" spellCheck={false} required={required} aria-describedby={`${id}-help`} />
        <Button variant="secondary" icon={browsing ? X : FolderOpen} aria-expanded={browsing} onClick={() => {
          if (!browsing) openLocation(value.trim() || null);
          setBrowsing(!browsing);
        }}>{browsing ? 'Close browser' : 'Browse server'}</Button>
      </div>
      <span className="field-help" id={`${id}-help`}>Choose a folder on the server or enter its full path.</span>
      {browsing && (
        <section className="folder-browser" aria-label="Server folders">
          <div className="action-row">
            <Button variant="secondary" icon={HardDrive} disabled={path === null} onClick={() => openLocation(null)}>Disks</Button>
            {locations?.path && <Button variant="secondary" icon={ArrowUp} onClick={() => openLocation(locations.parent)}>Up</Button>}
            {value.trim() && value.trim() !== path && (
              <Button variant="secondary" onClick={() => openLocation(value.trim())}>Open entered path</Button>
            )}
            {locations?.path && <Button className="ms-auto" icon={Check} disabled={!locations.selectable} onClick={() => {
              if (locations.path) onChange(locations.path);
              setBrowsing(false);
            }}>Use this folder</Button>}
          </div>
          {locations?.path && (
            <nav className="folder-breadcrumbs" aria-label="Folder path">
              <ol>
                {locations.breadcrumbs.map((folder, index) => (
                  <li key={folder.path}>
                    {index > 0 && <ChevronRight className="mx-0.5 size-3 shrink-0 text-muted" aria-hidden="true" />}
                    {index === locations.breadcrumbs.length - 1
                      ? <span className="px-1.5 py-1" aria-current="location" title={folder.path}>{folder.name}</span>
                      : <Button variant="ghost" size="compact" className="text-start" title={folder.path}
                        onClick={() => openLocation(folder.path)}>{folder.name}</Button>}
                  </li>
                ))}
              </ol>
              <code className="folder-location">{locations.path}</code>
            </nav>
          )}
          {error && <p role="alert" className="error">{error}</p>}
          {!locations && !error && <p role="status">Loading server folders…</p>}
          {!path && !!locations?.suggestions.length && (
            <>
              <h3>Suggested locations</h3>
              {folderLinks(locations.suggestions)}
            </>
          )}
          {locations && (
            <>
              <h3>{path ? 'Subfolders' : 'Disks and mounts'}</h3>
              {locations.folders.length > 1 && (
                <SearchField className="mb-3" aria-label="Filter folders" placeholder="Filter folders"
                  value={filter} onChange={event => setFilter(event.target.value)} />
              )}
              {folders.length ? folderLinks(folders) : <p>{search ? 'No folders match this filter.' : 'No folders found here.'}</p>}
            </>
          )}
        </section>
      )}
    </>
  );
}
