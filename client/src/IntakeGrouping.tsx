import { Group } from 'lucide-react';
import { useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import type { Series } from './CatalogInventory';
import { ROOTS_URL } from './library';
import { Select } from './Select';
import { Button, Modal } from './ui';

const kinds = [{ value: 'manga', label: 'Manga' }, { value: 'doujin', label: 'Doujin' }] as const;

export function IntakeGroupingDialog({ rootId, series, onClose, onSaved, onSignedOut }: {
  rootId: string;
  series: Series[];
  onClose: () => void;
  onSaved: () => void;
  onSignedOut: () => void;
}) {
  const [title, setTitle] = useState('');
  const [kind, setKind] = useState<'manga' | 'doujin'>('manga');
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const saved = series.filter(item => item.hasMetadata);
  const target = saved.length === 1 ? saved[0] : undefined;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError('');
    try {
      const response = await fetch(ROOTS_URL + '/' + rootId + '/group-series', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: JSON.stringify({ seriesKeys: series.map(item => item.key), title: title.trim(), kind,
          targetSeriesKey: target?.key, confirm }),
      });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        const failure = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(failure?.message ?? 'Could not group these entries.');
      }
      onSaved();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not group these entries.');
    } finally {
      setBusy(false);
    }
  }

  return <Modal title="Group selected entries" onClose={() => { if (!busy) onClose(); }}>
    <form className="grid min-w-0 gap-3" onSubmit={submit}>
      <p className="m-0 text-sm text-muted">Group all files in the selected entries.</p>
      <ul aria-label="Selected entries" className="m-0 max-h-44 overflow-auto rounded-lg border border-line px-6 py-2 text-sm text-muted">
        {series.map(item => <li key={item.key} className="wrap-anywhere">{item.title}</li>)}
      </ul>
      {saved.length > 1 ? <p role="alert" className="m-0 text-sm text-danger">
        This selection has multiple saved associations. Select at most one saved series as the destination.
      </p> : <>
        {target ? <p className="m-0 text-sm">Add the selected files to <strong>{target.title}</strong>.</p> :
          <label className="grid gap-1">Series title<input required maxLength={300} value={title} onChange={event => setTitle(event.target.value)} /></label>}
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" checked={confirm} onChange={event => setConfirm(event.target.checked)} />
          {target ? 'Confirm the selected files for this series' : 'Confirm with manual metadata'}
        </label>
        {confirm && !target && <Select label="Publication type" value={kind} onChange={setKind} options={kinds} />}
        {!target && <p className="m-0 text-sm text-muted">You can link the grouped series to AniList from its Import row.</p>}
        {error && <p role="alert" className="m-0 text-sm text-danger">{error}</p>}
        <Button type="submit" icon={Group} className="justify-self-start" disabled={busy || (!target && !title.trim())}>
          {busy ? 'Grouping…' : 'Group selected'}
        </Button>
      </>}
    </form>
  </Modal>;
}
