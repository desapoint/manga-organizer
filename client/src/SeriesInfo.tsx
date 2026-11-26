import { ExternalLink, Search, Save } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import { ROOTS_URL } from './library';
import { Select } from './Select';
import { CoverImage } from './SeriesCover';
import { ActionLink, Button, SearchField, SectionHeader } from './ui';

type BookKind = 'manga' | 'doujin';
type Metadata = {
  title: string;
  kind: BookKind;
  alternateTitle: string | null;
  summary: string | null;
  status: string | null;
  totalChapters: number | null;
  totalVolumes: number | null;
  providerId: number | null;
  providerUrl: string | null;
  providerDetails: AniListDetails | null;
};
type AniListDetails = {
  coverUrl: string | null;
  format: string | null;
  countryOfOrigin: string | null;
  startYear: number | null;
  endYear: number | null;
  genres: string[];
  tags: { name: string; spoiler: boolean }[];
  credits: { name: string; role: string | null }[];
};

type Details = {
  metadata: Metadata | null;
  availableChapters: number;
  readChapters: number;
  volumes: string[];
  chapters: { volume: string | null; chapter: string }[];
};

const kinds = [{ value: 'manga', label: 'Manga' }, { value: 'doujin', label: 'Doujin' }] as const;

function publicationYears({ startYear, endYear }: AniListDetails) {
  if (startYear && endYear && startYear !== endYear) return `${startYear}–${endYear}`;
  return startYear ?? endYear;
}

function MetadataTags({ names }: { names: string[] }) {
  return <ul className="m-0 flex list-none flex-wrap gap-1.5 p-0" role="list">
    {names.map(name => <li key={name} className="rounded-md border border-line px-2 py-1 text-xs wrap-anywhere">{name}</li>)}
  </ul>;
}

function ProviderFacts({ details }: { details: AniListDetails }) {
  const years = publicationYears(details);
  const tags = details.tags.filter(tag => !tag.spoiler).map(tag => tag.name);
  const spoilers = details.tags.filter(tag => tag.spoiler).map(tag => tag.name);
  return (
    <dl className="m-0 grid gap-2 text-sm wrap-anywhere">
      {details.format && <div><dt className="inline font-semibold">Format</dt> <dd className="inline text-muted">{details.format.toLowerCase().replaceAll('_', ' ')}</dd></div>}
      {years && <div><dt className="inline font-semibold">Publication years</dt> <dd className="inline text-muted">{years}</dd></div>}
      {details.countryOfOrigin && <div><dt className="inline font-semibold">Country of origin</dt> <dd className="inline text-muted">{details.countryOfOrigin}</dd></div>}
      {details.credits.length > 0 && <div><dt className="inline font-semibold">Credits</dt> <dd className="inline text-muted">
        {details.credits.map(credit => credit.role ? `${credit.name} (${credit.role})` : credit.name).join(' · ')}</dd></div>}
      {details.genres.length > 0 && <div><dt className="mb-1 font-semibold">Genres</dt><dd><MetadataTags names={details.genres} /></dd></div>}
      {(tags.length > 0 || spoilers.length > 0) && <div><dt className="mb-1 font-semibold">Tags</dt><dd>
        {tags.length > 0 && <MetadataTags names={tags} />}
        {spoilers.length > 0 && <details>
          <summary className="min-h-11 cursor-pointer py-2 text-muted">Show spoiler tags</summary>
          <MetadataTags names={spoilers} />
        </details>}
      </dd></div>}
    </dl>
  );
}

function SeriesMetadataForm({ title, metadata, save }: { title: string; metadata: Metadata | null; save: (values: object) => Promise<boolean> }) {
  const [kind, setKind] = useState<BookKind>(metadata?.kind ?? 'manga');
  const [busy, setBusy] = useState(false);

  async function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    setBusy(true);
    try {
      await save({
        kind,
        title: values.get('title'),
        alternateTitle: values.get('alternateTitle'),
        summary: values.get('summary'),
        totalChapters: Number(values.get('totalChapters')) || null,
        totalVolumes: Number(values.get('totalVolumes')) || null,
        status: values.get('status'),
      });
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={handleSave} className="grid gap-3">
      <label className="grid gap-1">Series title<input name="title" required maxLength={300} defaultValue={metadata?.title ?? title} /></label>
      <label className="grid gap-1">Alternate title<input name="alternateTitle" maxLength={300} defaultValue={metadata?.alternateTitle ?? ''} /></label>
      <Select label="Publication type" value={kind} onChange={setKind} options={kinds} />
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="grid gap-1">Total chapters<input name="totalChapters" type="number" min={1} max={100000}
          defaultValue={metadata?.totalChapters ?? ''} /></label>
        <label className="grid gap-1">Total volumes<input name="totalVolumes" type="number" min={1} max={100000}
          defaultValue={metadata?.totalVolumes ?? ''} /></label>
      </div>
      <label className="grid gap-1">Publication status<input name="status" maxLength={40} defaultValue={metadata?.status ?? ''} /></label>
      <label className="grid gap-1">Summary<textarea name="summary" rows={3} maxLength={6000} defaultValue={metadata?.summary ?? ''}
        className="min-w-0 rounded-lg border border-control bg-field p-3 text-base text-ink" /></label>
      <Button type="submit" icon={Save} disabled={busy} className="justify-self-start">Save details</Button>
    </form>
  );
}

export function SeriesInfo({ rootId, seriesKey, title, version, onSignedOut, onChange }: {
  rootId: string; seriesKey: string; title: string; version: string; onSignedOut: () => void; onChange: () => void;
}) {
  const url = ROOTS_URL + '/' + rootId + '/series/' + seriesKey;
  const [details, setDetails] = useState<Details | null>(null);
  const [query, setQuery] = useState(title);
  const [matches, setMatches] = useState<Metadata[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState<number | null>(null);
  const [error, setError] = useState('');
  const [edit, setEdit] = useState(false);

  useEffect(() => {
    const request = new AbortController();
    async function readDetails() {
      const response = await fetch(url + '/details', { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const details = await response.json() as Details;
      if (!request.signal.aborted) setDetails(details);
    }
    void readDetails().catch(() => { if (!request.signal.aborted) setError('Could not load series details.'); });
    return () => request.abort();
  }, [url, version, onSignedOut]);

  async function findMetadata(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError('');
    setLoading(true);
    try {
      const response = await fetch(url + '/metadata/search?' + new URLSearchParams({ title: query }));
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        const failure = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(failure?.message ?? 'Metadata lookup failed.');
      }
      setMatches(await response.json() as Metadata[]);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Metadata lookup failed.');
    } finally {
      setLoading(false);
    }
  }

  async function save(body: object) {
    setError('');
    try {
      const response = await fetch(url + '/metadata', {
        method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: JSON.stringify(body),
      });
      if (response.status === 401) {
        onSignedOut();
        return false;
      }
      if (!response.ok) {
        const failure = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(failure?.message ?? 'Could not save series metadata.');
      }
      setEdit(false);
      setMatches(null);
      onChange();
      return true;
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save series metadata.');
      return false;
    }
  }

  async function choose(candidate: Metadata) {
    if (candidate.providerId === null) return;
    setSaving(candidate.providerId);
    try {
      await save({ providerId: candidate.providerId, kind: candidate.kind });
    } finally {
      setSaving(null);
    }
  }

  const metadata = details?.metadata;
  return (
    <section className="grid min-w-0 gap-4 border-t border-line p-4" aria-label="Series details">
      {!details && !error && <p role="status" className="m-0 text-sm text-muted">Loading series details…</p>}
      {details && <>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="grid gap-1">
            <SectionHeader title={metadata?.title ?? 'Series details'} />
            {metadata && <span className="text-sm text-muted">{metadata.kind === 'doujin' ? 'Doujin' : 'Manga'}
              {metadata.status && ' · ' + metadata.status.toLowerCase().replaceAll('_', ' ')}
              {metadata.providerUrl && <> · <a className="text-accent-readable" href={metadata.providerUrl}
                target="_blank" rel="noreferrer">Open in AniList</a></>}
            </span>}
          </div>
          <Button variant="secondary" onClick={() => setEdit(value => !value)}>{edit ? 'Close editor' : 'Edit metadata'}</Button>
        </div>
        {metadata?.alternateTitle && <p className="m-0 text-sm text-muted">{metadata.alternateTitle}</p>}
        {metadata?.summary && <p className="m-0 max-w-prose text-sm whitespace-pre-line text-detail">{metadata.summary}</p>}
        {metadata?.providerDetails && <ProviderFacts details={metadata.providerDetails} />}
        <div className="grid gap-1 text-sm">
          <strong>Available chapters</strong>
          <span className="text-muted">{details.availableChapters.toLocaleString()}
            {metadata?.totalChapters ? ' of ' + metadata.totalChapters.toLocaleString() : ' · total unknown'}</span>
          {metadata?.totalChapters && <progress className="h-2 w-full max-w-80 accent-accent" aria-label="Available chapters"
            value={Math.min(details.availableChapters, metadata.totalChapters)} max={metadata.totalChapters} />}
        </div>
        <div className="grid gap-1 text-sm">
          <strong>Read chapters</strong>
          <span className="text-muted">{details.readChapters.toLocaleString()}
            {metadata?.totalChapters ? ' of ' + metadata.totalChapters.toLocaleString() : ' · total unknown'}</span>
          {metadata?.totalChapters && <progress className="h-2 w-full max-w-80 accent-accent" aria-label="Read chapters"
            value={Math.min(details.readChapters, metadata.totalChapters)} max={metadata.totalChapters} />}
        </div>
        {(details.volumes.length > 0 || details.chapters.length > 0) && <details>
          <summary className="min-h-11 cursor-pointer py-2 text-sm font-semibold">Volumes and chapters</summary>
          <ul className="m-0 flex list-none flex-wrap gap-1.5 p-0">
            {details.volumes.map(volume => <li key={'v' + volume} className="rounded-lg bg-nav-active px-2 py-1 text-sm">Vol. {volume}</li>)}
            {details.chapters.map(chapter => <li key={(chapter.volume ?? '') + ':' + chapter.chapter} className="rounded-lg border border-line px-2 py-1 text-sm">
              {chapter.volume && 'Vol. ' + chapter.volume + ' · '}Ch. {chapter.chapter}</li>)}
          </ul>
        </details>}
        {edit && <SeriesMetadataForm title={title} metadata={metadata ?? null} save={save} />}
      </>}
      <form onSubmit={findMetadata} className="action-row">
        <SearchField aria-label="Metadata title" value={query} onChange={event => setQuery(event.target.value)} required maxLength={300}
          className="flex-[1_1_12rem]" />
        <Button type="submit" variant="secondary" icon={Search} disabled={loading || !query.trim()}>
          {loading ? 'Looking up…' : 'Search AniList'}
        </Button>
      </form>
      {matches?.length === 0 && <p className="m-0 text-sm text-muted">No matching metadata found.</p>}
      {!!matches?.length && <ul className="m-0 grid list-none gap-2 p-0" role="list" aria-label="Metadata results">
        {matches.map(candidate => <li key={candidate.providerId} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-line p-3">
          <div className="flex min-w-0 flex-[1_1_14rem] items-center gap-3">
            <CoverImage src={candidate.providerDetails?.coverUrl ?? undefined} className="h-21 w-14 shrink-0 rounded-md" />
            <div className="grid min-w-0 gap-1 wrap-anywhere">
              <strong>{candidate.title}</strong>
              {candidate.alternateTitle && <span className="text-sm text-muted">{candidate.alternateTitle}</span>}
              <span className="text-sm text-muted">
                {[candidate.kind === 'doujin' ? 'Doujin' : 'Manga', candidate.providerDetails && publicationYears(candidate.providerDetails),
                  candidate.status?.toLowerCase().replaceAll('_', ' ')].filter(Boolean).join(' · ')}
              </span>
            </div>
          </div>
          <div className="action-row">
            {candidate.providerUrl && <ActionLink variant="ghost" icon={ExternalLink} href={candidate.providerUrl} target="_blank" rel="noreferrer">
              Open in AniList
            </ActionLink>}
            <Button variant="secondary" disabled={saving !== null} onClick={() => void choose(candidate)}>
              {saving === candidate.providerId ? 'Saving…' : 'Use this match'}
            </Button>
          </div>
        </li>)}
      </ul>}
      {error && <p role="alert" className="m-0 text-sm text-danger">{error}</p>}
    </section>
  );
}
