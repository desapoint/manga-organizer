import { ArrowLeft, BookOpen, ChevronRight, Library, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { formatSize, ROOTS_URL, seriesHref } from './library';
import { Select } from './Select';
import { ActionLink, Button, PageHeader, SearchField, SectionHeader, SegmentedControl } from './ui';

const PAGE_SIZE = 50;

type CatalogEntry = {
  id: string;
  relativePath: string;
  extension: string;
  size: number;
  modifiedAt: string;
  sha256: string | null;
  indexedLocations: number | null;
  hasCover: boolean;
  seriesCoverKey: string | null;
};

type Series = { key: string; title: string; fileCount: number; suggested: boolean; hasCover: boolean };
type Page<T> = { total: number; items: T[] };
type InventoryProps = { rootId: string; version: string; onSignedOut: () => void };
type Inspection = { kind: 'series'; series: Series } | { kind: 'file'; file: CatalogEntry };
type Inspect = (item: Inspection) => void;
type InventoryView = 'covers' | 'series' | 'files';
type SeriesSort = 'title' | 'recent' | 'files';

const sortOptions = [
  { value: 'title', label: 'Title' },
  { value: 'recent', label: 'Recently modified' },
  { value: 'files', label: 'Most files' },
] as const;

function useInventoryPage<T>(url: string, version: string, onSignedOut: () => void, query?: Record<string, string>) {
  const [offset, setOffset] = useState(0);
  const [stored, setStored] = useState<{ url: string; page: Page<T> } | null>(null);
  const [error, setError] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const params = new URLSearchParams({ ...query, offset: String(offset), limit: String(PAGE_SIZE) });
  const pageUrl = `${url}?${params}`;

  useEffect(() => {
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

function PageControls({ label, offset, count, total, onChange }: {
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

function FileRow({ file, rootId, version, onInspect }: Pick<InventoryProps, 'rootId' | 'version'> & { file: CatalogEntry; onInspect?: Inspect }) {
  const separator = file.relativePath.lastIndexOf('/');
  const name = file.relativePath.slice(separator + 1);
  const art = file.hasCover ? coverUrl(rootId, 'files', file.id, version) :
    file.seriesCoverKey ? coverUrl(rootId, 'series', file.seriesCoverKey, version) : undefined;
  return (
    <li>
      <span className="flex min-w-0 items-center gap-3">
        <CoverImage url={art} title={file.hasCover ? 'File cover' : file.seriesCoverKey ? 'Series cover' : 'No cover'}
          className="h-12 w-8 shrink-0 rounded-sm" />
        <span className="file-path" title={file.relativePath}>
          {onInspect ? <button type="button" className="min-h-11 cursor-pointer rounded-sm border-0 bg-transparent p-0 text-start
            text-inherit wrap-anywhere hover:text-accent-readable hover:underline desktop:min-h-0"
            onClick={() => onInspect({ kind: 'file', file })}>{name}</button> : name}
          {separator >= 0 && <small>{file.relativePath.slice(0, separator)}</small>}
          {file.indexedLocations === null && <small>Content identity unavailable</small>}
          {file.indexedLocations !== null && file.indexedLocations > 1 && (
            <small>Same bytes at {file.indexedLocations.toLocaleString()} indexed locations</small>
          )}
        </span>
      </span>
      <span className="file-format">{file.extension.toUpperCase()}</span>
      <span>{formatSize(file.size)}</span>
      <time dateTime={file.modifiedAt}>{new Date(file.modifiedAt).toLocaleDateString()}</time>
    </li>
  );
}

function FileInventory({ rootId, seriesKey, version, onSignedOut, onInspect }: InventoryProps & { seriesKey?: string; onInspect?: Inspect }) {
  const { page, offset, setOffset, error, retry } = useInventoryPage<CatalogEntry>(`${ROOTS_URL}/${rootId}/files`, version, onSignedOut,
    seriesKey ? { seriesKey } : undefined);
  const label = seriesKey ? 'Series files' : 'Catalog files';
  return (
    <>
      {error && <div className="px-4 pb-3"><InventoryError retry={retry}>Could not load files.</InventoryError></div>}
      {!page && !error && <p className="px-4" role="status">Loading files…</p>}
      {page?.total === 0 && <p className="px-4">No files in the current inventory.</p>}
      {page && page.total > 0 && (
        <div className={seriesKey ? 'min-w-0 rounded-b-xl border-t border-line bg-inset' : 'surface'}>
          <div className="catalog-columns" aria-hidden="true"><span>File</span><span>Type</span><span>Size</span><span>Modified</span></div>
          <ul className="catalog-files" role="list" aria-label={label}>
            {page.items.map(file => <FileRow key={file.id} file={file} rootId={rootId} version={version} onInspect={onInspect} />)}
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

function coverUrl(rootId: string, collection: 'series' | 'files', id: string, revision: string) {
  return `${ROOTS_URL}/${rootId}/${collection}/${id}/cover?revision=${encodeURIComponent(revision)}`;
}

function CoverImage({ url, className, title }: { url?: string; className?: string; title?: string }) {
  const [failed, setFailed] = useState('');
  return (
    <span title={title} className={`grid place-items-center overflow-hidden bg-nav-active text-nav-icon ${className ?? ''}`}>
      {url && failed !== url ? <img src={url} alt="" loading="lazy" decoding="async" onError={() => setFailed(url)}
        className="h-full w-full object-cover transition-transform duration-200 group-hover:scale-[1.025] motion-reduce:transform-none" /> :
        <BookOpen className="size-6" aria-hidden="true" />}
    </span>
  );
}

function SeriesRow({ series, onInspect, coverVersion, ...inventory }: InventoryProps & { series: Series; onInspect?: Inspect; coverVersion: string }) {
  const [open, setOpen] = useState(false);
  const rowClass = 'grid cursor-pointer list-none grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2 rounded-xl p-3 hover:bg-nav-hover sm:gap-3 sm:px-4';
  const contents = <>
    {series.hasCover ? <CoverImage url={coverUrl(inventory.rootId, 'series', series.key, coverVersion)} className="h-14 w-10 rounded-md" /> :
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
        {open && <FileInventory {...inventory} seriesKey={series.key} onInspect={onInspect} />}
      </details> : <a className={`${rowClass} text-ink no-underline`} href={seriesHref(inventory.rootId, series.key)}>{contents}</a>}
    </li>
  );
}

function CoverGrid({ series, rootId, coverVersion }: { series: Series[]; rootId: string; coverVersion: string }) {
  return (
    <ul className="m-0 grid list-none grid-cols-[repeat(auto-fill,minmax(min(100%,8rem),1fr))] gap-4 p-0 sm:gap-5" role="list" aria-label="Series covers">
      {series.map(item => (
        <li key={item.key} className="min-w-0">
          <a href={seriesHref(rootId, item.key)} className="group grid gap-2 rounded-xl border border-transparent p-1 text-start text-ink no-underline hover:bg-panel">
            <CoverImage url={item.hasCover ? coverUrl(rootId, 'series', item.key, coverVersion) : undefined}
              className="aspect-[2/3] rounded-lg" />
            <span className="line-clamp-2 min-h-10 px-1 text-sm font-semibold wrap-anywhere" title={item.title}>{item.title}</span>
            <span className="px-1 pb-1 text-xs text-muted">{item.fileCount.toLocaleString()} {item.fileCount === 1 ? 'file' : 'files'}</span>
          </a>
        </li>
      ))}
    </ul>
  );
}

export function SeriesPage({ seriesKey, coverVersion, ...inventory }: InventoryProps & { seriesKey: string; coverVersion: string }) {
  const { rootId, version, onSignedOut } = inventory;
  const { page, error, retry } = useInventoryPage<Series>(`${ROOTS_URL}/${rootId}/series`, version, onSignedOut, { seriesKey });
  const series = page?.items[0];
  return (
    <section className="workspace-page" aria-labelledby="series-heading">
      <ActionLink href="#library" variant="ghost" icon={ArrowLeft} className="justify-self-start">Back to library</ActionLink>
      <div className="flex min-w-0 flex-wrap items-end gap-4 sm:gap-6">
        {series?.hasCover && <CoverImage url={coverUrl(rootId, 'series', seriesKey, coverVersion)}
          className="aspect-[2/3] w-24 shrink-0 rounded-xl sm:w-36" />}
        <div className="min-w-0 flex-1">
          <PageHeader id="series-heading" section="Library" title={series?.title ?? 'Series'} />
          {series && <p className="text-sm text-muted">{series.fileCount.toLocaleString()} {series.fileCount === 1 ? 'file' : 'files'}</p>}
        </div>
      </div>
      {error && <InventoryError retry={retry}>Could not load this series.</InventoryError>}
      {!error && !page && <p role="status">Loading series…</p>}
      {page && !series && <p>This series is no longer in the selected library folder.</p>}
      {series && <section className="grid gap-3" aria-labelledby="series-files-heading">
        <SectionHeader id="series-files-heading" title="Files" />
        <FileInventory {...inventory} seriesKey={seriesKey} />
      </section>}
    </section>
  );
}

function GroupedInventory({ scanComplete, onInspect, coverVersion, coverGrid, search, sort, coverOnly, ...inventory }: InventoryProps & {
  scanComplete: boolean; onInspect?: Inspect; coverVersion: string; coverGrid: boolean; search: string; sort: SeriesSort; coverOnly: boolean;
}) {
  const { rootId, version, onSignedOut } = inventory;
  const { page, offset, setOffset, error, retry } = useInventoryPage<Series>(`${ROOTS_URL}/${rootId}/series`, version, onSignedOut,
    { search, sort, coverOnly: String(coverOnly) });

  return (
    <>
      {error && <InventoryError retry={retry}>Could not load the inventory.</InventoryError>}
      {!page && !error && <p role="status">Loading inventory…</p>}
      {page?.total === 0 && (search || coverOnly ? <p>No series match these filters.</p> : scanComplete && <p>No supported files found.</p>)}
      {page && page.total > 0 && (
        <>
          {coverGrid ? <CoverGrid series={page.items} rootId={rootId} coverVersion={coverVersion} /> : (
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
            <dt className="text-warning">{item.series.suggested ? 'Suggested series' : 'Series unresolved'}</dt>
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

export function CatalogInventory({ scanComplete, intake, rootPath, hasCovers, coverVersion, ...inventory }:
  InventoryProps & { scanComplete: boolean; intake: boolean; rootPath: string; hasCovers: boolean; coverVersion: string }) {
  const [selectedView, setView] = useState<InventoryView | null>(null);
  const [item, setItem] = useState<Inspection | null>(null);
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [sort, setSort] = useState<SeriesSort>('title');
  const [coverOnly, setCoverOnly] = useState(false);
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
      {view !== 'files' && <div className="flex flex-wrap items-end gap-3">
        <SearchField className="flex-[1_1_13rem]" placeholder="Search series" aria-label="Search series" maxLength={160}
          value={search} onChange={event => setSearch(event.target.value)} />
        <Select label="Sort series" value={sort} options={sortOptions} onChange={setSort} className="w-46 max-w-full" />
        {hasCovers && <Select label="Artwork" value={coverOnly ? 'only' : 'all'} className="w-40 max-w-full"
          options={[{ value: 'all', label: 'All series' }, { value: 'only', label: 'With cover' }]} onChange={option => setCoverOnly(option === 'only')} />}
      </div>}
      <div className={intake ? 'grid min-w-0 items-start gap-4 min-[72rem]:grid-cols-[minmax(0,1fr)_minmax(16rem,22rem)]' : undefined}>
        <div className="grid min-w-0 gap-3">
          {view === 'files' ? <FileInventory {...inventory} onInspect={onInspect} /> :
            <GroupedInventory key={`${view}:${query}:${sort}:${coverOnly}`} {...inventory} scanComplete={scanComplete} onInspect={onInspect}
              coverVersion={coverVersion} coverGrid={view === 'covers'} search={query} sort={sort} coverOnly={coverOnly && hasCovers} />}
        </div>
        {intake && <IntakeInspector rootPath={rootPath} item={item} onClose={() => setItem(null)} />}
      </div>
    </section>
  );
}
