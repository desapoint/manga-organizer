import { FolderCog, ScanSearch } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { getCsrfToken } from './api';
import { CatalogInventory, SeriesPage } from './CatalogInventory';
import { FileOperationHistory } from './FileOperations';
import { ROOTS_URL, scanIsActive } from './library';
import type { FolderRole, LibraryRoot, SeriesLocation } from './library';
import { LibrarySettings } from './LibrarySettings';
import { Overview } from './Overview';
import { ScanStatus } from './ScanStatus';
import { Select } from './Select';
import { ActionLink, Button, PageHeader } from './ui';

export type CatalogView = FolderRole | 'settings' | 'overview';

export function Catalog({ view, title, section, onSignedOut, series, children }: {
  view: CatalogView; title: string; section: string; onSignedOut: () => void; series?: SeriesLocation | null; children?: ReactNode;
}) {
  const [roots, setRoots] = useState<LibraryRoot[] | null>(null);
  const [selectedId, setSelectedId] = useState('');
  const [refresh, setRefresh] = useState(0);
  const [scanPending, setScanPending] = useState(false);
  const [rootsError, setRootsError] = useState('');
  const [scanError, setScanError] = useState('');

  const catalogView = view === 'library' || view === 'import';
  const visibleRoots = catalogView ? roots?.filter(root => root.kind === view || root.kind === 'both') : [];
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
        const available = await response.json() as LibraryRoot[];
        if (request.signal.aborted) return;
        setRoots(available);
        setRootsError('');
        timer = window.setTimeout(loadRoots, available.some(scanIsActive) ? 500 : 4500);
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

  if (series) {
    const seriesRoot = roots?.find(root => root.id === series.rootId);
    return <SeriesPage key={`${series.rootId}/${series.seriesKey}`} {...series} onSignedOut={onSignedOut}
      version={`${inventoryVersion}:${refresh}`} coverVersion={`${seriesRoot?.lastScannedAt ?? ''}:${refresh}`}
      managedFolders={seriesRoot?.managed ? roots?.filter(root => root.managed) ?? [] : []}
      onInventoryChanged={() => setRefresh(value => value + 1)} />;
  }

  return (
    <section className="workspace-page" aria-labelledby="catalog-heading">
      <PageHeader id="catalog-heading" section={section} title={title} />
      {rootsError && <p role="alert" className="error">{rootsError}</p>}
      {!roots && !rootsError && <p role="status">Loading library folders…</p>}
      {view === 'overview' && roots && <Overview roots={roots} onSignedOut={onSignedOut} />}
      {view === 'settings' && roots && (
        <>
          <LibrarySettings folders={roots} onSignedOut={onSignedOut} onChange={() => setRefresh(value => value + 1)} />
          {children}
          <FileOperationHistory onSignedOut={onSignedOut} onChanged={() => setRefresh(value => value + 1)} />
        </>
      )}
      {visibleRoots?.length === 0 && catalogView && (
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
            <ScanStatus root={selected} />
            <div className="catalog-summary">
              <span>{selected.fileCount.toLocaleString()} {selected.fileCount === 1 ? 'file' : 'files'}</span>
              {selected.lastScannedAt && <span>Last indexed {new Date(selected.lastScannedAt).toLocaleString()}</span>}
              {selected.skippedLinks > 0 && <span>{selected.skippedLinks.toLocaleString()} linked {selected.skippedLinks === 1 ? 'path' : 'paths'} skipped</span>}
            </div>
          </div>
          {scanError && <p role="alert" className="error">{scanError}</p>}
          <CatalogInventory key={selected.id} rootId={selected.id} version={`${inventoryVersion}:${refresh}`}
            onSignedOut={onSignedOut} scanComplete={selected.state === 'complete'} intake={view === 'import'} rootPath={selected.path}
            hasCovers={selected.hasCovers} coverVersion={`${selected.lastScannedAt ?? ''}:${refresh}`}
            managedFolders={selected.managed ? roots?.filter(root => root.managed) ?? [] : []}
            onInventoryChanged={() => setRefresh(value => value + 1)} />
        </>
      )}
    </section>
  );
}
