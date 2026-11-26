import { ChevronRight, Library } from 'lucide-react';
import { useEffect, useState } from 'react';
import { ROOTS_URL } from './LibrarySettings';
import { Button } from './ui';

const PAGE_SIZE = 50;

type CatalogEntry = {
  id: string;
  relativePath: string;
  extension: string;
  size: number;
  modifiedAt: string;
  indexedLocations: number | null;
};

type Series = { key: string; title: string; fileCount: number };
type Page<T> = { total: number; items: T[] };
type InventoryProps = { rootId: string; version: string; onSignedOut: () => void };

function useInventoryPage<T>(url: string, version: string, onSignedOut: () => void, seriesKey?: string) {
  const [offset, setOffset] = useState(0);
  const [stored, setStored] = useState<{ url: string; page: Page<T> } | null>(null);
  const [error, setError] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const params = new URLSearchParams({ offset: String(offset), limit: String(PAGE_SIZE) });
  if (seriesKey) params.set('seriesKey', seriesKey);
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

function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KiB`;
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MiB`;
  return `${(bytes / 1024 ** 3).toFixed(1)} GiB`;
}

function FileRow({ file }: { file: CatalogEntry }) {
  const separator = file.relativePath.lastIndexOf('/');
  return (
    <li>
      <span className="file-path" title={file.relativePath}>
        {file.relativePath.slice(separator + 1)}
        {separator >= 0 && <small>{file.relativePath.slice(0, separator)}</small>}
        {file.indexedLocations === null && <small>Content identity unavailable</small>}
        {file.indexedLocations !== null && file.indexedLocations > 1 && (
          <small>Same bytes at {file.indexedLocations.toLocaleString()} indexed locations</small>
        )}
      </span>
      <span className="file-format">{file.extension.toUpperCase()}</span>
      <span>{formatSize(file.size)}</span>
      <time dateTime={file.modifiedAt}>{new Date(file.modifiedAt).toLocaleDateString()}</time>
    </li>
  );
}

function SeriesFiles({ rootId, seriesKey, version, onSignedOut }: InventoryProps & { seriesKey: string }) {
  const { page, offset, setOffset, error, retry } = useInventoryPage<CatalogEntry>(`${ROOTS_URL}/${rootId}/files`, version, onSignedOut, seriesKey);
  return (
    <>
      {error && <div className="px-4 pb-3"><InventoryError retry={retry}>Could not load series files.</InventoryError></div>}
      {!page && !error && <p className="px-4" role="status">Loading series files…</p>}
      {page?.total === 0 && <p className="px-4">This group has no files in the current inventory.</p>}
      {page && page.total > 0 && (
        <div className="min-w-0 border-t border-line bg-inset">
          <div className="catalog-columns" aria-hidden="true"><span>File</span><span>Type</span><span>Size</span><span>Modified</span></div>
          <ul className="catalog-files" role="list" aria-label="Series files">
            {page.items.map(file => <FileRow key={file.id} file={file} />)}
          </ul>
          {page.total > PAGE_SIZE && (
            <div className="border-t border-line p-3">
              <PageControls label="Series files pages" offset={offset} count={page.items.length} total={page.total} onChange={setOffset} />
            </div>
          )}
        </div>
      )}
    </>
  );
}

function SeriesRow({ series, ...inventory }: InventoryProps & { series: Series }) {
  const [open, setOpen] = useState(false);

  return (
    <li className="surface">
      <details className="group" onToggle={event => setOpen(event.currentTarget.open)}>
        <summary className="grid cursor-pointer list-none grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2 rounded-xl p-3
          hover:bg-nav-hover sm:gap-3 sm:px-4 [&::-webkit-details-marker]:hidden">
          <span className="grid size-8 place-items-center rounded-lg bg-nav-active text-nav-icon">
            <Library className="size-4" aria-hidden="true" />
          </span>
          <span className="wrap-anywhere">
            <span className="block font-semibold">{series.title}</span>
            <span className="block text-sm text-muted">{series.fileCount.toLocaleString()} {series.fileCount === 1 ? 'file' : 'files'}</span>
          </span>
          <ChevronRight className="size-4 text-muted group-open:rotate-90" aria-hidden="true" />
        </summary>
        {open && <SeriesFiles {...inventory} seriesKey={series.key} />}
      </details>
    </li>
  );
}

export function CatalogInventory({ scanComplete, ...inventory }: InventoryProps & { scanComplete: boolean }) {
  const { rootId, version, onSignedOut } = inventory;
  const { page, offset, setOffset, error, retry } = useInventoryPage<Series>(`${ROOTS_URL}/${rootId}/series`, version, onSignedOut);

  return (
    <>
      {error && <InventoryError retry={retry}>Could not load the inventory.</InventoryError>}
      {!page && !error && <p role="status">Loading inventory…</p>}
      {page?.total === 0 && scanComplete && <p>No supported files found.</p>}
      {page && page.total > 0 && (
        <>
          <ul className="m-0 grid list-none gap-2 p-0" role="list" aria-label="Series groups">
            {page.items.map(series => <SeriesRow key={series.key} series={series} {...inventory} />)}
          </ul>
          <PageControls label="Series pages" offset={offset} count={page.items.length} total={page.total} onChange={setOffset} />
        </>
      )}
    </>
  );
}
