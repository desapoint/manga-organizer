import { ArrowLeft, BookOpen, ChevronRight, Info, Library, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { ChapterCoverage } from './ChapterCoverage';
import { CollectionForm, emptyFilters, SeriesCollectionMembership, TagRulesFields, useTagNames } from './CollectionControls';
import type { TagRules } from './CollectionControls';
import { DuplicateReviewDialog } from './DuplicateReview';
import { FileMove } from './FileOperations';
import type { LibraryRoot } from './library';
import { MetadataDescription, MetadataDialogAction, SeriesInfo, useSeriesDetails } from './SeriesInfo';
import { EditionCoverage, SeriesChapterList, VolumeReleasePicker } from './EditionCoverage';
import { FileCover, SeriesCover } from './SeriesCover';
import { duplicateToolsHref, formatSize, isReadableArchive, organizeToolsHref, ROOTS_URL, seriesHref, seriesReaderHref } from './library';
import { Select } from './Select';
import { ActionLink, Button, Modal, PageHeader, SearchField, SectionHeader, SegmentedControl } from './ui';

const PAGE_SIZE = 50;

type CatalogEntry = {
  id: string;
  relativePath: string;
  extension: string;
  size: number;
  modifiedAt: string;
  contentId: string | null;
  sha256: string | null;
  indexedLocations: number | null;
  volume: string | null;
  chapter: string | null;
  hasCover: boolean;
  seriesCoverKey: string | null;
  seriesKey: string;
};

export type Series = { rootId: string; key: string; title: string; fileCount: number; suggested: boolean; hasCover: boolean; hasMetadata: boolean };
type Page<T> = { total: number; items: T[] };
type InventoryProps = { rootId: string; version: string; onSignedOut: () => void };
type FileManagement = { onDataChanged: () => void; managedFolders: LibraryRoot[]; onMoved: () => void };
type InventoryActions = InventoryProps & FileManagement;
type Inspection = { kind: 'series'; series: Series } | { kind: 'file'; file: CatalogEntry };
type Inspect = (item: Inspection) => void;
type InventoryView = 'covers' | 'series' | 'files';
type SeriesSort = 'title' | 'recent' | 'files';

const sortOptions = [
  { value: 'title', label: 'Title' },
  { value: 'recent', label: 'Recently modified' },
  { value: 'files', label: 'Most files' },
] as const;

export function useInventoryPage<T>(url: string, version: string, onSignedOut: () => void, query?: Record<string, string> | URLSearchParams) {
  const [offset, setOffset] = useState(0);
  const [stored, setStored] = useState<{ url: string; page: Page<T> } | null>(null);
  const [error, setError] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const params = new URLSearchParams(query);
  params.set('offset', String(offset));
  params.set('limit', String(PAGE_SIZE));
  const pageUrl = url ? `${url}?${params}` : '';

  useEffect(() => {
    if (!pageUrl) return;
    const request = new AbortController();
    setError(false);

    async function load() {
      const response = await fetch(pageUrl, { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const page = await response.json() as Page<T>;
      if (request.signal.aborted) return;
      if (offset > 0 && offset >= page.total) {
        setOffset(Math.max(0, Math.floor((page.total - 1) / PAGE_SIZE) * PAGE_SIZE));
      } else {
        setStored({ url: pageUrl, page });
      }
    }

    void load().catch(() => {
      if (!request.signal.aborted) setError(true);
    });
    return () => request.abort();
  }, [pageUrl, version, offset, attempt, onSignedOut]);

  const page = stored?.url === pageUrl ? stored.page : null;
  return { page, error, offset, setOffset, retry: () => setAttempt(value => value + 1) };
}

export function PageControls({ label, offset, count, total, onChange }: {
  label: string; offset: number; count: number; total: number; onChange: (offset: number) => void;
}) {
  if (total <= PAGE_SIZE) return null;

  return (
    <nav className="pagination" aria-label={label}>
      <Button variant="secondary" disabled={offset === 0} onClick={() => onChange(offset - PAGE_SIZE)}>Previous</Button>
      <span>{offset + 1}–{offset + count} of {total.toLocaleString()}</span>
      <Button variant="secondary" disabled={offset + PAGE_SIZE >= total} onClick={() => onChange(offset + PAGE_SIZE)}>Next</Button>
    </nav>
  );
}

function InventoryError({ children, retry }: { children: string; retry: () => void }) {
  return (
    <div className="action-row text-danger">
      <span role="alert">{children}</span> <Button variant="secondary" onClick={retry}>Retry</Button>
    </div>
  );
}

function FileRow({ file, rootId, seriesKey, version, onInspect, management, onSignedOut }:
  Pick<InventoryProps, 'rootId' | 'version'> & {
  file: CatalogEntry; seriesKey?: string; onInspect?: Inspect; onSignedOut: () => void; management?: FileManagement;
}) {
  const [showCoverage, setShowCoverage] = useState(false);
  const [showMove, setShowMove] = useState(false);
  const [showDuplicates, setShowDuplicates] = useState(false);
  const [moving, setMoving] = useState(false);
  const separator = file.relativePath.lastIndexOf('/');
  const name = file.relativePath.slice(separator + 1);
  const readable = file.contentId && isReadableArchive(file.extension);
  const coverageUrl = management && `${ROOTS_URL}/${rootId}/series/${file.seriesKey}/coverage`;
  return (
    <li>
      <span className="flex min-w-0 items-center gap-3">
        <FileCover rootId={rootId} fileId={file.id} available={file.hasCover} seriesCoverKey={file.seriesCoverKey} revision={version}
          className="h-12 w-8 shrink-0 rounded-sm" />
        <span className="file-path" title={file.relativePath}>
          {onInspect ? <button type="button" className="min-h-11 cursor-pointer rounded-sm border-0 bg-transparent p-0 text-start
            text-inherit wrap-anywhere hover:text-accent-readable hover:underline desktop:min-h-0"
            onClick={() => onInspect({ kind: 'file', file })}>{name}</button> : name}
          {separator >= 0 && <small>{file.relativePath.slice(0, separator)}</small>}
          {(file.volume || file.chapter) && <small>{file.volume && `Vol. ${file.volume}`}{file.volume && file.chapter && ' · '}
            {file.chapter && `Ch. ${file.chapter}`}</small>}
          {file.indexedLocations === null && <small>Content identity unavailable</small>}
          {file.indexedLocations !== null && file.indexedLocations > 1 && (
            <small>Same bytes at {file.indexedLocations.toLocaleString()} indexed locations</small>
          )}
        </span>
      </span>
      <span className="file-format">{file.extension.toUpperCase()}</span>
      <span>{formatSize(file.size)}</span>
      <time dateTime={file.modifiedAt}>{new Date(file.modifiedAt).toLocaleDateString()}</time>
      {file.contentId && <div className="action-row col-span-full">
        {readable && <ActionLink variant="ghost" icon={BookOpen} href={onInspect ? `#read/${file.contentId}/intake` :
          seriesKey ? seriesReaderHref(file.contentId, rootId, seriesKey) : `#read/${file.contentId}`}>Read file</ActionLink>}
        {onInspect ? <Button variant="ghost" onClick={() => setShowDuplicates(true)}>Compare this file</Button> :
          <ActionLink variant="ghost" href={duplicateToolsHref({ rootId, seriesKey: file.seriesKey }, file.id)}>Compare in Tools</ActionLink>}
        {coverageUrl && <Button variant="ghost" onClick={() => setShowCoverage(true)}>Set chapter coverage</Button>}
        {!!management?.managedFolders.length && <Button variant="ghost" onClick={() => setShowMove(true)}>Move or rename</Button>}
        {showDuplicates && management && <DuplicateReviewDialog rootId={rootId} fileId={file.id} version={version} onSignedOut={onSignedOut}
          onClose={() => setShowDuplicates(false)} onChanged={management.onMoved} />}
        {showCoverage && coverageUrl && management && <Modal title="Set chapter coverage" onClose={() => setShowCoverage(false)}>
          <p className="m-0 mb-3 text-sm"><code>{file.relativePath}</code></p>
          <ChapterCoverage url={coverageUrl} contentId={file.contentId} volumeHint={file.volume}
            chapterHint={file.chapter} onSaved={management.onDataChanged} onSignedOut={onSignedOut} />
        </Modal>}
        {showMove && management && <Modal title="Move or rename file" wide onClose={() => { if (!moving) setShowMove(false); }}>
          <FileMove rootId={rootId} fileId={file.id} relativePath={file.relativePath} folders={management.managedFolders}
            onSignedOut={onSignedOut} onApplying={setMoving} onApplied={() => { setShowMove(false); management.onMoved(); }} />
        </Modal>}
      </div>}
    </li>
  );
}

export function FileInventory({ rootId, seriesKey, volume, version, onSignedOut, management, onInspect, search = '' }:
  InventoryProps & { seriesKey?: string; volume?: string; management?: FileManagement; onInspect?: Inspect; search?: string }) {
  const { page, offset, setOffset, error, retry } = useInventoryPage<CatalogEntry>(`${ROOTS_URL}/${rootId}/files`, version, onSignedOut,
    seriesKey ? { seriesKey, search, ...(volume ? { volume } : {}) } : { search });
  const label = seriesKey ? 'Series files' : 'Catalog files';
  return (
    <>
      {error && <div className="px-4 pb-3"><InventoryError retry={retry}>Could not load files.</InventoryError></div>}
      {!page && !error && <p className="px-4" role="status">Loading files…</p>}
      {page?.total === 0 && <p className="px-4">{search ? 'No files match this search.' : 'No files in the current inventory.'}</p>}
      {page && page.total > 0 && (
        <div className={seriesKey ? 'min-w-0 rounded-b-xl border-t border-line bg-inset' : 'surface'}>
          <div className="catalog-columns" aria-hidden="true"><span>File</span><span>Type</span><span>Size</span><span>Modified</span></div>
          <ul className="catalog-files" role="list" aria-label={label}>
            {page.items.map(file => <FileRow key={file.id} file={file} rootId={rootId} seriesKey={seriesKey} version={version}
              onInspect={onInspect} onSignedOut={onSignedOut} management={management} />)}
          </ul>
          {page.total > PAGE_SIZE && (
            <div className="border-t border-line p-3">
              <PageControls label={`${label} pages`} offset={offset} count={page.items.length} total={page.total} onChange={setOffset} />
            </div>
          )}
        </div>
      )}
    </>
  );
}

function SeriesRow({ series, onInspect, coverVersion, ...inventory }: InventoryActions & { series: Series; onInspect?: Inspect; coverVersion: string }) {
  const [open, setOpen] = useState(false);
  const rowClass = 'grid cursor-pointer list-none grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2 rounded-xl p-3 hover:bg-nav-hover sm:gap-3 sm:px-4';
  const contents = <>
    {series.hasCover ? <SeriesCover rootId={inventory.rootId} seriesKey={series.key} available={series.hasCover} revision={coverVersion} className="h-14 w-10 rounded-md" /> :
      <span className="grid size-8 place-items-center rounded-lg bg-nav-active text-nav-icon"><Library className="size-4" aria-hidden="true" /></span>}
    <span className="wrap-anywhere">
      <span className="block font-semibold">{series.title}</span>
      <span className="block text-sm text-muted">{series.fileCount.toLocaleString()} {series.fileCount === 1 ? 'file' : 'files'}</span>
    </span>
    <ChevronRight className="size-4 text-muted group-open:rotate-90" aria-hidden="true" />
  </>;

  return (
    <li className="surface">
      {onInspect ? <details className="group" onToggle={event => {
        const expanded = event.currentTarget.open;
        setOpen(expanded);
        if (expanded) onInspect({ kind: 'series', series });
      }}>
        <summary className={`${rowClass} [&::-webkit-details-marker]:hidden`}>{contents}</summary>
        {open && <>
          <SeriesInfo rootId={inventory.rootId} seriesKey={series.key} title={series.title} version={inventory.version}
            onChange={inventory.onDataChanged} onSignedOut={inventory.onSignedOut} />
          <FileInventory {...inventory} seriesKey={series.key} onInspect={onInspect} management={inventory} />
        </>}
      </details> : <a className={`${rowClass} text-ink no-underline`} href={seriesHref(inventory.rootId, series.key)}>{contents}</a>}
    </li>
  );
}

export function CoverGrid({ series, coverVersion }: { series: Series[]; coverVersion: string }) {
  return (
    <ul className="m-0 grid list-none grid-cols-[repeat(auto-fill,minmax(min(100%,8rem),1fr))] gap-4 p-0 sm:gap-5" role="list" aria-label="Series covers">
      {series.map(item => (
        <li key={item.rootId + item.key} className="min-w-0">
          <a href={seriesHref(item.rootId, item.key)} className="group grid gap-2 rounded-xl border border-transparent p-1 text-start text-ink no-underline hover:bg-panel">
            <SeriesCover rootId={item.rootId} seriesKey={item.key} available={item.hasCover} revision={coverVersion}
              className="aspect-[2/3] rounded-lg" />
            <span className="line-clamp-2 min-h-10 px-1 text-sm font-semibold wrap-anywhere" title={item.title}>{item.title}</span>
            <span className="px-1 pb-1 text-xs text-muted">{item.fileCount.toLocaleString()} {item.fileCount === 1 ? 'file' : 'files'}</span>
          </a>
        </li>
      ))}
    </ul>
  );
}

const seriesViews = [{ value: 'chapters', label: 'Chapters' }, { value: 'volumes', label: 'Volumes' }, { value: 'files', label: 'Files' }] as const;
type SeriesView = typeof seriesViews[number]['value'];

function CollectionTotal({ label, value, total }: { label: string; value: number; total: number | null }) {
  return <div className="grid min-w-0 gap-1.5">
    <strong className="text-sm">{label}</strong>
    <span className="text-sm text-muted">{value.toLocaleString()}{total === null ? ' · released total unknown' : ` of ${total.toLocaleString()}`}</span>
    {!!total && <progress className="h-2 w-full accent-accent" aria-label={label} value={Math.min(value, total)} max={total} />}
  </div>;
}

export function SeriesPage({ seriesKey, coverVersion, rootPath, ...inventory }: InventoryProps & {
  seriesKey: string; coverVersion: string; rootPath?: string;
}) {
  const [revision, setRevision] = useState(0);
  const [dialog, setDialog] = useState<'info' | 'cover' | null>(null);
  const [selectedView, setView] = useState<SeriesView | null>(null);
  const [volume, setVolume] = useState<string | null>(null);
  const [readingVolume, setReadingVolume] = useState<string | null>(null);
  const catalog = { ...inventory, version: `${inventory.version}:${revision}` };
  const onDataChanged = () => setRevision(value => value + 1);
  const { rootId, version, onSignedOut } = catalog;
  const { page, error, retry } = useInventoryPage<Series>(`${ROOTS_URL}/${rootId}/series`, version, onSignedOut, { seriesKey });
  const series = page?.items[0];
  const { details, error: detailsError } = useSeriesDetails({ rootId, seriesKey, version, onSignedOut });
  const metadata = details?.metadata ?? null;
  const reading = details?.reading;
  const title = metadata?.title ?? series?.title ?? 'Series';
  const seriesUrl = `${ROOTS_URL}/${rootId}/series/${seriesKey}`;
  const seriesTools = { url: seriesUrl, version, onSignedOut, onChange: onDataChanged };
  const view = selectedView ?? (details?.chapters.length ? 'chapters' : details?.volumes.length ? 'volumes' :
    metadata?.totalChapters != null ? 'chapters' : metadata?.totalVolumes != null ? 'volumes' : 'files');
  return (
    <section className="workspace-page" aria-labelledby="series-heading">
      <ActionLink href="#library" variant="ghost" icon={ArrowLeft} className="justify-self-start">Back to library</ActionLink>
      <div className="surface grid min-w-0 gap-4 bg-linear-to-br from-nav-active/45 to-panel p-4 sm:grid-cols-[auto_minmax(0,1fr)] sm:items-center sm:gap-6 sm:p-6">
        {series?.hasCover && <Button variant="ghost" className="w-24 justify-self-start p-0 sm:w-37" aria-label="Enlarge cover" onClick={() => setDialog('cover')}>
          <SeriesCover rootId={rootId} seriesKey={seriesKey} available revision={coverVersion} className="aspect-[2/3] w-full rounded-lg" />
        </Button>}
        <div className="grid min-w-0 gap-3">
          <PageHeader id="series-heading" section="Library" title={title} />
          {series && <p className="m-0 text-sm text-muted">{series.fileCount.toLocaleString()} {series.fileCount === 1 ? 'file' : 'files'}
            {metadata && ` · ${metadata.kind === 'doujin' ? 'Doujin' : 'Manga'}`}</p>}
          {metadata?.summary && <p className="m-0 line-clamp-3 max-w-prose text-sm text-detail">{metadata.summary}</p>}
          {series && <div className="action-row">
            {reading && <ActionLink icon={BookOpen} href={seriesReaderHref(reading.contentId, rootId, seriesKey)}>
              {reading.pageIndex !== null ? 'Continue reading' : reading.hasFinishedFile ? 'Read next file' : 'Start reading'}
            </ActionLink>}
            <Button variant="secondary" icon={Info} onClick={() => setDialog('info')}>More info</Button>
            <MetadataDialogAction rootId={rootId} seriesKey={seriesKey} title={title} metadata={metadata}
              onChange={onDataChanged} onSignedOut={onSignedOut} />
            <ActionLink variant="ghost" href={duplicateToolsHref({ rootId, seriesKey })}>Review duplicates in Tools</ActionLink>
          </div>}
          {reading?.pageIndex != null && <p className="m-0 text-sm text-muted wrap-anywhere">
            Page {reading.pageIndex + 1} of {reading.pageCount} · {reading.relativePath.split('/').at(-1)}</p>}
        </div>
      </div>
      {error && <InventoryError retry={retry}>Could not load this series.</InventoryError>}
      {!error && !page && <p role="status">Loading series…</p>}
      {page && !series && <p>This series is no longer in the selected library folder.</p>}
      {series && detailsError && <p role="alert" className="m-0 text-danger">{detailsError}</p>}
      {series && details && <section aria-label="Reading and collection progress" className="surface grid gap-4 p-4 sm:grid-cols-3">
        <CollectionTotal label="Read chapters · indexed" value={details.readChapters} total={details.availableChapters} />
        <CollectionTotal label="Indexed chapters · released" value={details.availableChapters} total={metadata?.totalChapters ?? null} />
        <CollectionTotal label="Indexed volumes · released" value={details.indexedVolumes} total={metadata?.totalVolumes ?? null} />
      </section>}
      {series && <EditionCoverage {...seriesTools} editable={false}
        onBrowse={number => { setVolume(number); setView('chapters'); }} />}
      {series && <section className="grid min-w-0 gap-3" aria-label="Series inventory">
        <SegmentedControl label="Series views" options={seriesViews} value={view} onChange={value => { setVolume(null); setView(value); }} />
        <div className="surface min-w-0">
          {view === 'files' && <>
            <div className="action-row p-3"><ActionLink variant="ghost" href={organizeToolsHref(rootId, seriesKey)}>Organize series in Tools</ActionLink></div>
            {volume && <div className="action-row p-3"><span>Volume {volume}</span><Button variant="ghost" onClick={() => setVolume(null)}>Show all files</Button></div>}
            <FileInventory key={volume ?? 'all-files'} {...catalog} seriesKey={seriesKey} volume={volume ?? undefined} />
          </>}
          {view === 'chapters' && <>
            {volume && <div className="action-row p-3"><span>Volume {volume}</span><Button variant="ghost" onClick={() => setVolume(null)}>Show all chapters</Button></div>}
            <SeriesChapterList {...seriesTools} rootId={rootId} seriesKey={seriesKey}
              chapters={details?.chapters.filter(chapter => !volume || chapter.volume === volume) ?? []} />
            {details && details.availableChapters > details.chapters.length && <p className="m-0 p-4 text-sm text-muted">Showing the first {details.chapters.length.toLocaleString()} chapters. All indexed files are in Files.</p>}
          </>}
          {view === 'volumes' && (details?.volumes.length ? <ul aria-label="Indexed volumes" role="list" className="m-0 list-none divide-y divide-line p-0">
            {details.volumes.map(number => <li key={number} className="flex flex-wrap items-center justify-between gap-2 px-4 py-2">
              <strong>Volume {number}</strong>
              <div className="action-row">
                {details.readableVolumes.includes(number) &&
                  <Button variant="secondary" icon={BookOpen} onClick={() => setReadingVolume(number)}>Read volume</Button>}
                {details.chapters.some(chapter => chapter.volume === number) &&
                  <Button variant="ghost" onClick={() => { setVolume(number); setView('chapters'); }}>View chapters</Button>}
                <Button variant="ghost" onClick={() => { setVolume(number); setView('files'); }}>View files</Button>
              </div>
            </li>)}
          </ul> : <p className="m-0 p-4 text-sm text-muted">No indexed volumes for this series.</p>)}
        </div>
      </section>}
      {readingVolume && <VolumeReleasePicker {...seriesTools} rootId={rootId} seriesKey={seriesKey} volume={readingVolume} onClose={() => setReadingVolume(null)} />}
      {series && <SeriesCollectionMembership rootId={rootId} seriesKey={seriesKey} onSignedOut={onSignedOut} />}
      {dialog === 'info' && <Modal title="More info" onClose={() => setDialog(null)}>
        <div className="grid gap-4">
          <strong>{title}</strong>
          {metadata ? <MetadataDescription metadata={metadata} /> : <p className="m-0 text-sm text-muted">No metadata has been assigned.</p>}
          {rootPath && <p className="m-0 text-sm wrap-anywhere">Library folder <code>{rootPath}</code></p>}
        </div>
      </Modal>}
      {dialog === 'cover' && <Modal title={title} onClose={() => setDialog(null)}>
        <SeriesCover rootId={rootId} seriesKey={seriesKey} available revision={coverVersion}
          className="rounded-lg [&_img]:max-h-[70dvh] [&_img]:object-contain" />
      </Modal>}
    </section>
  );
}

function GroupedInventory({ scanComplete, onInspect, coverVersion, coverGrid, search, sort, coverOnly, rules, ...inventory }: InventoryActions & {
  scanComplete: boolean; onInspect?: Inspect; coverVersion: string; coverGrid: boolean; search: string; sort: SeriesSort; coverOnly: boolean; rules: TagRules;
}) {
  const { rootId, version, onSignedOut } = inventory;
  const filters = new URLSearchParams({ search, sort, coverOnly: String(coverOnly) });
  rules.includeTags.forEach(tag => filters.append('includeTag', tag));
  rules.excludeTags.forEach(tag => filters.append('excludeTag', tag));
  const { page, offset, setOffset, error, retry } = useInventoryPage<Series>(`${ROOTS_URL}/${rootId}/series`, version, onSignedOut, filters);

  return (
    <>
      {error && <InventoryError retry={retry}>Could not load the inventory.</InventoryError>}
      {!page && !error && <p role="status">Loading inventory…</p>}
      {page?.total === 0 && (search || coverOnly || rules.includeTags.length || rules.excludeTags.length ? <p>No series match these filters.</p> : scanComplete && <p>No supported files found.</p>)}
      {page && page.total > 0 && (
        <>
          {coverGrid ? <CoverGrid series={page.items} coverVersion={coverVersion} /> : (
            <ul className="m-0 grid list-none gap-2 p-0" role="list" aria-label="Series groups">
              {page.items.map(series => <SeriesRow key={series.key} series={series} onInspect={onInspect} coverVersion={coverVersion} {...inventory} />)}
            </ul>
          )}
          <PageControls label="Series pages" offset={offset} count={page.items.length} total={page.total} onChange={setOffset} />
        </>
      )}
    </>
  );
}

function IntakeInspector({ item, rootPath, onClose }: { item: Inspection | null; rootPath: string; onClose: () => void }) {
  const placement = item ? 'fixed inset-x-3 bottom-[calc(5.5rem+env(safe-area-inset-bottom))] z-10 max-h-[50svh] overflow-auto shadow-xl desktop:bottom-4 min-[72rem]:sticky min-[72rem]:inset-x-auto min-[72rem]:top-5 min-[72rem]:bottom-auto min-[72rem]:max-h-none min-[72rem]:shadow-none' : '';
  return (
    <aside className={`surface grid min-w-0 content-start gap-3 p-4 ${placement}`} aria-labelledby="intake-inspector-heading">
      <div className="flex items-center justify-between gap-2">
        <SectionHeader id="intake-inspector-heading" title="Intake details" />
        {item && <Button variant="ghost" icon={X} aria-label="Close intake details" onClick={onClose} className="min-[72rem]:hidden" />}
      </div>
      <dl className="m-0 grid gap-3 text-sm">
        <div>
          <dt className="text-muted">Source folder</dt>
          <dd><code>{rootPath}</code></dd>
        </div>
        {item?.kind === 'series' && (
          <div>
            <dt className={item.series.hasMetadata ? 'text-positive' : 'text-warning'}>
              {item.series.hasMetadata ? 'Series metadata' : item.series.suggested ? 'Suggested series' : 'Series unresolved'}</dt>
            <dd className="font-semibold">{item.series.title}</dd>
          </div>
        )}
        {item?.kind === 'file' && (
          <>
            <div><dt className="text-muted">File</dt><dd><code>{item.file.relativePath}</code></dd></div>
            <div><dt className="text-muted">Format and size</dt><dd>{item.file.extension.toUpperCase()} · {formatSize(item.file.size)}</dd></div>
            <div><dt className="text-muted">SHA-256</dt><dd>{item.file.sha256 ? <code>{item.file.sha256}</code> : 'Unavailable'}</dd></div>
          </>
        )}
      </dl>
      {item ? <p className="m-0 border-t border-line pt-3 text-sm text-muted">Chapter association is pending.</p> :
        <p className="m-0 border-t border-line pt-3 text-sm text-muted">Open a group or select a file to inspect it.</p>}
    </aside>
  );
}

export function CatalogInventory({ scanComplete, intake, rootPath, hasCovers, coverVersion, managedFolders, onInventoryChanged, ...inventory }:
  InventoryProps & { scanComplete: boolean; intake: boolean; rootPath: string; hasCovers: boolean; coverVersion: string;
    managedFolders: LibraryRoot[]; onInventoryChanged: () => void }) {
  const [selectedView, setView] = useState<InventoryView | null>(null);
  const [item, setItem] = useState<Inspection | null>(null);
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [sort, setSort] = useState<SeriesSort>('title');
  const [coverOnly, setCoverOnly] = useState(false);
  const [rules, setRules] = useState<TagRules>(emptyFilters);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [saveFilters, setSaveFilters] = useState(false);
  const { tags, error: tagsError } = useTagNames(filtersOpen ? `${ROOTS_URL}/${inventory.rootId}/tags` : '', inventory.version, inventory.onSignedOut);
  const activeRules = rules.includeTags.length + rules.excludeTags.length;
  const [catalogRevision, setCatalogRevision] = useState(0);
  const [notice, setNotice] = useState('');
  const catalog = { ...inventory, version: `${inventory.version}:${catalogRevision}`, managedFolders,
    onDataChanged: () => setCatalogRevision(value => value + 1), onMoved: () => {
      setNotice('File change completed. A verified recovery copy is in Organize under File operation history.');
      onInventoryChanged();
    } };
  const onInspect = intake ? setItem : undefined;
  const coverView = hasCovers && !intake;
  const view = selectedView && (selectedView !== 'covers' || coverView) ? selectedView : coverView ? 'covers' : 'series';
  const options: { value: InventoryView; label: string }[] = [];
  if (coverView) options.push({ value: 'covers', label: 'Covers' });
  options.push({ value: 'series', label: 'Series' }, { value: 'files', label: 'Files' });

  useEffect(() => setItem(null), [inventory.version, intake]);
  useEffect(() => {
    const pending = window.setTimeout(() => setQuery(search.trim()), 180);
    return () => window.clearTimeout(pending);
  }, [search]);

  return (
    <section className="grid min-w-0 gap-3" aria-labelledby="inventory-heading">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <SectionHeader id="inventory-heading" title={intake ? 'Incoming inventory' : 'Inventory'} />
        <SegmentedControl label="Inventory view" value={view} options={options}
          onChange={view => { setView(view); setItem(null); }} />
      </div>
      {notice && <p role="status" className="m-0 text-sm text-positive">{notice}</p>}
      <div className="flex flex-wrap items-end gap-3">
        <SearchField className="flex-[1_1_13rem]" placeholder={view === 'files' ? 'Search files' : 'Search series'}
          aria-label={view === 'files' ? 'Search files' : 'Search series'} maxLength={160}
          value={search} onChange={event => setSearch(event.target.value)} />
        {view !== 'files' && <>
          <Select label="Sort series" value={sort} options={sortOptions} onChange={setSort} className="w-46 max-w-full" />
          {hasCovers && <Select label="Artwork" value={coverOnly ? 'only' : 'all'} className="w-40 max-w-full"
            options={[{ value: 'all', label: 'All series' }, { value: 'only', label: 'With cover' }]} onChange={option => setCoverOnly(option === 'only')} />}
        </>}
      </div>
      {view !== 'files' && <details className="surface p-3" onToggle={event => setFiltersOpen(event.currentTarget.open)}>
        <summary className="min-h-11 cursor-pointer py-2 text-sm font-semibold">Filter by tag{activeRules ? ` (${activeRules})` : ''}</summary>
        <div className="grid gap-3">
          {tagsError && <p role="alert" className="m-0 text-danger">Could not load saved tags.</p>}
          <TagRulesFields tags={tags} rules={rules} onChange={setRules} />
        </div>
      </details>}
      {!intake && view !== 'files' && (activeRules > 0 || query || coverOnly) &&
        <Button variant="secondary" className="justify-self-start" onClick={() => setSaveFilters(true)}>Save filters as collection</Button>}
      {saveFilters && <Modal title="Save filters as collection" onClose={() => setSaveFilters(false)}>
        <CollectionForm preset={{ ...rules, search: query, coverOnly: coverOnly && hasCovers }} tags={tags} onSignedOut={inventory.onSignedOut}
          onCancel={() => setSaveFilters(false)} onSaved={collection => { setSaveFilters(false); setNotice(`Created collection “${collection.name}”.`); }} />
      </Modal>}
      <div className={intake ? 'grid min-w-0 items-start gap-4 min-[72rem]:grid-cols-[minmax(0,1fr)_minmax(16rem,22rem)]' : undefined}>
        <div className="grid min-w-0 gap-3">
          {view === 'files' ? <FileInventory key={query} {...catalog} onInspect={onInspect} management={intake ? catalog : undefined} search={query} /> :
            <GroupedInventory key={JSON.stringify([view, query, sort, coverOnly, rules.includeTags, rules.excludeTags])} {...catalog} scanComplete={scanComplete} onInspect={onInspect}
              coverVersion={coverVersion} coverGrid={view === 'covers'} search={query} sort={sort} coverOnly={coverOnly && hasCovers} rules={rules} />}
        </div>
        {intake && <IntakeInspector rootPath={rootPath} item={item} onClose={() => setItem(null)} />}
      </div>
    </section>
  );
}
