import { useEffect, useState } from 'react';

type Folder = { name: string; path: string };
type Locations = { path: string | null; parent: string | null; selectable: boolean; folders: Folder[]; suggestions: Folder[] };

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
  const [locations, setLocations] = useState<Locations | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!browsing) return;
    const request = new AbortController();
    const endpoint = setupToken ? '/api/setup/folders' : '/api/library/folders';
    const query = path ? `?${new URLSearchParams({ path })}` : '';
    setLocations(null);
    setError('');

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
      if (!request.signal.aborted) setLocations(listing);
    }

    void load().catch(error => {
      if (!request.signal.aborted) setError(error instanceof Error ? error.message : 'Could not browse folders.');
    });
    return () => request.abort();
  }, [browsing, path, setupToken]);

  function folderLinks(items: Folder[]) {
    return (
      <ul className="folder-links">
        {items.map(folder => (
          <li key={folder.path}>
            <button type="button" className="secondary" title={folder.path} onClick={() => setPath(folder.path)}>{folder.name}</button>
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
        <button type="button" className="secondary" aria-expanded={browsing} onClick={() => {
          if (!browsing) setPath(null);
          setBrowsing(!browsing);
        }}>{browsing ? 'Close browser' : 'Browse server'}</button>
      </div>
      <span className="field-help" id={`${id}-help`}>Choose a folder on the server or enter its full path.</span>
      {browsing && (
        <section className="folder-browser" aria-label="Server folders">
          <div className="folder-browser-controls">
            <button type="button" className="secondary" disabled={path === null} onClick={() => setPath(null)}>Disks</button>
            {path && <button type="button" className="secondary" onClick={() => setPath(locations?.parent ?? null)}>Up</button>}
            {!path && value && <button type="button" className="secondary" onClick={() => setPath(value)}>Open entered path</button>}
          </div>
          {locations?.path && <code className="folder-location">{locations.path}</code>}
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
              {locations.folders.length ? folderLinks(locations.folders) : <p>No folders found here.</p>}
            </>
          )}
          {locations?.path && <button type="button" disabled={!locations.selectable} onClick={() => {
            if (locations.path) onChange(locations.path);
            setBrowsing(false);
          }}>Use this folder</button>}
        </section>
      )}
    </>
  );
}
