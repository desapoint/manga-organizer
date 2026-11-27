import { BookOpen, Check, Pencil, Plus } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import { PageControls, useInventoryPage } from './CatalogInventory';
import { seriesReaderHref } from './library';
import type { Details } from './SeriesInfo';
import { ActionLink, Button, Modal, SectionHeader } from './ui';

type Chapter = Details['chapters'][number];
type Release = { contentId: string; relativePath: string; format: string; pageIndex: number | null; pageCount: number | null;
  unresolvedVolume: boolean; preferred: boolean };
type EditionVolume = {
  id: string; edition: string; volume: string; chapters: string[]; contentIds: string[];
  available: string[]; state: 'complete' | 'partial' | 'missing' | 'unknown';
};
type SeriesTools = { url: string; version: string; onSignedOut: () => void; onChange: () => void; published?: boolean };

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

function VolumeReleasePicker({ volume, rootId, seriesKey, url, version, onSignedOut, onClose, published }: Omit<SeriesTools, 'onChange'> & {
  volume: string; rootId: string; seriesKey: string; onClose: () => void;
}) {
  const { items, error } = useSeriesList<Release>(url + '/volume-releases?' + new URLSearchParams({ volume, published: String(!!published) }), version, onSignedOut);
  return <Modal title={`Read volume ${volume}`} onClose={onClose}>
    {error && <p role="alert" className="text-danger">Could not load volume files.</p>}
    {!items && !error && <p role="status">Loading volume files…</p>}
    {items?.length === 0 && <p>No readable volume archive is indexed. Check the volume’s files for individual chapters or other formats.</p>}
    {items && items.length > 0 && <ul className="m-0 grid list-none divide-y divide-line p-0" role="list">
      {items.map(file => <li key={file.contentId} className="flex flex-wrap items-center justify-between gap-2 py-3">
        <div className="min-w-0 wrap-anywhere">
          <code>{file.relativePath}</code>
          <p className="m-0 text-sm text-muted">{file.format.toUpperCase()}{file.unresolvedVolume && ' · Chapter coverage unknown'}
            {file.pageIndex !== null && ` · Page ${file.pageIndex + 1} of ${file.pageCount}`}</p>
        </div>
        <ActionLink href={seriesReaderHref(file.contentId, rootId, seriesKey)} icon={BookOpen} variant="secondary"
          onClick={onClose}>{file.pageIndex === null ? 'Open file' : 'Resume file'}</ActionLink>
      </li>)}
    </ul>}
  </Modal>;
}

export function SeriesVolumeList({ details, rootId, seriesKey, onBrowse, ...tools }: SeriesTools & {
  details: Details; rootId: string; seriesKey: string; onBrowse: (kind: 'chapters' | 'files', volume: string) => void;
}) {
  const [limit, setLimit] = useState(50);
  const [releaseVolume, setReleaseVolume] = useState<string | null>(null);
  const released = Array.from({ length: details.metadata?.totalVolumes ?? 0 }, (_, index) => String(index + 1));
  const volumes = [...new Set([...details.volumes, ...released])].sort((a, b) => Number(a) - Number(b));
  const readings = new Map(details.volumeReading.map(item => [item.volume, item]));
  if (!volumes.length) return <p className="m-0 p-4 text-sm text-muted">No volume numbers are known for this series.</p>;

  return <>
    <ul aria-label="Series volumes" role="list" className="m-0 list-none divide-y divide-line p-0">
      {volumes.slice(0, limit).map(volume => {
        const reading = readings.get(volume);
        return <li key={volume} className="flex flex-wrap items-center justify-between gap-2 px-4 py-2">
          <strong>Volume {volume}</strong>
          <div className="action-row">
            {reading && <ActionLink variant="secondary" icon={BookOpen} href={seriesReaderHref(reading.contentId, rootId, seriesKey)}>Read volume</ActionLink>}
            {reading && reading.releaseCount > 1 && <Button variant="ghost"
              onClick={() => setReleaseVolume(volume)}>Choose release ({reading.releaseCount})</Button>}
            <LinkVolumeChapters {...tools} volume={volume} />
            <Button variant="ghost" onClick={() => onBrowse('chapters', volume)}>View chapters</Button>
            <Button variant="ghost" onClick={() => onBrowse('files', volume)}>View files</Button>
          </div>
        </li>;
      })}
    </ul>
    {limit < volumes.length && <div className="p-4"><Button variant="secondary" onClick={() => setLimit(value => value + 50)}>Show more volumes</Button></div>}
    {releaseVolume && <VolumeReleasePicker {...tools} volume={releaseVolume} rootId={rootId} seriesKey={seriesKey}
      onClose={() => setReleaseVolume(null)} />}
  </>;
}

function ReleasePicker({ chapter, rootId, seriesKey, url, version, onSignedOut, onChange, onClose, published }: SeriesTools & {
  chapter: Chapter; rootId: string; seriesKey: string; onClose: () => void;
}) {
  const query = new URLSearchParams({ chapter: chapter.chapter, volume: chapter.volume ?? '', published: String(!!published) });
  const { items, error } = useSeriesList<Release>(url + '/chapter-releases?' + query, version, onSignedOut);
  const [busy, setBusy] = useState(false);
  const [preferenceError, setPreferenceError] = useState('');

  async function prefer(file: Release) {
    setBusy(true);
    setPreferenceError('');
    try {
      await saveSeries(url + '/chapter-preference' + (published ? '?published=true' : ''), { volume: chapter.volume, chapter: chapter.chapter,
        contentId: file.preferred ? null : file.contentId }, onSignedOut);
      onChange();
    } catch (reason) {
      setPreferenceError(reason instanceof Error ? reason.message : 'Could not save the preferred release.');
    } finally {
      setBusy(false);
    }
  }

  return <Modal title={`Releases for chapter ${chapter.chapter}`} onClose={() => { if (!busy) onClose(); }}>
    {error && <p role="alert" className="text-danger">Could not load releases.</p>}
    {preferenceError && <p role="alert" className="text-danger">{preferenceError}</p>}
    {!items && !error && <p role="status">Loading releases…</p>}
    {items?.length === 0 && <p>No readable release is indexed for this chapter.</p>}
    {items && items.length > 0 && <>
      <p className="m-0 mb-3 text-sm text-muted">Each file opens at its own saved page. A volume can contain several chapters.</p>
      <ul className="m-0 grid list-none divide-y divide-line p-0" role="list">
        {items.map(file => <li key={file.contentId} className="flex flex-wrap items-center justify-between gap-2 py-3">
          <div className="min-w-0">
            <code>{file.relativePath}</code>
            <p className="m-0 text-sm text-muted">{file.format.toUpperCase()}{file.pageIndex !== null && ` · Page ${file.pageIndex + 1} of ${file.pageCount}`}</p>
            {file.preferred && <p className="m-0 text-sm text-positive">Preferred release</p>}
          </div>
          <div className="action-row">
            <ActionLink href={seriesReaderHref(file.contentId, rootId, seriesKey)} icon={BookOpen} variant="secondary"
              onClick={onClose}>{file.pageIndex === null ? 'Open file' : 'Resume file'}</ActionLink>
            <Button variant="ghost" disabled={busy} onClick={() => void prefer(file)}>{file.preferred ? 'Clear preference' : 'Prefer this release'}</Button>
          </div>
        </li>)}
      </ul>
    </>}
  </Modal>;
}

export function SeriesChapterList({ volume, rootId, seriesKey, url, version, onSignedOut, onChange, published }: SeriesTools & {
  volume: string | null; rootId: string; seriesKey: string;
}) {
  const [chosen, setChosen] = useState<Chapter | null>(null);
  const [filter, setFilter] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const params = new URLSearchParams({ search: filter.trim(), published: String(!!published) });
  if (volume) params.set('volume', volume);
  const query = useInventoryPage<Chapter>(url + '/chapters', version, onSignedOut, params);

  async function mark(chapter: Chapter) {
    setBusy(true);
    setError('');
    try {
      await saveSeries(url + '/chapter-progress' + (published ? '?published=true' : ''), { volume: chapter.volume, chapter: chapter.chapter, completed: !chapter.completed }, onSignedOut);
      onChange();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save chapter progress.');
    } finally {
      setBusy(false);
    }
  }

  return <>
    <label className="grid max-w-xs gap-1 p-4">Find a chapter
      <input value={filter} maxLength={25} onChange={event => { setFilter(event.target.value); query.setOffset(0); }}
        placeholder="Chapter or volume number" />
    </label>
    {error && <p role="alert" className="px-4 text-danger">{error}</p>}
    {query.error && <div className="action-row px-4 pb-4"><p role="alert" className="m-0 text-danger">Could not load chapters.</p>
      <Button variant="secondary" onClick={query.retry}>Retry</Button></div>}
    {!query.page && !query.error && <p role="status" className="p-4">Loading chapters…</p>}
    {query.page?.items.length ? <ul aria-label="Series chapters" role="list" className="m-0 list-none divide-y divide-line p-0">
      {query.page.items.map(chapter => <li key={`${chapter.volume ?? ''}:${chapter.chapter}`}
        className="flex flex-wrap items-center justify-between gap-2 px-4 py-2">
        <div className="grid"><strong>Chapter {chapter.chapter}</strong><span className="text-sm text-muted">
          {chapter.volume ? `Volume ${chapter.volume} · ` : ''}{chapter.completed ? 'Read' : 'Unread'}
          {chapter.missing ? ' · Missing local file' : chapter.readableFiles === 0 && ' · No readable file'}
        </span></div>
        <div className="action-row">
          {chapter.preferredContentId && chapter.readableFiles > 1 && <ActionLink icon={BookOpen} variant="secondary"
            href={seriesReaderHref(chapter.preferredContentId, rootId, seriesKey)}>Read preferred</ActionLink>}
          {chapter.readableFiles > 1 ? <Button variant="secondary" onClick={() => setChosen(chapter)}>Choose release ({chapter.readableFiles})</Button> :
            chapter.contentId && <ActionLink variant="ghost" icon={BookOpen} href={seriesReaderHref(chapter.contentId, rootId, seriesKey)}>Read file</ActionLink>}
          <Button variant="ghost" icon={chapter.completed ? undefined : Check} disabled={busy}
            onClick={() => void mark(chapter)}>{chapter.completed ? 'Mark unread' : 'Mark read'}</Button>
        </div>
      </li>)}
    </ul> : query.page && <p className="m-0 p-4 text-sm text-muted">{filter || volume ? 'No matching chapter numbers.' : 'No chapter numbers are known for this series.'}</p>}
    {query.page && <div className="px-4"><PageControls label="Chapter pages" offset={query.offset}
      count={query.page.items.length} total={query.page.total} onChange={query.setOffset} /></div>}
    {chosen && <ReleasePicker chapter={chosen} rootId={rootId} seriesKey={seriesKey} url={url} published={published}
      version={version} onSignedOut={onSignedOut} onChange={onChange} onClose={() => setChosen(null)} />}
  </>;
}

const states = { complete: 'Complete', partial: 'Partial', missing: 'Missing', unknown: 'Unknown' } as const;
const statusClasses = {
  complete: 'border-positive/45 bg-positive/10 text-positive', partial: 'border-warning/45 bg-warning/10 text-warning',
  missing: 'border-danger/45 bg-danger/10 text-danger', unknown: 'border-line bg-inset text-muted',
} as const;

function ManifestEditor({ initial, volume: initialVolume, url, version, onChange, onSignedOut, onClose, published }: SeriesTools & {
  initial?: EditionVolume; volume?: string; onClose: () => void;
}) {
  const [edition, setEdition] = useState(initial?.edition ?? (initialVolume ? 'Default' : ''));
  const [volume, setVolume] = useState(initial?.volume ?? initialVolume ?? '');
  const [chapters, setChapters] = useState(initial?.chapters.join(', ') ?? '');
  const [ids, setIds] = useState(initial?.contentIds ?? []);
  const [full, setFull] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const valid = /^\d{1,6}(\.\d{1,6})?$/.test(volume.trim());
  const files = useSeriesList<Release>(valid ? url + '/edition-candidates?' + new URLSearchParams({ volume: volume.trim(), published: String(!!published) }) : '', version, onSignedOut);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError('');
    try {
      await saveSeries(url + '/edition-volumes' + (published ? '?published=true' : ''), { edition, volume: volume.trim(), chapters: chapters.split(',').map(item => item.trim()).filter(Boolean),
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
      <label className="grid gap-1">Volume <input disabled={!!initial || !!initialVolume} value={volume} maxLength={13} required inputMode="decimal"
        onChange={event => { setVolume(event.target.value); setIds([]); setFull(null); }} /></label>
      <label className="grid gap-1">Verified chapters <textarea required value={chapters} maxLength={8000}
        onChange={event => setChapters(event.target.value)} placeholder="1, 1.5, 2" className="rounded-lg border border-control bg-field p-3" /></label>
      <p className="m-0 text-sm text-muted">Use the chapter list from this edition. Select local files if you also want to link them to the edition.</p>
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
      <Button type="submit" disabled={!valid || busy} className="justify-self-start">Save verified volume</Button>
    </form>
  </Modal>;
}

export function LinkVolumeChapters(props: SeriesTools & { volume: string }) {
  const [opened, setOpened] = useState(false);
  return <>
    <Button variant="ghost" icon={Pencil} onClick={() => setOpened(true)}>Link chapters</Button>
    {opened && <VolumeManifestDialog {...props} onClose={() => setOpened(false)} />}
  </>;
}

function VolumeManifestDialog(props: SeriesTools & { volume: string; onClose: () => void }) {
  const { items, error } = useSeriesList<EditionVolume>(props.url + '/edition-volumes?published=true', props.version, props.onSignedOut);
  if (!items) return <Modal title={`Link chapters for volume ${props.volume}`} onClose={props.onClose}>
    {error ? <p role="alert">Could not load this volume’s chapter list.</p> : <p role="status">Loading volume…</p>}
  </Modal>;
  const initial = items.find(item => item.volume === props.volume && item.edition === 'Default')
    ?? items.find(item => item.volume === props.volume);
  return <ManifestEditor {...props} initial={initial} />;
}

export function EditionCoverage({ url, version, onSignedOut, onChange, onBrowse, volume, editable = true, published }: SeriesTools & {
  onBrowse?: (number: string) => void; volume?: string; editable?: boolean;
}) {
  const { items, error } = useSeriesList<EditionVolume>(url + '/edition-volumes' + (published ? '?published=true' : ''), version, onSignedOut);
  const [selectedId, setSelectedId] = useState('');
  const [editing, setEditing] = useState<'new' | EditionVolume | null>(null);
  const selected = items?.find(item => item.id === selectedId) ?? items?.[0];

  return <section aria-label="Volume coverage" className="surface grid gap-3 p-4">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <SectionHeader title="Volume coverage" />
      {editable && <Button variant="secondary" icon={Plus} onClick={() => setEditing('new')}>Add verified volume</Button>}
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
      <details>
        <summary className="min-h-11 cursor-pointer py-2 text-sm font-semibold">Show chapter coverage</summary>
        <ul aria-label="Expected chapters" role="list" className="m-0 flex list-none flex-wrap gap-2 p-0">
          {selected.chapters.map(chapter => {
            const present = selected.available.includes(chapter);
            const status = present ? 'Indexed' : selected.state === 'unknown' ? 'Unknown' : 'Missing';
            return <li key={chapter} className={`rounded-md border px-2 py-1 text-xs ${present ? statusClasses.complete :
              selected.state === 'unknown' ? statusClasses.unknown : statusClasses.missing}`}>Ch. {chapter} · {status}</li>;
          })}
        </ul>
      </details>
      <div className="action-row">{onBrowse && <Button variant="ghost" onClick={() => onBrowse(selected.volume)}>View chapters</Button>}
        {editable && <Button variant="ghost" icon={Pencil} onClick={() => setEditing(selected)}>Edit verified volume</Button>}</div>
    </div>}
    {editing && <ManifestEditor initial={editing === 'new' ? undefined : editing} volume={volume} url={url} version={version}
      onChange={onChange} onSignedOut={onSignedOut} onClose={() => setEditing(null)} />}
  </section>;
}
