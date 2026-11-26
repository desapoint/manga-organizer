import { Tags } from 'lucide-react';
import { useEffect, useState } from 'react';
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

export function EditReleaseTags(props: EditorProps) {
  const [open, setOpen] = useState(false);
  return <>
    <Button variant="ghost" icon={Tags} onClick={() => setOpen(true)}>Edit release tags</Button>
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
