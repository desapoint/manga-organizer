import { ArrowRightLeft, History } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import { formatSize } from './library';
import type { LibraryRoot } from './library';
import { Select } from './Select';
import { Button, SectionHeader } from './ui';

const url = '/api/file-operations';
type Operation = { id: string; state: 'planned' | 'applying' | 'completed' | 'restoring' | 'restored' | 'review';
  source: string; target: string; size: number; sha256: string; recovery: string | null; message: string | null;
  covers: { source: string; target: string; published: boolean }[] };

const stateLabel: Record<Operation['state'], string> = {
  planned: 'Previewed', applying: 'Applying', completed: 'Completed', restoring: 'Restoring', restored: 'Restored', review: 'Needs review',
};

async function post(path: string, onSignedOut: () => void, body?: object) {
  const response = await fetch(url + path, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
    body: body ? JSON.stringify(body) : undefined });
  if (response.status === 401) {
    onSignedOut();
    return null;
  }
  if (!response.ok) {
    const details = await response.json().catch(() => null) as { message?: string } | null;
    throw new Error(details?.message ?? 'Could not finish the file operation. Check its history before retrying.');
  }
  return await response.json() as Operation;
}

function OperationDetails({ operation }: { operation: Operation }) {
  return (
    <dl className="m-0 grid gap-2 text-sm">
      <div><dt className="text-muted">Original path</dt><dd><code>{operation.source}</code></dd></div>
      <div><dt className="text-muted">Destination</dt><dd><code>{operation.target}</code></dd></div>
      <div><dt className="text-muted">Verified identity · {formatSize(operation.size)}</dt><dd><code>{operation.sha256}</code></dd></div>
      {operation.covers.length > 0 && <div>
        <dt className="text-muted">Folder covers</dt>
        <dd><ul className="m-0 grid gap-2 pl-4">
          {operation.covers.map(cover => <li key={cover.target}>
            <code>{cover.source}</code><br />{cover.published ? 'Copied to ' : 'Copy to '}<code>{cover.target}</code>
          </li>)}
        </ul></dd>
      </div>}
      {operation.recovery && <div><dt className="text-muted">Recovery copy</dt><dd><code>{operation.recovery}</code></dd></div>}
      {operation.message && <div><dt className="text-muted">Result</dt><dd>{operation.message}</dd></div>}
    </dl>
  );
}

export function FileMove({ rootId, fileId, relativePath, folders, onSignedOut, onApplied }: {
  rootId: string; fileId: string; relativePath: string; folders: LibraryRoot[]; onSignedOut: () => void; onApplied: () => void;
}) {
  const [targetRoot, setTargetRoot] = useState(rootId);
  const [targetPath, setTargetPath] = useState(relativePath);
  const [keepBoth, setKeepBoth] = useState(false);
  const [copyFolderCovers, setCopyFolderCovers] = useState(false);
  const [plan, setPlan] = useState<Operation | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  async function preview(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError('');
    setBusy(true);
    try {
      setPlan(await post('/preview', onSignedOut, { sourceRoot: rootId, fileId, targetRoot, targetPath, keepBoth, copyFolderCovers }));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not preview this change.');
    } finally {
      setBusy(false);
    }
  }

  async function apply() {
    if (!plan) return;
    setBusy(true);
    setError('');
    try {
      if (await post('/' + plan.id + '/apply', onSignedOut)) onApplied();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Check the operation history before retrying.');
      setPlan(null);
    } finally {
      setBusy(false);
    }
  }

  const options = folders.map(folder => ({ value: folder.id, label: folder.name || folder.path, description: folder.name ? folder.path : undefined }));
  return (
    <div className="grid max-w-3xl gap-3 pb-2">
      {plan ? <>
        <OperationDetails operation={plan} />
        <p className="m-0 text-sm text-muted">The file is rechecked before moving. A recovery copy is retained.</p>
        {plan.covers.length > 0 && <p className="m-0 text-sm text-muted">Folder covers are copied and kept at both locations.</p>}
        <div className="action-row">
          <Button icon={ArrowRightLeft} disabled={busy} onClick={() => void apply()}>{busy ? 'Moving file…' : 'Confirm move or rename'}</Button>
          <Button variant="secondary" disabled={busy} onClick={() => setPlan(null)}>Back to path</Button>
        </div>
      </> : <form onSubmit={preview}>
        <Select label="Destination folder" value={targetRoot} options={options} onChange={setTargetRoot} disabled={busy} />
        <label className="grid gap-1">Destination path<input value={targetPath} onChange={event => setTargetPath(event.target.value)}
          maxLength={4096} required /><span className="field-help">Enter a path inside that folder, including the file extension.</span></label>
        <label className="flex items-start gap-2"><input type="checkbox" checked={keepBoth}
          onChange={event => setKeepBoth(event.target.checked)} /> Keep both when the destination exists</label>
        <label className="flex items-start gap-2"><input type="checkbox" checked={copyFolderCovers}
          onChange={event => setCopyFolderCovers(event.target.checked)} /> Copy folder covers</label>
        <Button type="submit" variant="secondary" disabled={busy} className="justify-self-start">Preview change</Button>
      </form>}
      {error && <p role="alert" className="m-0 text-sm text-danger">{error}</p>}
    </div>
  );
}

export function FileOperationHistory({ onSignedOut, onChanged }: { onSignedOut: () => void; onChanged: () => void }) {
  const [history, setHistory] = useState<Operation[] | null>(null);
  const [refresh, setRefresh] = useState(0);
  const [busy, setBusy] = useState<string | null>(null);
  const [confirmation, setConfirmation] = useState<string | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    const request = new AbortController();
    async function load() {
      const response = await fetch(url, { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const data = await response.json() as Operation[];
      if (!request.signal.aborted) setHistory(data);
    }
    void load().catch(() => { if (!request.signal.aborted) setError('Could not load file operation history.'); });
    return () => request.abort();
  }, [refresh, onSignedOut]);

  async function restore(id: string) {
    setBusy(id);
    setError('');
    try {
      if (await post('/' + id + '/restore', onSignedOut)) {
        setConfirmation(null);
        onChanged();
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not restore the original file.');
    } finally {
      setBusy(null);
      setRefresh(value => value + 1);
    }
  }

  return (
    <section className="surface grid gap-3 p-4 sm:p-5" aria-labelledby="file-operations-heading">
      <SectionHeader id="file-operations-heading" title="File operation history" />
      {!history && !error && <p role="status" className="m-0 text-muted">Loading file operations…</p>}
      {history?.length === 0 && <p className="m-0 text-sm text-muted">Reviewed file changes appear here.</p>}
      {error && <p role="alert" className="m-0 text-sm text-danger">{error}</p>}
      {history?.map(operation => <details className="rounded-lg border border-line bg-inset px-3" key={operation.id}>
        <summary className="min-h-11 cursor-pointer py-2.5 font-medium wrap-anywhere">{stateLabel[operation.state]} · {operation.target}</summary>
        <div className="grid gap-3 pb-3">
          <OperationDetails operation={operation} />
          {(operation.state === 'completed' || operation.state === 'review') && (confirmation === operation.id ? <div className="action-row">
            <Button icon={History} disabled={busy !== null} onClick={() => void restore(operation.id)}>
              {busy ? 'Restoring…' : 'Confirm restoration'}
            </Button>
            <Button variant="secondary" disabled={busy !== null} onClick={() => setConfirmation(null)}>Cancel</Button>
          </div> : <Button variant="secondary" icon={History} className="justify-self-start" onClick={() => setConfirmation(operation.id)}>
            Restore original path
          </Button>)}
        </div>
      </details>)}
    </section>
  );
}
