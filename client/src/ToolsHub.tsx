import { ArrowLeft, ChevronRight, Files, FolderCog, NotebookPen, ScanSearch } from 'lucide-react';
import { useState } from 'react';
import { PageControls, useInventoryPage } from './CatalogInventory';
import { DuplicateReview } from './DuplicateReview';
import { duplicateToolsHref, organizeToolsHref, ROOTS_URL, seriesHref } from './library';
import type { LibraryRoot, SeriesLocation, toolsLocation } from './library';
import { MetadataQueue } from './MetadataQueue';
import { OrganizeTools } from './OrganizeTools';
import { Select } from './Select';
import { ActionLink, Button, PageHeader, SearchField, SectionHeader, SegmentedControl } from './ui';

const duplicateUrl = '/api/file-operations/duplicates';
const kindOptions = [
  { value: 'all', label: 'All candidate types' },
  { value: 'bytes', label: 'Same indexed bytes' },
  { value: 'chapter', label: 'Shared chapter numbers' },
  { value: 'volume', label: 'Same numbered volume' },
] as const;

const reviewViews = [{ value: 'files', label: 'File duplicates' }, { value: 'overlaps', label: 'Series overlaps' }] as const;
type DuplicateSeries = { rootId: string; seriesKey: string; title: string; candidateFiles: number };
type QueueFile = { fileId: string; relativePath: string; reasons: string[] };
type Route = NonNullable<ReturnType<typeof toolsLocation>>;
type Inventory = { version: string; onSignedOut: () => void };

export function ToolsHub({ route, roots, version, onSignedOut, onChanged }: Inventory & {
  route: Route; roots: LibraryRoot[] | null; onChanged: () => void;
}) {
  const [root, setRoot] = useState('');
  const [kind, setKind] = useState<typeof kindOptions[number]['value']>('all');
  const [search, setSearch] = useState('');
  const series = route.module === 'duplicates' ? route.series : null;
  const name = { home: 'Tools', metadata: 'Associations and metadata', duplicates: 'Duplicates', organize: 'Organize' }[route.module];
  const query: Record<string, string> = { search: search.trim() };
  if (kind !== 'all') query.kind = kind;
  if (root) query.root = root;
  return <section className="workspace-page" aria-labelledby="tools-heading">
    {route.module !== 'home' && <ActionLink variant="ghost" icon={ArrowLeft}
      href={series ? duplicateToolsHref() : '#tools'} className="justify-self-start">{series ? 'Back to duplicates' : 'Back to Tools'}</ActionLink>}
    <PageHeader id="tools-heading" section={route.module === 'home' ? 'Management' : 'Tools'}
      title={name} />
    {route.module === 'home' ? <div className="action-row">
      <ActionLink variant="secondary" icon={Files} href={duplicateToolsHref()}>Duplicates</ActionLink>
      <ActionLink variant="secondary" icon={NotebookPen} href="#tools/metadata">Associations and metadata</ActionLink>
      <ActionLink variant="secondary" icon={FolderCog} href={organizeToolsHref()}>Organize</ActionLink>
    </div> : route.module === 'metadata' ? <MetadataQueue roots={roots} version={version} onSignedOut={onSignedOut} /> :
      route.module === 'organize' ? <OrganizeTools rootId={route.rootId} seriesKey={route.seriesKey} roots={roots} version={version}
        onSignedOut={onSignedOut} onChanged={onChanged} /> :
      series ? <DuplicateWorkbench key={series.rootId + series.seriesKey} series={series} fileId={route.fileId} kind={query.kind} version={version}
        onSignedOut={onSignedOut} onChanged={onChanged} /> :
        <>
          <div className="surface flex flex-wrap items-end gap-3 p-4">
            <SearchField className="min-w-0 flex-1" aria-label="Find duplicate series" placeholder="Search by series title"
              value={search} onChange={event => setSearch(event.target.value)} maxLength={300} />
            <Select label="Folder" value={root} options={[{ value: '', label: 'All folders' }, ...(roots ?? []).map(folder => ({
              value: folder.id, label: folder.name || folder.path,
            }))]} onChange={setRoot} />
            <Select label="Candidate type" value={kind} options={kindOptions} onChange={setKind} />
          </div>
          <DuplicateIndex key={JSON.stringify([root, kind, search])} roots={roots} version={version} onSignedOut={onSignedOut}
            query={query} />
        </>}
  </section>;
}

function DuplicateIndex({ roots, version, onSignedOut, query }: Inventory & { roots: LibraryRoot[] | null; query: Record<string, string> }) {
  const { page, error, retry, offset, setOffset } = useInventoryPage<DuplicateSeries>(duplicateUrl + '/series', version, onSignedOut, query);
  return <>
    {error && <QueueError onRetry={retry}>Could not load the duplicate queue.</QueueError>}
    {!page && !error && <p role="status">Loading duplicate candidates…</p>}
    {page?.total === 0 && <p>No indexed duplicate candidates match these filters.</p>}
    {page && page.total > 0 && <>
      <ul aria-label="Series with duplicate candidates" className="m-0 grid list-none gap-2 p-0" role="list">
        {page.items.map(item => {
          const folder = roots?.find(root => root.id === item.rootId);
          return <li key={item.rootId + item.seriesKey} className="surface">
            <a href={duplicateToolsHref(item)} className="flex min-h-11 items-center gap-3 rounded-xl p-3 text-ink no-underline hover:bg-nav-hover">
              <Files aria-hidden="true" className="size-5 shrink-0 text-nav-icon" />
              <span className="min-w-0 flex-1">
                <strong className="block">{item.title}</strong>
                <span className="text-sm text-muted">{item.candidateFiles.toLocaleString()} candidate {item.candidateFiles === 1 ? 'file' : 'files'}
                  {folder && ` · ${folder.name || folder.path}`}</span>
              </span>
              <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-muted" />
            </a>
          </li>;
        })}
      </ul>
      <PageControls label="Duplicate series pages" offset={offset} count={page.items.length} total={page.total} onChange={setOffset} />
    </>}
  </>;
}

function QueueError({ children, onRetry }: { children: string; onRetry: () => void }) {
  return <div className="action-row"><span role="alert" className="text-danger">{children}</span>
    <Button variant="secondary" onClick={onRetry}>Retry</Button></div>;
}

function DuplicateWorkbench({ series, fileId, kind, version, onSignedOut, onChanged }: Inventory & {
  series: SeriesLocation; fileId?: string; kind?: string; onChanged: () => void;
}) {
  const [view, setView] = useState<typeof reviewViews[number]['value']>('files');
  const { rootId, seriesKey } = series;
  const { page } = useInventoryPage<{ title: string }>(`${ROOTS_URL}/${rootId}/series`, version, onSignedOut, { seriesKey });
  return <div className="grid min-w-0 gap-4">
    {page?.items[0] && <SectionHeader title={page.items[0].title} />}
    <div className="action-row">
      <SegmentedControl label="Duplicate views" options={reviewViews} value={view} onChange={setView} />
      <ActionLink variant="ghost" href={seriesHref(rootId, seriesKey)}>Open series in Library</ActionLink>
    </div>
    {view === 'overlaps' ? <div className="surface overflow-hidden">
      <DuplicateReview {...series} version={version} onSignedOut={onSignedOut} onChanged={onChanged} />
    </div> : fileId ? <div className="grid min-w-0 gap-2">
      <ActionLink className="justify-self-start" variant="ghost" icon={ArrowLeft} href={duplicateToolsHref(series)}>Back to candidate files</ActionLink>
      <div className="surface overflow-hidden">
        <DuplicateReview key={fileId} {...series} fileId={fileId} quickReview version={version} onSignedOut={onSignedOut} onChanged={onChanged} />
      </div>
    </div> : <FileQueue series={series} kind={kind} version={version} onSignedOut={onSignedOut} />}
  </div>;
}

function FileQueue({ series, kind, version, onSignedOut }: Inventory & { series: SeriesLocation; kind?: string }) {
  const url = `${duplicateUrl}/${series.rootId}/series/${series.seriesKey}/queue`;
  const { page, error, retry, offset, setOffset } = useInventoryPage<QueueFile>(url, version, onSignedOut, kind ? { kind } : undefined);
  return <section aria-label="Candidate files" className="surface grid gap-3 p-4">
    <SectionHeader title="Candidate files" />
    {error && <QueueError onRetry={retry}>Could not load matching files.</QueueError>}
    {!page && !error && <p role="status">Loading matching files…</p>}
    {page?.total === 0 && <p>No indexed duplicate candidates for this series.</p>}
    {page && page.total > 0 && <>
      <ul className="m-0 list-none divide-y divide-line p-0" role="list">
        {page.items.map(file => <li key={file.fileId} className="flex flex-wrap items-center justify-between gap-2 py-2">
          <div className="min-w-0 wrap-anywhere">
            <code>{file.relativePath}</code>
            <p className="m-0 text-sm text-muted">{kindOptions.filter(item => file.reasons.includes(item.value)).map(item => item.label).join(' · ')}</p>
          </div>
          <ActionLink variant="secondary" icon={ScanSearch} href={duplicateToolsHref(series, file.fileId)}>Compare file</ActionLink>
        </li>)}
      </ul>
      <PageControls label="Candidate file pages" offset={offset} count={page.items.length} total={page.total} onChange={setOffset} />
    </>}
  </section>;
}
