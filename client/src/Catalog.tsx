import { FolderCog, ScanSearch } from 'lucide-react';
import { useEffect, useState } from 'react';
import { getCsrfToken } from './api';
import { CatalogInventory } from './CatalogInventory';
import { LibrarySettings, ROOTS_URL } from './LibrarySettings';
import type { ConfiguredFolder, FolderRole } from './LibrarySettings';
import { Select } from './Select';
import { ActionLink, Button } from './ui';

export type CatalogView = FolderRole | 'settings';

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

type Root = ConfiguredFolder & {
  state: keyof typeof SCAN_LABELS;
  fileCount: number;
  lastScannedAt: string | null;
  skippedLinks: number;
};

function scanIsActive(root: Root) {
  return root.state === 'queued' || root.state === 'scanning';
}

export function Catalog({ view, title, onSignedOut }: { view: CatalogView; title: string; onSignedOut: () => void }) {
  const [roots, setRoots] = useState<Root[] | null>(null);
  const [selectedId, setSelectedId] = useState('');
  const [refresh, setRefresh] = useState(0);
  const [scanPending, setScanPending] = useState(false);
  const [rootsError, setRootsError] = useState('');
  const [scanError, setScanError] = useState('');

  const visibleRoots = view === 'settings' ? [] : roots?.filter(root => root.kind === view || root.kind === 'both');
  const selected = visibleRoots?.find(root => root.id === selectedId) ?? visibleRoots?.[0];
  const folderOptions = visibleRoots?.map(root => ({ value: root.id, label: root.name || root.path, description: root.name ? root.path : undefined })) ?? [];
  const rootId = selected?.id;
  const inventoryVersion = roots?.map(root => `${root.id}:${root.lastScannedAt}`).join('|');

  useEffect(() => {
    setScanError('');
  }, [view]);

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
    <section className="workspace-page" aria-labelledby="catalog-heading">
      <h1 id="catalog-heading">{title}</h1>
      {rootsError && <p role="alert" className="error">{rootsError}</p>}
      {!roots && !rootsError && <p role="status">Loading library folders…</p>}
      {view === 'settings' && roots && <LibrarySettings folders={roots} onSignedOut={onSignedOut}
        onChange={() => setRefresh(value => value + 1)} />}
      {visibleRoots?.length === 0 && view !== 'settings' && (
        <div className="surface catalog-surface">
          <p>{view === 'import' ? 'Add an intake folder to see files awaiting association.' : 'Add a folder on the server to start building your catalog.'}</p>
          <ActionLink icon={FolderCog} href="#settings">Manage folders</ActionLink>
        </div>
      )}
      {selected && (
        <>
          <div className="surface catalog-surface">
            <div className="catalog-controls">
              <Select className="flex-[1_1_13rem]" label={view === 'import' ? 'Import folder' : 'Library folder'}
                value={selected.id} options={folderOptions} onChange={id => {
                  setSelectedId(id);
                  setScanError('');
                }} />
              <Button icon={ScanSearch} onClick={startScan} disabled={scanPending || scanIsActive(selected)}>Scan folder</Button>
            </div>
            <p className="scan-state" role="status">{SCAN_LABELS[selected.state]}</p>
            <div className="catalog-summary">
              <span>{selected.fileCount.toLocaleString()} {selected.fileCount === 1 ? 'file' : 'files'}</span>
              {selected.lastScannedAt && <span>Last indexed {new Date(selected.lastScannedAt).toLocaleString()}</span>}
              {selected.skippedLinks > 0 && <span>{selected.skippedLinks.toLocaleString()} linked {selected.skippedLinks === 1 ? 'path' : 'paths'} skipped</span>}
            </div>
          </div>
          {scanError && <p role="alert" className="error">{scanError}</p>}
          <CatalogInventory key={selected.id} rootId={selected.id} version={`${inventoryVersion}:${refresh}`}
            onSignedOut={onSignedOut} scanComplete={selected.state === 'complete'} />
        </>
      )}
    </section>
  );
}
