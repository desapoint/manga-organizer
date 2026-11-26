import { Plus } from 'lucide-react';
import { useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import { FolderField } from './FolderField';
import { Button } from './ui';

export type FolderKind = 'library' | 'import';
export type ConfiguredFolder = { id: string; path: string; name: string | null; kind: FolderKind };

type FolderDraft = { id?: string; path: string; name: string; kind: FolderKind };

export const ROOTS_URL = '/api/library/roots';

export function LibrarySettings({ folders, onChange, onSignedOut }: {
  folders: ConfiguredFolder[];
  onChange: () => void;
  onSignedOut: () => void;
}) {
  const [draft, setDraft] = useState<FolderDraft | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  async function changeFolder(method: 'POST' | 'PUT' | 'DELETE', id?: string, body?: FolderDraft) {
    setBusy(true);
    setError('');
    try {
      const response = await fetch(id ? `${ROOTS_URL}/${id}` : ROOTS_URL, {
        method,
        headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: body && JSON.stringify(body),
      });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        const details = await response.json().catch(() => null) as { message?: string } | null;
        setError(details?.message ?? 'Could not update the folder. Try again.');
        return;
      }
      setDraft(null);
      onChange();
    } catch {
      setError('Could not update the folder. Try again.');
    } finally {
      setBusy(false);
    }
  }

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (draft) void changeFolder(draft.id ? 'PUT' : 'POST', draft.id, draft);
  }

  return (
    <div className="library-settings">
      {folders.length > 0 && (
        <ul className="settings-folders" aria-label="Configured folders">
          {folders.map(folder => (
            <li key={folder.id}>
              <div className="folder-description">
                <strong>{folder.name || folder.path}</strong>
                {folder.name && <code>{folder.path}</code>}
                <span>{folder.kind === 'import' ? 'Import folder' : 'Read-only library'}</span>
              </div>
              <div className="action-row">
                <Button variant="secondary" disabled={busy} aria-label={`Edit ${folder.name || folder.path}`}
                  onClick={() => { setError(''); setDraft({ ...folder, name: folder.name ?? '' }); }}>Edit</Button>
                <Button variant="secondary" disabled={busy} aria-label={`Remove ${folder.name || folder.path}`}
                  onClick={() => void changeFolder('DELETE', folder.id)}>Remove</Button>
              </div>
            </li>
          ))}
        </ul>
      )}
      {error && <p role="alert" className="error">{error}</p>}
      {!draft && <Button icon={Plus} disabled={busy} onClick={() => {
        setError('');
        setDraft({ path: '', name: '', kind: 'library' });
      }}>Add folder</Button>}
      {draft && (
        <form className="settings-form" key={draft.id ?? 'new'} onSubmit={save}>
          <h2>{draft.id ? 'Edit folder' : 'Add folder'}</h2>
          <label htmlFor="folder-kind">Use folder for</label>
          <select id="folder-kind" value={draft.kind} onChange={event => setDraft({ ...draft, kind: event.target.value as FolderKind })}>
            <option value="library">Read-only library</option>
            <option value="import">Import intake</option>
          </select>
          <label htmlFor="folder-name">Display name (optional)</label>
          <input id="folder-name" value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} />
          <FolderField id="settings-folder" label="Folder location" required value={draft.path}
            onChange={path => setDraft({ ...draft, path })} />
          <div className="action-row">
            <Button type="submit" disabled={busy}>{busy ? 'Saving…' : 'Save folder'}</Button>
            <Button variant="secondary" disabled={busy} onClick={() => setDraft(null)}>Cancel</Button>
          </div>
        </form>
      )}
    </div>
  );
}
