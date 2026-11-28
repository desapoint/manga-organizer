import { FolderCog, Pause, Play, ScanSearch, Square } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { getCsrfToken } from './api';
import { IntakeTransfer } from './BatchOrganizer';
import { CatalogInventory, SeriesPage } from './CatalogInventory';
import { Collections } from './Collections';
import { canTransferTo, hasPendingScanLookups, intakeTargetFor, organizeToolsHref, ROOTS_URL, scanIsActive } from './library';
import type { FolderRole, LibraryRoot, SeriesLocation, toolsLocation } from './library';
import { LibrarySettings } from './LibrarySettings';
import { Overview } from './Overview';
import { ScanFolderInfo, ScanStatus } from './ScanStatus';
import { Select } from './Select';
import { ToolsHub } from './ToolsHub';
import { ActionLink, Button, PageHeader, SectionHeader } from './ui';

export type CatalogView = FolderRole | 'settings' | 'overview' | 'collections' | 'tools';

export function Catalog({ view, title, section, onSignedOut, series, tools, children }: {
  view: CatalogView; title: string; section: string; onSignedOut: () => void; series?: SeriesLocation | null;
  tools: ReturnType<typeof toolsLocation>; children?: ReactNode;
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
        timer = window.setTimeout(loadRoots, available.some(scanIsActive) ? 500 : available.some(hasPendingScanLookups) ? 1500 : 4500);
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

  async function scanRequest(action: 'quick' | 'full' | 'pause' | 'resume' | 'stop') {
    if (!rootId) return;
    setScanPending(true);
    setScanError('');
    try {
      const suffix = action === 'quick' || action === 'full' ? `?full=${action === 'full'}` : '/' + action;
      const response = await fetch(`${ROOTS_URL}/${rootId}/scan${suffix}`, {
        method: 'POST', headers: { 'X-CSRF-Token': await getCsrfToken() },
      });
      if (response.status === 401) return onSignedOut();
      setRefresh(value => value + 1);
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(problem?.message ?? 'Could not update the scan. Check its status and try again.');
      }
    } catch (reason) {
      setScanError(reason instanceof Error ? reason.message : 'Could not update the scan.');
    } finally {
      setScanPending(false);
    }
  }

  if (series) {
    const seriesRoot = roots?.find(root => root.id === series.rootId);
    return <SeriesPage key={`${series.rootId}/${series.seriesKey}`} {...series} onSignedOut={onSignedOut}
      version={`${inventoryVersion}:${refresh}`} coverVersion={`${seriesRoot?.lastScannedAt ?? ''}:${refresh}`}
      rootPath={seriesRoot?.path} onInventoryChanged={() => setRefresh(value => value + 1)} />;
  }

  if (tools) return <ToolsHub route={tools} roots={roots} version={`${inventoryVersion}:${refresh}`} onSignedOut={onSignedOut}
    onChanged={() => setRefresh(value => value + 1)} />;

  const intakeTarget = view === 'import' && selected && roots ? intakeTargetFor(selected, roots) : undefined;
  const folderTitle = view === 'import' ? 'Import folder' : 'Library folder';
  const paused = selected?.state === 'paused';
  const scanActive = selected && scanIsActive(selected);
  const controlPending = scanPending || !!selected?.scanProgress?.requestedAction || selected?.scanProgress?.phase === 'publishing';
  return (
    <section className="workspace-page" aria-labelledby="catalog-heading">
      <PageHeader id="catalog-heading" section={section} title={title} />
      {rootsError && <p role="alert" className="error">{rootsError}</p>}
      {!roots && !rootsError && <p role="status">Loading library folders…</p>}
      {view === 'overview' && roots && <Overview roots={roots} onSignedOut={onSignedOut} />}
      {view === 'collections' && <Collections version={inventoryVersion ?? ''} onSignedOut={onSignedOut} />}
      {view === 'settings' && roots && (
        <>
          <LibrarySettings folders={roots} onSignedOut={onSignedOut} onChange={() => setRefresh(value => value + 1)} />
          {children}
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
            <div className="mb-1 flex items-center justify-between gap-2">
              <SectionHeader title={folderTitle} />
              <ScanFolderInfo root={selected} title={folderTitle} />
            </div>
            <div className="catalog-controls">
              <Select className="flex-[1_1_13rem]" label={folderTitle} hideLabel
                value={selected.id} options={folderOptions} onChange={id => {
                  setSelectedId(id);
                  setScanError('');
                }} />
              <div className="action-row">
                {paused || scanActive ? <>
                  {paused ? <Button icon={Play} aria-label="Resume scan" title="Resume scan" disabled={controlPending}
                    onClick={() => void scanRequest('resume')} /> : selected.state === 'scanning' && <Button icon={Pause}
                    aria-label="Pause scan" title="Pause scan" disabled={controlPending} onClick={() => void scanRequest('pause')} />}
                  <Button icon={Square} variant="secondary" aria-label="Stop scan" title="Stop scan"
                    disabled={controlPending} onClick={() => void scanRequest('stop')} />
                </> : <>
                  <Button icon={ScanSearch} disabled={scanPending} onClick={() => void scanRequest('quick')}>Quick scan</Button>
                  <Button variant="secondary" disabled={scanPending} onClick={() => void scanRequest('full')}>Full verification</Button>
                </>}
              </div>
            </div>
            <ScanStatus root={selected} />
          </div>
          {scanError && <p role="alert" className="error">{scanError}</p>}
          {view === 'import' && roots && <>
            <IntakeTransfer source={selected} folders={roots} version={`${inventoryVersion}:${refresh}`}
              onSignedOut={onSignedOut} onChanged={() => setRefresh(value => value + 1)} />
            <ActionLink variant="secondary" href={organizeToolsHref(selected.id)} className="justify-self-start">Organize intake in Tools</ActionLink>
          </>}
          <CatalogInventory key={`${view}:${selected.id}`} rootId={selected.id} version={`${inventoryVersion}:${refresh}`}
            onSignedOut={onSignedOut} scanComplete={selected.lastScannedAt !== null} intake={view === 'import'} rootPath={selected.path}
            hasCovers={selected.hasCovers} coverVersion={`${selected.lastScannedAt ?? ''}:${refresh}`}
            managedFolders={selected.managed && view === 'import' ? [selected] : []}
            intakeTarget={canTransferTo(selected, intakeTarget) ? intakeTarget : undefined}
            onInventoryChanged={() => setRefresh(value => value + 1)} />
        </>
      )}
    </section>
  );
}
