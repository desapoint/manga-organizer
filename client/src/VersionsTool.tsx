import { ChevronRight, Layers } from 'lucide-react';
import { useState } from 'react';
import type { FormEvent } from 'react';
import { PageControls, useInventoryPage } from './CatalogInventory';
import { duplicateToolsHref, versionsToolsHref } from './library';
import type { LibraryRoot, SeriesLocation } from './library';
import { censorshipOptions, colorOptions, EditReleaseTags, ReleaseSelectionActions, ReleaseTagSummary, SelectReleaseCheckbox,
  useReleaseSelection } from './ReleaseTagsEditor';
import type { ReleaseTags } from './ReleaseTagsEditor';
import { Select } from './Select';
import { ActionLink, Button, SearchField } from './ui';

type VersionSeries = SeriesLocation & { title: string; releaseCount: number };
type VersionFile = { rootId: string; fileId: string; relativePath: string; reason: 'span' | 'volume'; tags: ReleaseTags };
type ToolProps = { version: string; onSignedOut: () => void; onChanged: () => void };

const reviews = [{ value: '', label: 'Any review status' }, { value: 'true', label: 'Reviewed' }, { value: 'false', label: 'Awaiting review' }] as const;
const colors = [{ value: '', label: 'Any color' }, ...colorOptions] as const;
const censorship = [{ value: '', label: 'Any censorship' }, ...censorshipOptions] as const;

export function VersionsTool({ roots, series, version, onSignedOut, onChanged }: ToolProps & {
  roots: LibraryRoot[] | null; series: SeriesLocation | null;
}) {
  const [root, setRoot] = useState('');
  const [query, setQuery] = useState('');
  const [reviewed, setReviewed] = useState<typeof reviews[number]['value']>('');
  const [color, setColor] = useState<typeof colors[number]['value']>('');
  const [censored, setCensored] = useState<typeof censorship[number]['value']>('');

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const params = new URLSearchParams();
    for (const [name, value] of new FormData(event.currentTarget)) {
      if (typeof value === 'string' && value.trim()) params.set(name, value.trim());
    }
    if (color) params.set('color', color);
    if (censored) params.set('censorship', censored);
    if (reviewed) params.set('reviewed', reviewed);
    setQuery(params.toString());
  }

  const params = new URLSearchParams(query);
  if (root && !series) params.set('root', root);
  return <>
    <form onSubmit={applyFilters} className="surface grid grid-cols-1 items-end gap-3 p-4 sm:grid-cols-2 lg:grid-cols-3">
      <SearchField name="search" aria-label="Search release versions" placeholder="Series or file name" maxLength={160} />
      <label className="grid gap-1">Language code<input name="language" maxLength={35} pattern="[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*" placeholder="en" /></label>
      <Select label="Color" value={color} onChange={setColor} options={colors} />
      <Select label="Censorship" value={censored} onChange={setCensored} options={censorship} />
      <label className="grid gap-1">Edition<input name="edition" maxLength={120} /></label>
      <label className="grid gap-1">Release group<input name="group" maxLength={120} /></label>
      {!series && <Select label="Folder" value={root} onChange={setRoot} options={[{ value: '', label: 'All folders' },
        ...(roots ?? []).map(folder => ({ value: folder.id, label: folder.name || folder.path }))]} />}
      <Select label="Review status" value={reviewed} onChange={setReviewed} options={reviews} />
      <Button type="submit" variant="secondary" className="justify-self-start">Apply filters</Button>
    </form>
    {series ? <ReleaseList key={series.rootId + series.seriesKey + params} series={series} params={params}
      version={version} onSignedOut={onSignedOut} onChanged={onChanged} /> :
      <SeriesList key={params.toString()} roots={roots} params={params} version={version} onSignedOut={onSignedOut} />}
  </>;
}

function SeriesList({ params, version, roots, onSignedOut }: Omit<ToolProps, 'onChanged'> & { params: URLSearchParams; roots: LibraryRoot[] | null }) {
  const query = useInventoryPage<VersionSeries>('/api/versions/series', version, onSignedOut, params);
  return <>
    <ListStatus query={query} empty="No series with alternate release candidates match these filters." />
    {!!query.page?.items.length && <ul aria-label="Series with alternate releases" role="list" className="m-0 grid list-none gap-2 p-0">
      {query.page.items.map(item => {
        const folder = roots?.find(value => value.id === item.rootId);
        return <li key={item.rootId + item.seriesKey} className="surface">
          <a href={versionsToolsHref(item)} className="flex min-h-11 items-center gap-3 rounded-xl p-3 text-ink no-underline hover:bg-nav-hover">
            <Layers aria-hidden="true" className="size-5 shrink-0 text-nav-icon" />
            <span className="min-w-0 flex-1 wrap-anywhere">
              <strong className="block">{item.title}</strong>
              <span className="text-sm text-muted">{item.releaseCount.toLocaleString()} matching releases
                {folder && ` · ${folder.name || folder.path}`}</span>
            </span>
            <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-muted" />
          </a>
        </li>;
      })}
    </ul>}
    {query.page && <PageControls label="Alternate series pages" offset={query.offset} count={query.page.items.length}
      total={query.page.total} onChange={query.setOffset} />}
  </>;
}

function ReleaseList({ params, series, version, onSignedOut, onChanged }: ToolProps & { params: URLSearchParams; series: SeriesLocation }) {
  const query = useInventoryPage<VersionFile>(`/api/versions/${series.rootId}/series/${series.seriesKey}`, version, onSignedOut, params);
  const selection = useReleaseSelection(String(query.offset), query.page?.items.map(file => file.fileId) ?? []);
  return <>
    <ListStatus query={query} empty="No alternate releases in this series match these filters." />
    {!!query.page?.items.length && <ReleaseSelectionActions selection={selection} rootId={series.rootId}
      onSignedOut={onSignedOut} onSaved={onChanged} />}
    {!!query.page?.items.length && <ul aria-label="Alternate releases" role="list" className="m-0 grid list-none gap-2 p-0">
      {query.page.items.map(file => <li key={file.fileId} className="surface flex min-w-0 flex-wrap items-center justify-between gap-2 p-3">
        <div className="flex min-w-0 items-center gap-2">
          {selection.enabled && <SelectReleaseCheckbox path={file.relativePath} checked={selection.selected.includes(file.fileId)}
            onChange={() => selection.toggle(file.fileId)} />}
          <div className="min-w-0">
            <code className="wrap-anywhere">{file.relativePath}</code>
            <p className="m-0 text-sm text-muted">{file.reason === 'span' ? 'Same indexed chapter span' : 'Same numbered volume · Chapter coverage uncertain'}</p>
            <ReleaseTagSummary tags={file.tags} />
          </div>
        </div>
        <div className="action-row">
          <EditReleaseTags rootId={file.rootId} fileId={file.fileId} onSignedOut={onSignedOut} onSaved={onChanged} />
          <ActionLink variant="ghost" href={duplicateToolsHref(series, file.fileId)}>Compare releases</ActionLink>
        </div>
      </li>)}
    </ul>}
    {query.page && <PageControls label="Alternate release pages" offset={query.offset} count={query.page.items.length}
      total={query.page.total} onChange={query.setOffset} />}
  </>;
}

function ListStatus({ query, empty }: { query: { page: { total: number } | null; error: boolean; retry: () => void }; empty: string }) {
  return <>
    {query.error && <div className="action-row"><span role="alert" className="text-danger">Could not load alternate releases.</span>
      <Button variant="secondary" onClick={query.retry}>Retry</Button></div>}
    {!query.page && !query.error && <p role="status">Loading alternate releases…</p>}
    {query.page?.total === 0 && <p>{empty}</p>}
  </>;
}
