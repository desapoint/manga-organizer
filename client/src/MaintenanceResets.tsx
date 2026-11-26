import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import type { LibraryRoot } from './library';
import { Select } from './Select';
import { Button, Modal, SectionHeader } from './ui';

const endpoint = '/api/maintenance/resets';
const levels = [
  { value: 'catalog', label: 'Catalog inventory' },
  { value: 'progress', label: 'Reading progress' },
  { value: 'configuration', label: 'Folder configuration' },
] as const;
type Level = typeof levels[number]['value'];
type Count = { label: string; count: number };
type Preview = { level: Level; rootIds: string[]; affected: Count[]; token: string; confirmation: string };
type Result = { id: string; level: Level; affected: Count[]; backupDirectory: string; completedAt: string };

const descriptions = {
  catalog: 'Clear the selected folders’ indexed files and series links, then rescan to rediscover their files. Saved metadata, release tags and reading progress are retained.',
  progress: 'Clear saved reading positions and read chapter status for your account. Your library and preferred releases are retained.',
  configuration: 'Clear intake targets and display names for the selected folders. Reset their role to Library and access to read-only. Re-enable managed access to restore files through the history if needed.',
};

export function MaintenanceResets({ roots, onSignedOut, onChanged }: {
  roots: LibraryRoot[] | null; onSignedOut: () => void; onChanged: () => void;
}) {
  const [level, setLevel] = useState<Level>('catalog');
  const [selected, setSelected] = useState<string[]>([]);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [confirmation, setConfirmation] = useState('');
  const [recent, setRecent] = useState<Result[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [completed, setCompleted] = useState<Result | null>(null);
  const scope = roots?.filter(folder => selected.includes(folder.id)) ?? [];

  useEffect(() => {
    const controller = new AbortController();
    void fetch(endpoint, { signal: controller.signal }).then(async response => {
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const history = await response.json() as Result[];
      if (!controller.signal.aborted) setRecent(history);
    }).catch(() => { if (!controller.signal.aborted) setError('Could not load reset history.'); });
    return () => controller.abort();
  }, [onSignedOut]);

  async function post<T>(path: string, body: object) {
    const response = await fetch(endpoint + path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
      body: JSON.stringify(body),
    });
    if (response.status === 401) {
      onSignedOut();
      return null;
    }
    if (!response.ok) {
      const details = await response.json().catch(() => null) as { message?: string } | null;
      throw new Error(details?.message ?? 'Could not finish this reset.');
    }
    return await response.json() as T;
  }

  async function review() {
    setError('');
    setCompleted(null);
    setBusy(true);
    try {
      const result = await post<Preview>('/preview', { level, rootIds: level === 'progress' ? [] : scope.map(folder => folder.id) });
      if (result) {
        setConfirmation('');
        setPreview(result);
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not preview the reset.');
    } finally {
      setBusy(false);
    }
  }

  async function apply(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!preview || confirmation !== preview.confirmation) return;
    setError('');
    setBusy(true);
    try {
      const result = await post<Result>('/apply', { token: preview.token, confirmation });
      if (result) {
        setCompleted(result);
        setRecent(current => [result, ...current].slice(0, 20));
        setPreview(null);
        onChanged();
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not apply the reset.');
    } finally {
      setBusy(false);
    }
  }

  function close() {
    if (busy) return;
    setPreview(null);
    setError('');
  }

  return <section className="surface grid gap-3 p-4" aria-labelledby="maintenance-resets">
    <SectionHeader id="maintenance-resets" title="Data resets" />
    <Select label="Reset level" value={level} options={levels} disabled={busy} onChange={setLevel} />
    <p className="m-0 text-sm text-muted">{descriptions[level]}</p>
    {level !== 'progress' && <fieldset className="grid gap-1">
      <legend>Folders to reset</legend>
      {roots?.map(folder => <label key={folder.id} className="flex min-h-11 items-center gap-2 wrap-anywhere">
        <input type="checkbox" checked={selected.includes(folder.id)} disabled={busy} onChange={event =>
          setSelected(current => event.target.checked ? [...current, folder.id] : current.filter(id => id !== folder.id))} />
        {folder.name || folder.path}
      </label>)}
    </fieldset>}
    {error && !preview && <p role="alert" className="m-0 text-danger">{error}</p>}
    {completed && <p role="status" className="m-0 text-positive">Reset completed. Verified backup in <code>maintenance-backups/{completed.backupDirectory}</code>.</p>}
    <Button variant="secondary" className="justify-self-start" disabled={busy || level !== 'progress' && !scope.length}
      onClick={() => void review()}>Preview reset</Button>
    {!!recent.length && <details>
      <summary className="min-h-11 cursor-pointer py-2 font-semibold">Recent resets</summary>
      <ul className="m-0 grid gap-2">
        {recent.map(item => <li key={item.id} className="text-sm">
          {levels.find(option => option.value === item.level)?.label} · {new Date(item.completedAt).toLocaleString()}
          <code className="block wrap-anywhere">maintenance-backups/{item.backupDirectory}</code>
        </li>)}
      </ul>
    </details>}
    {preview && <Modal title="Review data reset" onClose={close}>
      <form onSubmit={apply}>
        <p className="m-0 text-sm">{descriptions[preview.level]}</p>
        {preview.rootIds.length > 0 && <p className="m-0 text-sm text-muted">Folders · {preview.rootIds.map(id => {
          const folder = roots?.find(item => item.id === id);
          return folder?.name || folder?.path || id;
        }).join(' · ')}</p>}
        <dl className="m-0 grid gap-2">
          {preview.affected.map(item => <div key={item.label} className="flex items-baseline justify-between gap-3">
            <dt>{item.label}</dt><dd className="m-0 font-semibold">{item.count.toLocaleString()}</dd>
          </div>)}
        </dl>
        <p className="m-0 text-sm text-muted">The server verifies a backup before changing these records. Media and recovery files stay in their existing locations.</p>
        <label className="grid gap-1">Type <code>{preview.confirmation}</code> to confirm
          <input value={confirmation} onChange={event => setConfirmation(event.target.value)} disabled={busy} autoComplete="off" />
        </label>
        {error && <p role="alert" className="m-0 text-danger">{error}</p>}
        <div className="action-row">
          <Button type="submit" disabled={busy || confirmation !== preview.confirmation || !preview.affected.some(item => item.count > 0)}>
            {busy ? 'Verifying backup and applying…' : 'Apply reset'}</Button>
          <Button variant="secondary" onClick={close} disabled={busy}>Cancel</Button>
        </div>
      </form>
    </Modal>}
  </section>;
}
