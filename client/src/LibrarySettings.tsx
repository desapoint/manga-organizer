import { Plus } from 'lucide-react';
import { useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import { FolderField } from './FolderField';
import { intakeTargetFor, ROOTS_URL } from './library';
import type { FolderKind, LibraryRoot } from './library';
import { Select } from './Select';
import type { SelectOption } from './Select';
import { Button, Modal, ScanBadge, SectionHeader } from './ui';

type FolderDraft = { id?: string; path: string; name: string; kind: FolderKind; managed: boolean };

const folderKinds = {
  library: 'Library only',
  import: 'Import intake only',
  both: 'Library and import intake',
} satisfies Record<FolderKind, string>;

const folderOptions: SelectOption<FolderKind>[] = [
  { value: 'library', label: folderKinds.library },
  { value: 'import', label: folderKinds.import },
  { value: 'both', label: folderKinds.both, description: 'One scan and inventory shared by both views.' },
];

export function LibrarySettings({ folders, onChange, onSignedOut }: {
  folders: LibraryRoot[];
  onChange: () => void;
  onSignedOut: () => void;
}) {
  const [draft, setDraft] = useState<FolderDraft | null>(null);
  const [remove, setRemove] = useState<LibraryRoot | null>(null);
  const [routing, setRouting] = useState<LibraryRoot | null>(null);
  const [targetRoot, setTargetRoot] = useState('');
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
      setRemove(null);
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

  async function saveTarget(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!routing) return;
    setBusy(true);
    setError('');
    try {
      const response = await fetch(`${ROOTS_URL}/${routing.id}/intake-target`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: JSON.stringify({ targetRoot: targetRoot || null }),
      });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        const details = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(details?.message ?? 'Could not save the intake target.');
      }
      setRouting(null);
      onChange();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save the intake target.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="surface grid gap-4 p-4 sm:p-5" aria-labelledby="library-folders-heading">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <SectionHeader id="library-folders-heading" title="Library folders" />
        <Button icon={Plus} disabled={busy} onClick={() => {
          setError('');
          setDraft({ path: '', name: '', kind: 'library', managed: false });
        }}>Add folder</Button>
      </div>
      {folders.length === 0 && <p className="m-0 text-sm text-muted">Add a server folder to start indexing your collection.</p>}
      {folders.length > 0 && (
        <ul className="settings-folders" role="list" aria-label="Configured folders">
          {folders.map(folder => {
            const target = intakeTargetFor(folder, folders);
            return (
            <li key={folder.id}>
              <div className="folder-description">
                <strong>{folder.name || folder.path}</strong>
                {folder.name && <code>{folder.path}</code>}
                <div className="action-row text-sm text-muted">{folderKinds[folder.kind]} · {folder.managed ? 'Managed' : 'Read-only'}
                  <ScanBadge state={folder.state} /></div>
                {folder.kind !== 'library' && <span className="text-sm text-muted">
                  {target ? `Target library · ${target.name || target.path}` : 'Choose a target library'}
                </span>}
              </div>
              <div className="action-row">
                {folder.kind !== 'library' && <Button variant="secondary" disabled={busy}
                  aria-label={`Set intake target for ${folder.name || folder.path}`} onClick={() => {
                    setError('');
                    setTargetRoot(target?.id ?? '');
                    setRouting(folder);
                  }}>Set intake target</Button>}
                <Button variant="secondary" disabled={busy} aria-label={`Edit ${folder.name || folder.path}`}
                  onClick={() => { setError(''); setDraft({ ...folder, name: folder.name ?? '' }); }}>Edit</Button>
                <Button variant="secondary" disabled={busy} aria-label={`Remove ${folder.name || folder.path}`}
                  onClick={() => { setError(''); setRemove(folder); }}>Remove</Button>
              </div>
            </li>
            );
          })}
        </ul>
      )}
      {draft && <Modal title={draft.id ? 'Edit folder' : 'Add folder'} wide onClose={() => { if (!busy) setDraft(null); }}>
        <form key={draft.id ?? 'new'} onSubmit={save}>
          <Select label="Use folder for" value={draft.kind} options={folderOptions} disabled={busy}
            onChange={kind => setDraft({ ...draft, kind })} />
          <label htmlFor="folder-name">Display name (optional)</label>
          <input id="folder-name" value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} />
          <FolderField id="settings-folder" label="Folder location" required value={draft.path}
            onChange={path => setDraft({ ...draft, path })} />
          <label className="flex min-h-11 items-center gap-2">
            <input type="checkbox" className="size-4 min-h-0 shrink-0 p-0 accent-accent" checked={draft.managed}
              onChange={event => setDraft({ ...draft, managed: event.target.checked })} />
            Allow reviewed file moves and renames
          </label>
          {error && <p role="alert" className="error">{error}</p>}
          <div className="action-row">
            <Button type="submit" disabled={busy}>{busy ? 'Saving…' : 'Save folder'}</Button>
            <Button variant="secondary" disabled={busy} onClick={() => setDraft(null)}>Cancel</Button>
          </div>
        </form>
      </Modal>}
      {remove && <Modal title="Remove folder from the library" onClose={() => { if (!busy) setRemove(null); }}>
        <p>The server stops indexing <code>{remove.path}</code>. Its files stay on disk.</p>
        {error && <p role="alert" className="error">{error}</p>}
        <div className="action-row">
          <Button variant="secondary" disabled={busy} onClick={() => void changeFolder('DELETE', remove.id)}>Remove folder</Button>
          <Button variant="ghost" disabled={busy} onClick={() => setRemove(null)}>Cancel</Button>
        </div>
      </Modal>}
      {routing && <Modal title="Set intake target" onClose={() => { if (!busy) setRouting(null); }}>
        <form onSubmit={saveTarget}>
          <p className="m-0 text-sm text-muted">{routing.name || routing.path}</p>
          <Select label="Target library" value={targetRoot} onChange={setTargetRoot} disabled={busy}
            options={[{ value: '', label: 'None' }, ...folders.filter(folder => folder.kind !== 'import').map(folder => ({
              value: folder.id, label: folder.name || folder.path,
              description: folder.id === routing.id ? 'Same folder · File confirmation publishes to Library' : undefined,
            }))]} />
          {error && <p role="alert" className="error">{error}</p>}
          <div className="action-row">
            <Button type="submit" disabled={busy}>{busy ? 'Saving…' : 'Save target'}</Button>
            <Button variant="secondary" disabled={busy} onClick={() => setRouting(null)}>Cancel</Button>
          </div>
        </form>
      </Modal>}
    </section>
  );
}
