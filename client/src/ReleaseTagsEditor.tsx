import { Check, Minus, Tags } from 'lucide-react';
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import { Select } from './Select';
import { Button, Modal } from './ui';

export const colorOptions = [
  { value: 'unknown', label: 'Unknown' }, { value: 'black-and-white', label: 'Black and white' },
  { value: 'full-color', label: 'Full color' }, { value: 'partial', label: 'Partial color' },
] as const;
export const censorshipOptions = [
  { value: 'unknown', label: 'Unknown' }, { value: 'uncensored', label: 'Uncensored' },
  { value: 'censored', label: 'Censored' }, { value: 'mixed', label: 'Mixed' },
] as const;

export type ReleaseTags = {
  language: string | null; color: typeof colorOptions[number]['value']; censorship: typeof censorshipOptions[number]['value'];
  edition: string | null; group: string | null; revision: number;
};
type Release = { contentId: string; relativePath: string; tags: ReleaseTags };
type EditorProps = { rootId: string; fileId: string; onSignedOut: () => void; onSaved?: () => void };

const languagePattern = '[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*';
const releaseFields = [
  { value: 'language', label: 'Language code' }, { value: 'color', label: 'Color' },
  { value: 'censorship', label: 'Censorship' }, { value: 'edition', label: 'Edition' },
  { value: 'group', label: 'Release group' },
] as const;
type ReleaseField = typeof releaseFields[number]['value'];
type SelectedRelease = Release & { fileId: string };

const maxBatchFiles = 5000;

export function useReleaseSelection(scope: string, eligibleIds: string[], persistent = false) {
  const [stored, setStored] = useState<{ scope: string; ids: Set<string> } | null>(null);
  if (stored && stored.scope !== scope) setStored(null);
  const enabled = persistent || stored?.scope === scope;
  const eligible = new Set(eligibleIds);
  const selected = new Set(stored?.scope === scope ? [...stored.ids].filter(id => eligible.has(id)) : []);

  function update(change: (ids: Set<string>) => void) {
    setStored(previous => {
      const ids = new Set(previous?.scope === scope ? previous.ids : []);
      change(ids);
      return { scope, ids };
    });
  }

  return {
    enabled, selected,
    start: () => setStored({ scope, ids: new Set() }),
    stop: () => setStored(null),
    select: (ids: string[]) => update(current => ids.forEach(id => current.add(id))),
    remove: (id: string) => { if (selected.has(id)) update(current => current.delete(id)); },
    toggle: (ids: string[]) => update(current => {
      if (ids.every(id => current.has(id))) ids.forEach(id => current.delete(id));
      else ids.forEach(id => current.add(id));
    }),
  };
}

export function ReleaseSelectionActions({ selection, pageIds, rootId, onSignedOut, onSaved, persistent = false }: {
  selection: ReturnType<typeof useReleaseSelection>; pageIds: string[]; rootId: string; onSignedOut: () => void;
  onSaved: () => void; persistent?: boolean;
}) {
  const [targets, setTargets] = useState<string[] | null>(null);
  const ids = selection.selected;
  if (!selection.enabled) return <Button variant="secondary" icon={Tags} onClick={selection.start}>Select releases</Button>;
  return <>
    <Button variant="secondary" icon={Tags} onClick={() => setTargets([...ids])}
      disabled={ids.size === 0 || ids.size > maxBatchFiles}>Edit selected tags</Button>
    <Button variant="ghost" onClick={() => selection.select(pageIds)} disabled={!pageIds.some(id => !ids.has(id))}>Select this page</Button>
    <Button variant="ghost" onClick={selection.stop} disabled={persistent && ids.size === 0}>
      {persistent ? 'Clear selection' : 'Cancel selection'}
    </Button>
    <span role="status" className="w-28 text-sm text-muted tabular-nums">{ids.size.toLocaleString()} selected</span>
    {ids.size > maxBatchFiles && <p role="alert" className="m-0 text-sm text-warning">Select up to 5,000 files per batch.</p>}
    {targets && <BulkReleaseTagsDialog rootId={rootId} fileIds={targets} onSignedOut={onSignedOut}
      onClose={() => setTargets(null)} onSaved={() => { setTargets(null); selection.stop(); onSaved(); }} />}
  </>;
}

export function SelectReleaseCheckbox({ label, checked, onChange, disabled = false }: {
  label: string; checked: boolean | 'mixed'; onChange: () => void; disabled?: boolean;
}) {
  const input = useRef<HTMLInputElement>(null);
  useLayoutEffect(() => {
    if (input.current) input.current.indeterminate = checked === 'mixed';
  }, [checked]);

  return <label className="grid size-11 shrink-0 place-items-center has-enabled:cursor-pointer">
    <input ref={input} className="peer sr-only" type="checkbox" aria-label={label} checked={checked === true}
      disabled={disabled} onChange={onChange} />
    <span aria-hidden="true" className="grid size-5 place-items-center rounded-full border border-control bg-field
      peer-checked:border-accent peer-checked:bg-accent peer-checked:text-accent-ink
      peer-indeterminate:border-accent peer-indeterminate:text-accent-readable peer-disabled:opacity-45
      peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-accent">
      {checked === true && <Check className="size-3.5" strokeWidth={3} />}
      {checked === 'mixed' && <Minus className="size-3.5" strokeWidth={3} />}
    </span>
  </label>;
}

function BulkReleaseTagsDialog({ rootId, fileIds, onSignedOut, onClose, onSaved }: {
  rootId: string; fileIds: string[]; onSignedOut: () => void; onClose: () => void; onSaved: () => void;
}) {
  const url = `/api/versions/${rootId}`;
  const selection = JSON.stringify({ fileIds });
  const [files, setFiles] = useState<SelectedRelease[] | null>(null);
  const [field, setField] = useState<ReleaseField>('language');
  const [value, setValue] = useState('');
  const [clear, setClear] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [stale, setStale] = useState(false);
  const choices = field === 'color' ? colorOptions : field === 'censorship' ? censorshipOptions : null;
  const fieldLabel = releaseFields.find(item => item.value === field)?.label;
  const count = new Set(files?.map(file => file.contentId)).size;

  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      const token = await getCsrfToken();
      if (controller.signal.aborted) return;
      const response = await fetch(`${url}/batch-preview`, { signal: controller.signal, method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': token }, body: selection });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error('Could not load the selected files. Reopen the editor to review the current selection.');
      const preview = await response.json() as SelectedRelease[];
      if (!controller.signal.aborted) setFiles(preview);
    }
    void load().catch(reason => {
      if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Could not load the selected files.');
    });
    return () => controller.abort();
  }, [url, selection, onSignedOut]);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!files || stale || !clear && !value.trim()) return;
    setBusy(true);
    setError('');
    try {
      const response = await fetch(`${url}/batch`, { method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: JSON.stringify({ field, value: clear ? null : value, targets: files.map(({ fileId, contentId, relativePath, tags }) =>
          ({ fileId, contentId, relativePath, revision: tags.revision })) }) });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        setStale(response.status === 409);
        const details = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(details?.message ?? 'Could not save release tags.');
      }
      onSaved();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save release tags.');
    } finally {
      setBusy(false);
    }
  }

  return <Modal title="Edit selected release tags" wide onClose={() => { if (!busy) onClose(); }}>
    {error && <p role="alert" className="m-0 text-danger">{error}</p>}
    {!files && !error && <p role="status">Loading selected releases…</p>}
    {files && <form onSubmit={save}>
      <p className="m-0 text-sm text-muted">{files.length} file {files.length === 1 ? 'location' : 'locations'} · {count} distinct {count === 1 ? 'release' : 'releases'}</p>
      <div className="grid items-end gap-3 sm:grid-cols-2">
        <Select label="Field to change" options={releaseFields} value={field} disabled={busy || stale}
          onChange={next => { setField(next); setValue(''); setClear(false); }} />
        {choices ? <Select label={`New ${fieldLabel?.toLowerCase()} value`} disabled={busy || stale} value={value}
          options={[{ value: '', label: 'Choose a value' }, ...choices]} onChange={setValue} /> :
          <label className="grid gap-1">{`New ${fieldLabel?.toLowerCase()}`}
            <input value={value} onChange={event => setValue(event.target.value)} disabled={busy || stale || clear}
              required={!clear} pattern={field === 'language' ? languagePattern : undefined} maxLength={field === 'language' ? 35 : 120} />
          </label>}
      </div>
      {!choices && <label className="flex min-h-11 items-center gap-2">
        <input type="checkbox" checked={clear} disabled={busy || stale} onChange={event => setClear(event.target.checked)} /> Clear existing values
      </label>}
      <ul aria-label="Selected files and current values" className="m-0 grid max-h-60 list-none gap-2 overflow-y-auto rounded-lg border border-line p-3" role="list">
        {files.map(file => <li key={file.fileId} className="grid gap-0.5 wrap-anywhere">
          <code>{file.relativePath}</code>
          <span className="text-sm text-muted">{choices?.find(choice => choice.value === file.tags[field])?.label ?? file.tags[field] ?? 'Unset'}</span>
        </li>)}
      </ul>
      <div className="action-row">
        <Button type="submit" disabled={busy || stale || !clear && !value.trim()}>{busy ? 'Saving…' : 'Apply to selected releases'}</Button>
        <Button variant="secondary" disabled={busy} onClick={onClose}>Cancel</Button>
      </div>
    </form>}
  </Modal>;
}

export function EditReleaseTags({ iconOnly, ...props }: EditorProps & { iconOnly?: boolean }) {
  const [open, setOpen] = useState(false);
  return <>
    <Button variant="ghost" icon={Tags} aria-label="Edit release tags" title="Edit release tags" onClick={() => setOpen(true)}>
      {!iconOnly && 'Edit release tags'}
    </Button>
    {open && <ReleaseTagsDialog {...props} onClose={() => setOpen(false)} />}
  </>;
}

export function ReleaseTagSummary({ tags }: { tags: ReleaseTags }) {
  const text = [tags.language, tags.color !== 'unknown' && colorOptions.find(item => item.value === tags.color)?.label,
    tags.censorship !== 'unknown' && censorshipOptions.find(item => item.value === tags.censorship)?.label, tags.edition, tags.group].filter(Boolean);
  return <p className="m-0 text-sm text-muted wrap-anywhere">{text.length ? text.join(' · ') : tags.revision ? 'Reviewed · No tags set' : 'Awaiting tag review'}</p>;
}

function ReleaseTagsDialog({ rootId, fileId, onSignedOut, onSaved, onClose }: EditorProps & { onClose: () => void }) {
  const url = `/api/versions/${rootId}/files/${fileId}`;
  const [release, setRelease] = useState<Release | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => {
    const controller = new AbortController();
    void fetch(url, { signal: controller.signal }).then(async response => {
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const current = await response.json() as Release;
      if (!controller.signal.aborted) setRelease(current);
    }).catch(() => { if (!controller.signal.aborted) setError('Could not load release tags for this file.'); });
    return () => controller.abort();
  }, [url, onSignedOut]);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!release) return;
    const fields = new FormData(event.currentTarget);
    setBusy(true);
    setError('');
    try {
      const response = await fetch(url, {
        method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: JSON.stringify({ ...release.tags, contentId: release.contentId,
          language: fields.get('language'), edition: fields.get('edition'), group: fields.get('group') }),
      });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        const details = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(details?.message ?? 'Could not save release tags.');
      }
      onSaved?.();
      onClose();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save release tags.');
    } finally {
      setBusy(false);
    }
  }

  return <Modal title="Edit release tags" onClose={() => { if (!busy) onClose(); }}>
    {error && <p role="alert" className="m-0 text-danger">{error}</p>}
    {!release && !error && <p role="status">Loading release tags…</p>}
    {release && <form onSubmit={save}>
      <p className="m-0 text-sm text-muted wrap-anywhere">{release.relativePath}</p>
      <label className="grid gap-1">Language code<input name="language" maxLength={35} pattern={languagePattern}
        placeholder="en, ja, zh-Hant" defaultValue={release.tags.language ?? ''} /></label>
      <div className="grid gap-3 sm:grid-cols-2">
        <Select label="Color" options={colorOptions} value={release.tags.color} disabled={busy}
          onChange={color => setRelease(current => current && ({ ...current, tags: { ...current.tags, color } }))} />
        <Select label="Censorship" options={censorshipOptions} value={release.tags.censorship} disabled={busy}
          onChange={censorship => setRelease(current => current && ({ ...current, tags: { ...current.tags, censorship } }))} />
      </div>
      <label className="grid gap-1">Edition<input name="edition" maxLength={120} defaultValue={release.tags.edition ?? ''} /></label>
      <label className="grid gap-1">Release group<input name="group" maxLength={120} defaultValue={release.tags.group ?? ''} /></label>
      <div className="action-row">
        <Button type="submit" disabled={busy}>{busy ? 'Saving…' : 'Save release tags'}</Button>
        <Button variant="secondary" disabled={busy} onClick={onClose}>Cancel</Button>
      </div>
    </form>}
  </Modal>;
}
