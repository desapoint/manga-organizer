import { useEffect, useState } from 'react';
import { getCsrfToken } from './api';

const ROOTS_URL = '/api/library/roots';
const PAGE_SIZE = 50;
const SCAN_LABELS = {
  idle: 'Ready to scan.',
  queued: 'Scan queued.',
  scanning: 'Scanning…',
  complete: 'Scan complete.',
  unavailable: 'Folder unavailable. Any previous inventory is still shown.',
  incomplete: 'Scan could not finish. Any previous inventory is still shown.',
  emptyReview: 'This scan found no supported files. Check the folder or mount. The previous inventory is still shown.',
  interrupted: 'Scan interrupted. Any previous inventory is still shown.',
};

type Root = {
  id: string;
  path: string;
  state: keyof typeof SCAN_LABELS;
  fileCount: number;
  lastScannedAt: string | null;
  skippedLinks: number;
};
type CatalogEntry = {
  id: string;
  relativePath: string;
  extension: string;
  size: number;
  modifiedAt: string;
};
type Page = { total: number; items: CatalogEntry[] };

function scanIsActive(root: Root) {
  return root.state === 'queued' || root.state === 'scanning';
}

function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KiB`;
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MiB`;
  return `${(bytes / 1024 ** 3).toFixed(1)} GiB`;
}

export function Catalog({ onSignedOut }: { onSignedOut: () => void }) {
  const [roots, setRoots] = useState<Root[] | null>(null);
  const [selectedId, setSelectedId] = useState('');
  const [offset, setOffset] = useState(0);
  const [inventory, setInventory] = useState<(Page & { rootId: string; offset: number }) | null>(null);
  const [refresh, setRefresh] = useState(0);
  const [scanPending, setScanPending] = useState(false);
  const [rootsError, setRootsError] = useState('');
  const [filesError, setFilesError] = useState('');
  const [scanError, setScanError] = useState('');

  const selected = roots?.find(root => root.id === selectedId) ?? roots?.[0];
  const rootId = selected?.id;
  const lastScannedAt = selected?.lastScannedAt;
  const page = inventory?.rootId === rootId && inventory?.offset === offset ? inventory : null;

  useEffect(() => {
    const request = new AbortController();
    let timer: number | undefined;

    async function loadRoots() {
      try {
        const response = await fetch(ROOTS_URL, { signal: request.signal });
        if (response.status === 401) return onSignedOut();
        if (!response.ok) throw new Error();
        const available = await response.json() as Root[];
        if (request.signal.aborted) return;
        setRoots(available);
        setRootsError('');
        timer = window.setTimeout(loadRoots, available.some(scanIsActive) ? 900 : 4500);
      } catch {
        if (request.signal.aborted) return;
        setRootsError('Could not load library folders. Retrying…');
        timer = window.setTimeout(loadRoots, 4500);
      }
    }

    void loadRoots();
    return () => {
      request.abort();
      window.clearTimeout(timer);
    };
  }, [refresh, onSignedOut]);

  useEffect(() => {
    if (!rootId) return;
    const request = new AbortController();
    setFilesError('');

    async function loadFiles() {
      const response = await fetch(`${ROOTS_URL}/${rootId}/files?offset=${offset}&limit=${PAGE_SIZE}`, { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const result = await response.json() as Page;
      if (request.signal.aborted || !rootId) return;
      if (offset > 0 && offset >= result.total) {
        setOffset(Math.max(0, Math.floor((result.total - 1) / PAGE_SIZE) * PAGE_SIZE));
      } else {
        setInventory({ ...result, rootId, offset });
      }
    }

    loadFiles().catch(() => {
      if (!request.signal.aborted) setFilesError('Could not load the inventory.');
    });
    return () => request.abort();
  }, [rootId, lastScannedAt, offset, refresh, onSignedOut]);

  async function startScan() {
    if (!rootId) return;
    setScanPending(true);
    setScanError('');
    try {
      const response = await fetch(`${ROOTS_URL}/${rootId}/scan`, {
        method: 'POST', headers: { 'X-CSRF-Token': await getCsrfToken() },
      });
      if (response.status === 401) return onSignedOut();
      if (!response.ok && response.status !== 409) throw new Error();
      setRefresh(value => value + 1);
    } catch {
      setScanError('Could not start the scan. Try again.');
    } finally {
      setScanPending(false);
    }
  }

  return (
    <section className="panel" aria-labelledby="catalog-heading">
      <h1 id="catalog-heading">Library catalog</h1>
      {rootsError && <p role="alert" className="error">{rootsError}</p>}
      {!roots && !rootsError && <p role="status">Loading library folders…</p>}
      {roots?.length === 0 && <p>Set <code>MANGA_ORGANIZER_LIBRARY_ROOTS__0</code> to a library folder on the server and restart Manga Organizer.</p>}
      {selected && (
        <>
          <div className="catalog-controls">
            <label>
              Library folder
              <select value={selected.id} onChange={event => {
                setSelectedId(event.target.value);
                setOffset(0);
                setScanError('');
              }}>
                {roots?.map(root => <option key={root.id} value={root.id}>{root.path}</option>)}
              </select>
            </label>
            <button type="button" onClick={startScan} disabled={scanPending || scanIsActive(selected)}>Scan folder</button>
          </div>
          <p className="scan-state" role="status">{SCAN_LABELS[selected.state]}</p>
          <div className="catalog-summary">
            <span>{selected.fileCount.toLocaleString()} {selected.fileCount === 1 ? 'file' : 'files'}</span>
            {selected.lastScannedAt && <span>Last indexed {new Date(selected.lastScannedAt).toLocaleString()}</span>}
            {selected.skippedLinks > 0 && <span>{selected.skippedLinks.toLocaleString()} linked {selected.skippedLinks === 1 ? 'path' : 'paths'} skipped</span>}
          </div>
          {scanError && <p role="alert" className="error">{scanError}</p>}
          {filesError && (
            <p role="alert" className="error">
              {filesError} <button type="button" className="secondary" onClick={() => setRefresh(value => value + 1)}>Retry</button>
            </p>
          )}
          {!page && !filesError && <p role="status">Loading inventory…</p>}
          {page && page.total > 0 && (
            <>
              <ul className="catalog-files" aria-label="Catalog files">
                {page.items.map(file => (
                  <li key={file.id}>
                    <span className="file-path">{file.relativePath}</span>
                    <span className="file-format">{file.extension.toUpperCase()}</span>
                    <span>{formatSize(file.size)}</span>
                    <time dateTime={file.modifiedAt}>{new Date(file.modifiedAt).toLocaleDateString()}</time>
                  </li>
                ))}
              </ul>
              {page.total > PAGE_SIZE && (
                <nav className="pagination" aria-label="Inventory pages">
                  <button type="button" className="secondary" disabled={offset === 0} onClick={() => setOffset(offset - PAGE_SIZE)}>Previous</button>
                  <span>{offset + 1}–{offset + page.items.length} of {page.total.toLocaleString()}</span>
                  <button type="button" className="secondary" disabled={offset + PAGE_SIZE >= page.total} onClick={() => setOffset(offset + PAGE_SIZE)}>Next</button>
                </nav>
              )}
            </>
          )}
          {page?.total === 0 && selected.state === 'complete' && <p>No supported files found.</p>}
        </>
      )}
    </section>
  );
}
