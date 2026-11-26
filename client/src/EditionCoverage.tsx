import { BookOpen, Check, Pencil, Plus } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import { seriesReaderHref } from './library';
import type { Details } from './SeriesInfo';
import { ActionLink, Button, Modal, SectionHeader } from './ui';

type Chapter = Details['chapters'][number];
type Release = { contentId: string; relativePath: string; format: string; pageIndex: number | null; pageCount: number | null; unresolvedVolume: boolean };
type EditionVolume = {
  id: string; edition: string; volume: string; chapters: string[]; contentIds: string[];
  available: string[]; state: 'complete' | 'partial' | 'missing' | 'unknown';
};
type SeriesTools = { url: string; version: string; onSignedOut: () => void; onChange: () => void };

function useSeriesList<T>(url: string, version: string, onSignedOut: () => void) {
  const [items, setItems] = useState<T[] | null>(null);
  const [error, setError] = useState(false);
  useEffect(() => {
    setItems(null);
    setError(false);
    if (!url) return;
    const request = new AbortController();
    void (async () => {
      const response = await fetch(url, { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const items = await response.json() as T[];
      if (!request.signal.aborted) setItems(items);
    })().catch(() => { if (!request.signal.aborted) setError(true); });
    return () => request.abort();
  }, [url, version, onSignedOut]);
  return { items, error };
}

async function saveSeries(url: string, data: object, onSignedOut: () => void) {
  const response = await fetch(url, { method: 'PUT', headers: {
    'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken(),
  }, body: JSON.stringify(data) });
  if (response.status === 401) onSignedOut();
  if (!response.ok) {
    const failure = await response.json().catch(() => null) as { message?: string } | null;
    throw new Error(failure?.message ?? 'Could not save this series change.');
  }
}

function ReleasePicker({ chapter, rootId, seriesKey, url, version, onSignedOut, onClose }: Omit<SeriesTools, 'onChange'> & {
  chapter: Chapter; rootId: string; seriesKey: string; onClose: () => void;
}) {
  const query = new URLSearchParams({ chapter: chapter.chapter, volume: chapter.volume ?? '' });
  const { items, error } = useSeriesList<Release>(url + '/chapter-releases?' + query, version, onSignedOut);
  return <Modal title={`Releases for chapter ${chapter.chapter}`} onClose={onClose}>
    {error && <p role="alert" className="text-danger">Could not load releases.</p>}
    {!items && !error && <p role="status">Loading releases…</p>}
    {items?.length === 0 && <p>No readable release is indexed for this chapter.</p>}
    {items && items.length > 0 && <>
      <p className="m-0 mb-3 text-sm text-muted">Each file opens at its own saved page. A volume can contain several chapters.</p>
      <ul className="m-0 grid list-none divide-y divide-line p-0" role="list">
        {items.map(file => <li key={file.contentId} className="flex flex-wrap items-center justify-between gap-2 py-3">
          <div className="min-w-0">
            <code>{file.relativePath}</code>
            <p className="m-0 text-sm text-muted">{file.format.toUpperCase()}{file.pageIndex !== null && ` · Page ${file.pageIndex + 1} of ${file.pageCount}`}</p>
          </div>
          <ActionLink href={seriesReaderHref(file.contentId, rootId, seriesKey)} icon={BookOpen} variant="secondary"
            onClick={onClose}>{file.pageIndex === null ? 'Open file' : 'Resume file'}</ActionLink>
        </li>)}
      </ul>
    </>}
  </Modal>;
}

export function SeriesChapterList({ chapters, rootId, seriesKey, url, version, onSignedOut, onChange }: SeriesTools & {
  chapters: Chapter[]; rootId: string; seriesKey: string;
}) {
  const [chosen, setChosen] = useState<Chapter | null>(null);
  const [filter, setFilter] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const filtered = chapters.filter(item => item.chapter.includes(filter.trim()) || item.volume?.includes(filter.trim()));
  const [limit, setLimit] = useState(50);

  async function mark(chapter: Chapter) {
    setBusy(true);
    setError('');
    try {
      await saveSeries(url + '/chapter-progress', { volume: chapter.volume, chapter: chapter.chapter, completed: !chapter.completed }, onSignedOut);
      onChange();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save chapter progress.');
    } finally {
      setBusy(false);
    }
  }

  return <>
    {chapters.length > 0 && <label className="grid max-w-xs gap-1 p-4">Find a chapter
      <input value={filter} onChange={event => { setFilter(event.target.value); setLimit(50); }} placeholder="Chapter or volume number" />
    </label>}
    {error && <p role="alert" className="px-4 text-danger">{error}</p>}
    {filtered.length ? <ul aria-label="Indexed chapters" role="list" className="m-0 list-none divide-y divide-line p-0">
      {filtered.slice(0, limit).map(chapter => <li key={`${chapter.volume ?? ''}:${chapter.chapter}`}
        className="flex flex-wrap items-center justify-between gap-2 px-4 py-2">
        <div className="grid"><strong>Chapter {chapter.chapter}</strong><span className="text-sm text-muted">
          {chapter.volume ? `Volume ${chapter.volume} · ` : ''}{chapter.completed ? 'Read' : 'Unread'}
        </span></div>
        <div className="action-row">
          {chapter.readableFiles > 1 ? <Button variant="secondary" onClick={() => setChosen(chapter)}>Choose release ({chapter.readableFiles})</Button> :
            chapter.contentId && <ActionLink variant="ghost" icon={BookOpen} href={seriesReaderHref(chapter.contentId, rootId, seriesKey)}>Read file</ActionLink>}
          <Button variant="ghost" icon={chapter.completed ? undefined : Check} disabled={busy}
            onClick={() => void mark(chapter)}>{chapter.completed ? 'Mark unread' : 'Mark read'}</Button>
        </div>
      </li>)}
    </ul> : <p className="m-0 p-4 text-sm text-muted">{filter ? 'No matching chapter numbers.' : 'No indexed chapter numbers. Files remain available in the Files tab.'}</p>}
    {filtered.length > limit && <div className="p-4"><Button variant="secondary" onClick={() => setLimit(count => count + 50)}>Show more chapters</Button></div>}
    {chosen && <ReleasePicker chapter={chosen} rootId={rootId} seriesKey={seriesKey} url={url}
      version={version} onSignedOut={onSignedOut} onClose={() => setChosen(null)} />}
  </>;
}

const states = { complete: 'Complete', partial: 'Partial', missing: 'Missing', unknown: 'Unknown' } as const;
const statusClasses = {
  complete: 'border-positive/45 bg-positive/10 text-positive', partial: 'border-warning/45 bg-warning/10 text-warning',
  missing: 'border-danger/45 bg-danger/10 text-danger', unknown: 'border-line bg-inset text-muted',
} as const;

function ManifestEditor({ initial, volume: initialVolume, url, version, onChange, onSignedOut, onClose }: SeriesTools & {
  initial?: EditionVolume; volume?: string; onClose: () => void;
}) {
  const [edition, setEdition] = useState(initial?.edition ?? '');
  const [volume, setVolume] = useState(initial?.volume ?? initialVolume ?? '');
  const [chapters, setChapters] = useState(initial?.chapters.join(', ') ?? '');
  const [ids, setIds] = useState(initial?.contentIds ?? []);
  const [full, setFull] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const valid = /^\d{1,6}(\.\d{1,6})?$/.test(volume.trim());
  const files = useSeriesList<Release>(valid ? url + '/edition-candidates?' + new URLSearchParams({ volume: volume.trim() }) : '', version, onSignedOut);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError('');
    try {
      await saveSeries(url + '/edition-volumes', { edition, volume: volume.trim(), chapters: chapters.split(',').map(item => item.trim()).filter(Boolean),
        contentIds: ids, fullVolumeContentId: full }, onSignedOut);
      onChange();
      onClose();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save the edition coverage.');
    } finally {
      setBusy(false);
    }
  }

  return <Modal title={initial ? 'Edit verified volume' : 'Add verified volume'} onClose={() => { if (!busy) onClose(); }}>
    <form onSubmit={save}>
      <label className="grid gap-1">Edition <input disabled={!!initial} value={edition} maxLength={120} required
        placeholder="English digital edition" onChange={event => setEdition(event.target.value)} /></label>
      <label className="grid gap-1">Volume <input disabled={!!initial} value={volume} maxLength={13} required inputMode="decimal"
        onChange={event => { setVolume(event.target.value); setIds([]); setFull(null); }} /></label>
      <label className="grid gap-1">Verified chapters <textarea required value={chapters} maxLength={8000}
        onChange={event => setChapters(event.target.value)} placeholder="1, 1.5, 2" className="rounded-lg border border-control bg-field p-3" /></label>
      <p className="m-0 text-sm text-muted">Use the chapter list from this edition. Select files that belong to the same edition.</p>
      {files.error && <p role="alert" className="m-0 text-danger">Could not load indexed releases for this volume.</p>}
      {valid && !files.items && !files.error && <p role="status">Loading indexed files…</p>}
      {files.items && files.items.length > 0 && <fieldset className="grid gap-2">
        <legend className="text-sm font-semibold">Indexed files in this edition</legend>
        {files.items.map(file => <div key={file.contentId} className="rounded-lg border border-line p-2">
          <label className="flex items-center gap-2"><input type="checkbox" checked={ids.includes(file.contentId)} onChange={event => {
            setIds(current => event.target.checked ? [...current, file.contentId] : current.filter(id => id !== file.contentId));
            if (!event.target.checked && full === file.contentId) setFull(null);
          }} /><code>{file.relativePath}</code></label>
          {file.unresolvedVolume && ids.includes(file.contentId) && <label className="flex items-center gap-2">
            <input type="checkbox" checked={full === file.contentId} onChange={event => setFull(event.target.checked ? file.contentId : null)} />
            I verified this file contains every listed chapter
          </label>}
        </div>)}
      </fieldset>}
      {error && <p role="alert" className="m-0 text-danger">{error}</p>}
      <Button type="submit" disabled={!valid || busy || !!files.error} className="justify-self-start">Save verified volume</Button>
    </form>
  </Modal>;
}

export function EditionCoverage({ url, version, onSignedOut, onChange, onBrowse, volume }: SeriesTools & {
  onBrowse: (number: string) => void; volume?: string;
}) {
  const { items, error } = useSeriesList<EditionVolume>(url + '/edition-volumes', version, onSignedOut);
  const [selectedId, setSelectedId] = useState('');
  const [editing, setEditing] = useState<'new' | EditionVolume | null>(null);
  const selected = items?.find(item => item.id === selectedId) ?? items?.[0];

  return <section aria-label="Volume coverage" className="surface grid gap-3 p-4">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <SectionHeader title="Volume coverage" />
      <Button variant="secondary" icon={Plus} onClick={() => setEditing('new')}>Add verified volume</Button>
    </div>
    {error && <p role="alert" className="m-0 text-danger">Could not load volume coverage.</p>}
    {items?.length === 0 && <p className="m-0 text-sm text-muted">Coverage is unknown until an edition has a verified chapter list.</p>}
    {items && items.length > 0 && <div className="action-row" aria-label="Verified volume map">
      {items.map(item => <Button key={item.id} variant="secondary" aria-pressed={selected?.id === item.id} onClick={() => setSelectedId(item.id)}
        className={statusClasses[item.state]}>{item.edition} · Vol. {item.volume} · {states[item.state]}</Button>)}
    </div>}
    {selected && <div className="grid gap-2 rounded-lg border border-line bg-inset p-3">
      <strong>{selected.edition} · Volume {selected.volume}</strong>
      <progress className="h-2 w-full accent-accent" value={selected.available.length} max={selected.chapters.length}
        aria-label="Verified chapter coverage" />
      <p className="m-0 text-sm text-muted">{selected.available.length} of {selected.chapters.length} verified chapters indexed</p>
      <div className="action-row"><Button variant="ghost" onClick={() => onBrowse(selected.volume)}>View chapters</Button>
        <Button variant="ghost" icon={Pencil} onClick={() => setEditing(selected)}>Edit verified volume</Button></div>
    </div>}
    {editing && <ManifestEditor initial={editing === 'new' ? undefined : editing} volume={volume} url={url} version={version}
      onChange={onChange} onSignedOut={onSignedOut} onClose={() => setEditing(null)} />}
  </section>;
}
