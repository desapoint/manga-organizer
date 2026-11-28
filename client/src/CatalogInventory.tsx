import { ArrowLeft, BookOpen, ChevronRight, Info, Library, Split, X } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import type { ReactNode } from 'react';
import { getCsrfToken } from './api';
import { ChapterCoverage } from './ChapterCoverage';
import { CollectionForm, emptyFilters, SeriesCollectionMembership, TagRulesFields, useTagNames } from './CollectionControls';
import type { TagRules } from './CollectionControls';
import { FileAssociationEditor } from './FileAssociationEditor';
import { FileMove } from './FileOperations';
import { IntakeGroupingDialog } from './IntakeGrouping';
import { IntakeSeriesMatch, ReviewIntakeMatches } from './IntakeSeriesMatch';
import type { IntakeAniListMatch } from './IntakeSeriesMatch';
import { EditReleaseTags, ReleaseSelectionActions, SelectReleaseCheckbox, useReleaseSelection } from './ReleaseTagsEditor';
import type { LibraryRoot } from './library';
import { AniListSearchAction, MetadataDescription, MetadataDialogAction, SeriesInfo, useSeriesDetails } from './SeriesInfo';
import { EditionCoverage, SeriesChapterList, SeriesVolumeList } from './EditionCoverage';
import { FileCover, SeriesCover } from './SeriesCover';
import { duplicateFoldersHref, duplicateToolsHref, formatSize, isReadableArchive, organizeToolsHref, ROOTS_URL,
  seriesHref, seriesReaderHref } from './library';
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
  seriesTitle: string | null;
  hasSeriesMetadata: boolean;
  associationConfirmed: boolean;
  hasSeriesSuggestion: boolean;
  filenameEvidence?: { title: string; alternateTitle: string | null; event: string | null; creator: string | null; notes: string[] } | null;
  intakePath?: string | null;
  lookupTitle?: string;
};

export type Series = { rootId: string; key: string; title: string; fileCount: number; suggested: boolean; hasCover: boolean; hasMetadata: boolean;
  aniList?: IntakeAniListMatch; lookupQueued?: boolean; coverSeriesKey?: string; lookupTitle?: string };
type Page<T> = { total: number; items: T[]; lookupQueued?: boolean };
type InventoryProps = { rootId: string; version: string; onSignedOut: () => void };
type FileManagement = { onDataChanged: () => void; managedFolders: LibraryRoot[]; intakeTarget?: LibraryRoot; onMoved: () => void };
type InventoryActions = InventoryProps & FileManagement;
type Inspection = { kind: 'series'; series: Series } | { kind: 'file'; file: CatalogEntry }
  | { kind: 'folder'; directory: string };
type Inspect = (item: Inspection) => void;
type InventoryView = 'covers' | 'series' | 'files';
type SeriesSort = 'title' | 'recent' | 'files';
const intakeViews = [
  { value: 'all', label: 'All scanned files' },
  { value: 'attention', label: 'Needs attention' },
  { value: 'close', label: 'Close AniList matches' },
  { value: 'association', label: 'Unconfirmed association' },
  { value: 'series', label: 'No series suggestion' },
  { value: 'metadata', label: 'Missing metadata' },
  { value: 'cover', label: 'Missing cover' },
] as const;
type CatalogView = typeof intakeViews[number]['value'] | 'library' | 'unassociated';

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

function intakeFileTitle(file: CatalogEntry) {
  if (file.hasSeriesMetadata && file.seriesTitle) return file.seriesTitle;
  return file.filenameEvidence?.title || file.lookupTitle || file.seriesTitle || undefined;
}

function FileAssociationAction({ file, rootId, onSignedOut, onChange, intake }: {
  file: CatalogEntry; rootId: string; onSignedOut: () => void; onChange: () => void; intake: boolean;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const intakeTitle = intake ? intakeFileTitle(file) : undefined;
  const title = intake ? intakeTitle ?? 'Unidentified file' : file.seriesTitle ?? file.relativePath.split('/').at(-1) ?? file.relativePath;
  const lookupTitle = intake ? file.lookupTitle || file.seriesTitle || intakeTitle || '' : file.lookupTitle;

  async function confirm() {
    setBusy(true);
    setError('');
    try {
      const response = await fetch(`${ROOTS_URL}/${rootId}/series/${file.seriesKey}/association`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: JSON.stringify({ fileId: file.id, contentId: file.contentId }),
      });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      onChange();
    } catch {
      setError('Could not confirm this file. Refresh the inventory and check its current association.');
    } finally {
      setBusy(false);
    }
  }

  return file.hasSeriesMetadata ? <>
    <Button variant="secondary" title={`Confirm association with ${title}`} disabled={busy} onClick={confirm}>Confirm association</Button>
    {error && <span role="alert" className="text-sm text-danger">{error}</span>}
  </> : <>
    <AniListSearchAction rootId={rootId} seriesKey={file.seriesKey} title={title} lookupTitle={lookupTitle} onSignedOut={onSignedOut} onChange={onChange} />
    <MetadataDialogAction rootId={rootId} seriesKey={file.seriesKey} title={title} metadata={null}
    onChange={onChange} onSignedOut={onSignedOut} actionLabel="Assign series metadata" />
  </>;
}

function SplitIntakeFile({ rootId, file, onSignedOut, onChange }: {
  rootId: string; file: CatalogEntry; onSignedOut: () => void; onChange: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  async function split() {
    setBusy(true);
    setError('');
    try {
      const response = await fetch(`${ROOTS_URL}/${rootId}/series/${file.seriesKey}/split-file`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: JSON.stringify({ fileId: file.id, contentId: file.contentId }),
      });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(problem?.message ?? 'Could not split this file.');
      }
      setOpen(false);
      onChange();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not split this file.');
    } finally {
      setBusy(false);
    }
  }

  return <>
    <Button variant="ghost" icon={Split} onClick={() => { setError(''); setOpen(true); }}>Split from this series</Button>
    {open && <Modal title="Split file from series" onClose={() => { if (!busy) setOpen(false); }}>
      <div className="grid gap-3">
        <p className="m-0 text-sm wrap-anywhere"><code>{file.relativePath}</code></p>
        <p className="m-0 text-sm text-muted">Review this file as a separate group in Import. The file stays at its current path.</p>
        {error && <p role="alert" className="m-0 text-sm text-danger">{error}</p>}
        <Button variant="secondary" icon={Split} disabled={busy} onClick={() => void split()}>Split file</Button>
      </div>
    </Modal>}
  </>;
}

function FileRow({ file, rootId, seriesKey, version, onInspect, management, onSignedOut, selection, onAssociationChanged, importGroupSize, onCoverageChanged }:
  Pick<InventoryProps, 'rootId' | 'version'> & {
  file: CatalogEntry; seriesKey?: string; onInspect?: Inspect; onSignedOut: () => void; management?: FileManagement;
  selection: ReturnType<typeof useReleaseSelection>; onAssociationChanged?: () => void; importGroupSize?: number; onCoverageChanged?: () => void;
}) {
  const [showCoverage, setShowCoverage] = useState(false);
  const [move, setMove] = useState<'organize' | 'transfer' | null>(null);
  const [moving, setMoving] = useState(false);
  const separator = file.relativePath.lastIndexOf('/');
  const name = onInspect ? intakeFileTitle(file) ?? 'Unidentified file' : file.relativePath.slice(separator + 1);
  const coverage = [file.volume && `Vol. ${file.volume}`, file.chapter && `Ch. ${file.chapter}`].filter(Boolean).join(' · ');
  const readable = file.contentId && isReadableArchive(file.extension);
  const saveCoverage = onCoverageChanged ?? management?.onDataChanged;
  const coverageUrl = saveCoverage && ROOTS_URL + '/' + rootId + '/series/' + (seriesKey ?? file.seriesKey) + '/coverage';
  const coverageLabel = onCoverageChanged ? 'Assign volume or chapters' : 'Set chapter coverage';
  const transferTarget = file.intakePath ? management?.intakeTarget : undefined;
  const updateAssociation = onAssociationChanged ?? management?.onDataChanged;
  function finish(change?: () => void) {
    selection.remove(file.id);
    change?.();
  }
  return (
    <li>
      <span className="flex min-w-0 items-center gap-3 max-[36rem]:col-span-full">
        {selection.enabled && <SelectReleaseCheckbox label={`Select ${onInspect ? [name, coverage].filter(Boolean).join(', ') : file.relativePath}`} disabled={!file.contentId}
          checked={selection.selected.has(file.id)} onChange={() => selection.toggle([file.id])} />}
        <FileCover rootId={rootId} fileId={file.id} available={file.hasCover} seriesCoverKey={file.seriesCoverKey} revision={version}
          className="h-12 w-8 shrink-0 rounded-sm" />
        <span className="file-path" title={onInspect ? undefined : file.relativePath}>
          {onInspect ? <button type="button" className="min-h-11 cursor-pointer rounded-sm border-0 bg-transparent p-0 text-start
            text-inherit wrap-anywhere hover:text-accent-readable hover:underline desktop:min-h-0"
            onClick={() => onInspect({ kind: 'file', file })}>{name}</button> : name}
          {separator >= 0 && !onInspect && <small>{file.relativePath.slice(0, separator)}</small>}
          {management && !onInspect && file.filenameEvidence && <small>{[file.filenameEvidence.alternateTitle, file.filenameEvidence.event,
            file.filenameEvidence.creator, ...file.filenameEvidence.notes].filter(Boolean).join(' · ')}</small>}
          {coverage && <small>{coverage}</small>}
          {file.indexedLocations === null && <small>Content identity unavailable</small>}
          {file.indexedLocations !== null && file.indexedLocations > 1 && (
            <small>Same bytes at {file.indexedLocations.toLocaleString()} indexed locations</small>
          )}
        </span>
      </span>
      <span className="file-format">{file.extension.toUpperCase()}</span>
      <span>{formatSize(file.size)}</span>
      <time dateTime={file.modifiedAt}>{new Date(file.modifiedAt).toLocaleDateString()}</time>
      {onInspect && management && file.contentId && <EditReleaseTags rootId={rootId} fileId={file.id} iconOnly
        onSignedOut={onSignedOut} onSaved={() => finish(management.onDataChanged)} />}
      {file.contentId && <div className="action-row col-span-full">
        {readable && <ActionLink variant="ghost" icon={BookOpen} href={onInspect ? `#read/${file.contentId}/intake` :
          seriesKey ? seriesReaderHref(file.contentId, rootId, seriesKey) : `#read/${file.contentId}`}>Read file</ActionLink>}
        {!onAssociationChanged && !onInspect && <ActionLink variant="ghost"
          href={duplicateToolsHref({ rootId, seriesKey: file.seriesKey }, file.id)}>Compare in Tools</ActionLink>}
        {updateAssociation && importGroupSize === undefined && !file.associationConfirmed && <FileAssociationAction file={file} rootId={rootId}
          intake={!!onInspect} onChange={() => finish(updateAssociation)} onSignedOut={onSignedOut} />}
        {updateAssociation && importGroupSize === undefined && <FileAssociationEditor key={file.contentId + file.seriesKey} rootId={rootId} fileId={file.id}
          contentId={file.contentId} seriesKey={file.seriesKey} relativePath={file.relativePath} confirmed={file.associationConfirmed}
          onSignedOut={onSignedOut} onSaved={() => finish(updateAssociation)} />}
        {updateAssociation && (importGroupSize ?? 0) > 1 &&
          <SplitIntakeFile rootId={rootId} file={file} onSignedOut={onSignedOut} onChange={() => finish(updateAssociation)} />}
        {management && !onInspect && <EditReleaseTags rootId={rootId} fileId={file.id} onSignedOut={onSignedOut}
          onSaved={() => finish(management.onDataChanged)} />}
        {coverageUrl && <Button variant="ghost" onClick={() => setShowCoverage(true)}>{coverageLabel}</Button>}
        {management?.intakeTarget && file.associationConfirmed && !file.intakePath &&
          <span className="text-sm text-muted">Set a chapter or volume before transfer.</span>}
        {transferTarget && <Button variant="secondary" onClick={() => setMove('transfer')}>Transfer to library</Button>}
        {!!management?.managedFolders.length && <Button variant="ghost" onClick={() => setMove('organize')}>Move or rename</Button>}
        {showCoverage && coverageUrl && saveCoverage && <Modal title={coverageLabel} onClose={() => setShowCoverage(false)}>
          <p className="m-0 mb-3 text-sm"><code>{file.relativePath}</code></p>
          <ChapterCoverage url={coverageUrl} contentId={file.contentId} volumeHint={file.volume}
            chapterHint={file.chapter} onSaved={() => finish(saveCoverage)} onSignedOut={onSignedOut} published={!!onCoverageChanged} />
        </Modal>}
        {move && management && (move === 'organize' || transferTarget) && <Modal
          title={move === 'transfer' ? 'Review intake transfer' : 'Move or rename file'} wide onClose={() => { if (!moving) setMove(null); }}>
          <FileMove rootId={rootId} fileId={file.id} relativePath={file.relativePath} folders={management.managedFolders}
            intakeTarget={move === 'transfer' ? transferTarget : undefined} intakePath={file.intakePath}
            onSignedOut={onSignedOut} onApplying={setMoving} onApplied={() => { setMove(null); finish(management.onMoved); }} />
        </Modal>}
      </div>}
    </li>
  );
}

type ReleaseFolder = { directory: string; fileIds: string[] };

function useReleaseFolders(url: string, version: string, onSignedOut: () => void) {
  const [result, setResult] = useState<{ url: string; folders?: ReleaseFolder[] }>();
  useEffect(() => {
    if (!url) return;
    const request = new AbortController();
    void fetch(url, { signal: request.signal }).then(async response => {
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const folders = await response.json() as ReleaseFolder[];
      if (!request.signal.aborted) setResult({ url, folders });
    }).catch(() => { if (!request.signal.aborted) setResult({ url }); });
    return () => request.abort();
  }, [url, version, onSignedOut]);
  return result?.url === url ? result : undefined;
}

export function FileInventory({ rootId, seriesKey, volume, version, onSignedOut, management, onInspect, search = '', catalogView = 'all',
  onAssociationChanged, importGroupSize, onCoverageChanged, seriesToolbar }: InventoryProps & {
  seriesKey?: string; volume?: string; management?: FileManagement; onInspect?: Inspect; search?: string; catalogView?: CatalogView;
  onAssociationChanged?: () => void; importGroupSize?: number; onCoverageChanged?: () => void;
  seriesToolbar?: (actions: ReactNode) => ReactNode;
}) {
  const params = new URLSearchParams({ search, view: catalogView });
  if (seriesKey) params.set('seriesKey', seriesKey);
  if (volume) params.set('volume', volume);
  const { page, offset, setOffset, error, retry } = useInventoryPage<CatalogEntry>(`${ROOTS_URL}/${rootId}/files`, version, onSignedOut, params);
  const persistent = !!onInspect && !!management;
  const foldersUrl = persistent && seriesKey ? `${ROOTS_URL}/${rootId}/series/${seriesKey}/release-folders?view=${catalogView}` : '';
  const folderResult = useReleaseFolders(foldersUrl, version, onSignedOut);
  const folders = folderResult?.folders;
  const showFolders = !!folders && folders.length > 1;
  const pageIds = page?.items.flatMap(file => file.contentId ? [file.id] : []) ?? [];
  const selection = useReleaseSelection(foldersUrl || `${rootId}:${params}:${offset}`,
    folders?.flatMap(folder => folder.fileIds) ?? pageIds, persistent);
  const label = seriesKey ? 'Series files' : 'Catalog files';
  const rowActions = persistent ? ' with-file-actions' : '';
  const tagActions = management && (persistent || pageIds.length > 0) ? <ReleaseSelectionActions selection={selection}
    pageIds={pageIds} rootId={rootId} onSignedOut={onSignedOut} onSaved={management.onDataChanged} persistent={persistent} /> : null;
  const visibleFolders = new Map<string, CatalogEntry[]>();
  for (const file of page?.items ?? []) {
    const directory = showFolders ? file.relativePath.slice(0, Math.max(0, file.relativePath.lastIndexOf('/'))) : '';
    const entries = visibleFolders.get(directory);
    if (entries) entries.push(file);
    else visibleFolders.set(directory, [file]);
  }
  const folderInfo = new Map(folders?.map((folder, index) => [folder.directory,
    { ids: folder.fileIds, label: folder.directory ? `Folder ${index + 1}` : 'Intake root' }]));

  return (
    <>
      {seriesToolbar?.(tagActions)}
      {error && <div className="px-4 pb-3"><InventoryError retry={retry}>Could not load files.</InventoryError></div>}
      {!page && !error && <p className="px-4" role="status">Loading files…</p>}
      {folderResult && !folders && <p role="alert" className="m-0 px-4 py-2 text-sm text-danger">Could not load folder selection. Individual files remain selectable.</p>}
      {page?.total === 0 && <p className="px-4">{search ? 'No files match this search.' : catalogView === 'all' ?
        'No files in the current inventory.' : 'No files match this view.'}</p>}
      {page && page.total > 0 && (
        <div className={seriesKey ? 'min-w-0 rounded-b-xl border-t border-line bg-inset' : 'surface'}>
          {!seriesToolbar && tagActions && <div className="action-row border-b border-line px-4 py-2">{tagActions}</div>}
          <div className={`catalog-columns${rowActions}`} aria-hidden="true">
            <span>File</span><span>Type</span><span>Size</span><span>Modified</span>
          </div>
          <ul className={`catalog-files${rowActions}`} role="list" aria-label={label}>
            {[...visibleFolders].flatMap(([directory, entries]) => {
              const folder = folderInfo.get(directory);
              const folderIds = folder?.ids ?? [];
              const count = folderIds.filter(id => selection.selected.has(id)).length;
              const checked = count === 0 ? false : count === folderIds.length ? true : 'mixed' as const;
              const folderLabel = folder?.label ?? 'Folder';
              return [
                showFolders && onInspect && <li key={`folder:${directory}`} className="catalog-folder-heading">
                  <SelectReleaseCheckbox label={`Select all eligible files in ${folderLabel}`} checked={checked}
                    disabled={!folderIds.length} onChange={() => selection.toggle(folderIds)} />
                  <span className="min-w-0 wrap-anywhere">{folderLabel}</span>
                  <Button variant="ghost" icon={Info} aria-label={`Open ${folderLabel} details`} title="Folder details"
                    onClick={() => onInspect({ kind: 'folder', directory })} />
                </li>,
                ...entries.map(file => <FileRow key={file.id} file={file} rootId={rootId} seriesKey={seriesKey} version={version}
                  onInspect={onInspect} onSignedOut={onSignedOut} management={management} selection={selection}
                  onAssociationChanged={onAssociationChanged} importGroupSize={importGroupSize} onCoverageChanged={onCoverageChanged} />),
              ];
            })}
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

function SeriesRow({ series, onInspect, coverVersion, catalogView, selection, ...inventory }: InventoryActions & {
  series: Series; onInspect?: Inspect; coverVersion: string; catalogView: CatalogView;
  selection?: { checked: boolean; disabled: boolean; toggle: () => void };
}) {
  const [open, setOpen] = useState(false);
  const [coverOpen, setCoverOpen] = useState(false);
  const filesId = useId();
  const rowClass = 'grid cursor-pointer list-none grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2 rounded-xl p-3 hover:bg-nav-hover sm:gap-3 sm:px-4';
  const contents = <>
    {series.hasCover ? <SeriesCover rootId={inventory.rootId} seriesKey={series.coverSeriesKey ?? series.key} available={series.hasCover} revision={coverVersion} className="h-14 w-10 rounded-md" /> :
      <span className="grid size-8 place-items-center rounded-lg bg-nav-active text-nav-icon"><Library className="size-4" aria-hidden="true" /></span>}
    <span className="wrap-anywhere">
      <span className="block font-semibold">{series.title}</span>
      <span className="block text-sm text-muted">{series.fileCount.toLocaleString()} {series.fileCount === 1 ? 'file' : 'files'}</span>
    </span>
    <ChevronRight className="size-4 text-muted group-open:rotate-90" aria-hidden="true" />
  </>;

  if (onInspect) return <li className="surface">
    <div className="flex min-w-0 flex-wrap items-center gap-2 p-3 sm:px-4">
      {selection && <label className="grid min-h-11 min-w-9 place-items-center">
        <input type="checkbox" aria-label={"Select " + series.title} checked={selection.checked}
          disabled={selection.disabled} onChange={selection.toggle} />
      </label>}
      {series.hasCover ? <Button variant="ghost" className="shrink-0 p-0" aria-label={`Enlarge cover for ${series.title}`}
        onClick={() => setCoverOpen(true)}>
        <SeriesCover rootId={inventory.rootId} seriesKey={series.key} available revision={coverVersion} className="h-14 w-10 rounded-md" />
      </Button> : <span className="grid size-9 shrink-0 place-items-center rounded-lg bg-nav-active text-nav-icon">
        <Library className="size-4" aria-hidden="true" />
      </span>}
      <button type="button" className="flex min-h-11 min-w-[8rem] flex-1 cursor-pointer items-center justify-between gap-2 rounded-lg
        border-0 bg-transparent p-1 text-start text-ink wrap-anywhere hover:bg-nav-hover"
        aria-expanded={open} aria-controls={filesId} onClick={() => setOpen(value => !value)}>
        <span className="min-w-0">
          <strong className="block">{series.title}</strong>
          <span className="text-sm text-muted">{series.fileCount.toLocaleString()} {series.fileCount === 1 ? 'file' : 'files'}</span>
        </span>
        <ChevronRight aria-hidden="true" className={`size-4 shrink-0 text-muted ${open ? 'rotate-90' : ''}`} />
      </button>
      <Button variant="ghost" icon={Info} aria-label={`Intake details for ${series.title}`}
        title="Intake details" onClick={() => onInspect({ kind: 'series', series })} />
      <div className="min-w-0 basis-full sm:ml-auto sm:basis-auto sm:max-w-[55%]">
        <IntakeSeriesMatch series={series} onSignedOut={inventory.onSignedOut} onSaved={inventory.onDataChanged} />
      </div>
    </div>
    <div id={filesId} hidden={!open}>
      {open && <>
        <FileInventory {...inventory} seriesKey={series.key} onInspect={onInspect} management={inventory} catalogView={catalogView}
          importGroupSize={series.fileCount} seriesToolbar={actions => <SeriesInfo rootId={inventory.rootId} seriesKey={series.key}
            title={series.title} version={inventory.version} onChange={inventory.onDataChanged} onSignedOut={inventory.onSignedOut}
            suggestion={series.aniList?.revision ? series.aniList.metadata : undefined} actions={actions} />} />
      </>}
    </div>
    {coverOpen && <Modal title={series.title} wide onClose={() => setCoverOpen(false)}>
      <SeriesCover rootId={inventory.rootId} seriesKey={series.key} available revision={coverVersion}
        className="rounded-lg [&_img]:max-h-[70dvh] [&_img]:object-contain" />
    </Modal>}
  </li>;

  return (
    <li className="surface">
      <a className={`${rowClass} text-ink no-underline`} href={seriesHref(inventory.rootId, series.key)}>{contents}</a>
    </li>
  );
}

export function CoverGrid({ series, coverVersion }: { series: Series[]; coverVersion: string }) {
  return (
    <ul className="m-0 grid list-none grid-cols-[repeat(auto-fill,minmax(min(100%,8rem),1fr))] gap-4 p-0 sm:gap-5" role="list" aria-label="Series covers">
      {series.map(item => (
        <li key={item.rootId + item.key} className="min-w-0">
          <a href={seriesHref(item.rootId, item.key)} className="group grid gap-2 rounded-xl border border-transparent p-1 text-start text-ink no-underline hover:bg-panel">
            <SeriesCover rootId={item.rootId} seriesKey={item.coverSeriesKey ?? item.key} available={item.hasCover} revision={coverVersion}
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

export function SeriesPage({ seriesKey, coverVersion, rootPath, onInventoryChanged, ...inventory }: InventoryProps & {
  seriesKey: string; coverVersion: string; rootPath?: string; onInventoryChanged: () => void;
}) {
  const [revision, setRevision] = useState(0);
  const [dialog, setDialog] = useState<'info' | 'cover' | null>(null);
  const [selectedView, setView] = useState<SeriesView | null>(null);
  const [volume, setVolume] = useState<string | null>(null);
  const catalog = { ...inventory, version: `${inventory.version}:${revision}` };
  const onDataChanged = () => {
    setRevision(value => value + 1);
    onInventoryChanged();
  };
  const { rootId, version, onSignedOut } = catalog;
  const { page, error, retry } = useInventoryPage<Series>(`${ROOTS_URL}/${rootId}/series`, version, onSignedOut, { seriesKey, view: 'library' });
  const series = page?.items[0];
  const { details, error: detailsError } = useSeriesDetails({ rootId, seriesKey, version, onSignedOut, published: true });
  const metadata = details?.metadata ?? null;
  const reading = details?.reading;
  const title = metadata?.title ?? series?.title ?? 'Series';
  const seriesUrl = `${ROOTS_URL}/${rootId}/series/${seriesKey}`;
  const seriesTools = { url: seriesUrl, version, onSignedOut, onChange: onDataChanged, published: true };
  const view = selectedView ?? (details?.chapters.length ? 'chapters' : details?.volumes.length ? 'volumes' :
    metadata?.totalChapters != null ? 'chapters' : metadata?.totalVolumes != null ? 'volumes' : 'files');
  return (
    <section className="workspace-page" aria-labelledby="series-heading">
      <ActionLink href="#library" variant="ghost" icon={ArrowLeft} className="justify-self-start">Back to library</ActionLink>
      <div className="surface grid min-w-0 gap-4 bg-linear-to-br from-nav-active/45 to-panel p-4 sm:grid-cols-[auto_minmax(0,1fr)] sm:items-center sm:gap-6 sm:p-6">
        {series?.hasCover && <Button variant="ghost" className="w-24 justify-self-start p-0 sm:w-37" aria-label="Enlarge cover" onClick={() => setDialog('cover')}>
          <SeriesCover rootId={rootId} seriesKey={series?.coverSeriesKey ?? seriesKey} available revision={coverVersion} className="aspect-[2/3] w-full rounded-lg" />
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
            <MetadataDialogAction rootId={rootId} seriesKey={seriesKey} title={title} metadata={metadata} published
              onChange={onDataChanged} onSignedOut={onSignedOut} />
            <AniListSearchAction rootId={rootId} seriesKey={seriesKey} title={title} published
              onChange={onDataChanged} onSignedOut={onSignedOut} />
            <ActionLink variant="ghost" href={duplicateToolsHref({ rootId, seriesKey })}>Review duplicates in Tools</ActionLink>
            <ActionLink variant="ghost" href={duplicateFoldersHref({ rootId, seriesKey })}>Compare folders in Tools</ActionLink>
          </div>}
          {reading?.pageIndex != null && <p className="m-0 text-sm text-muted wrap-anywhere">
            Page {reading.pageIndex + 1} of {reading.pageCount} · {reading.relativePath.split('/').at(-1)}</p>}
        </div>
      </div>
      {error && <InventoryError retry={retry}>Could not load this series.</InventoryError>}
      {!error && !page && <p role="status">Loading series…</p>}
      {page && !series && <div className="action-row"><p className="m-0">No confirmed files are published for this series in this folder.</p>
        <ActionLink variant="secondary" href={organizeToolsHref(rootId, seriesKey)}>Review files in Tools</ActionLink></div>}
      {series && detailsError && <p role="alert" className="m-0 text-danger">{detailsError}</p>}
      {series && details && <section aria-label="Reading and collection progress" className="surface grid gap-4 p-4 sm:grid-cols-3">
        <CollectionTotal label="Read chapters · released" value={details.readCatalogChapters}
          total={metadata?.totalChapters ?? details.availableChapters} />
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
            <FileInventory key={volume ?? 'all-files'} {...catalog} seriesKey={seriesKey} volume={volume ?? undefined} catalogView="library" onCoverageChanged={onDataChanged} />
          </>}
          {view === 'chapters' && <>
            {volume && <div className="action-row p-3"><span>Volume {volume}</span><Button variant="ghost" onClick={() => setVolume(null)}>Show all chapters</Button></div>}
            <SeriesChapterList key={volume ?? 'all-chapters'} {...seriesTools} rootId={rootId} seriesKey={seriesKey} volume={volume}
              canEditDetails={!!details?.metadata} />
          </>}
          {view === 'volumes' && details && <SeriesVolumeList {...seriesTools} details={details} rootId={rootId} seriesKey={seriesKey}
            onBrowse={(kind, number) => { setVolume(number); setView(kind); }} />}
        </div>
      </section>}
      {series && <SeriesCollectionMembership rootId={rootId} seriesKey={seriesKey} onSignedOut={onSignedOut} />}
      {dialog === 'info' && <Modal title="More info" onClose={() => setDialog(null)}>
        <div className="grid gap-4">
          <strong>{title}</strong>
          {metadata ? <MetadataDescription metadata={metadata} /> : <p className="m-0 text-sm text-muted">No metadata has been assigned.</p>}
          {rootPath && <p className="m-0 text-sm wrap-anywhere">Library folder <code>{rootPath}</code></p>}
        </div>
      </Modal>}
      {dialog === 'cover' && <Modal title={title} onClose={() => setDialog(null)}>
        <SeriesCover rootId={rootId} seriesKey={series?.coverSeriesKey ?? seriesKey} available revision={coverVersion}
          className="rounded-lg [&_img]:max-h-[70dvh] [&_img]:object-contain" />
      </Modal>}
    </section>
  );
}

function GroupedInventory({ scanComplete, onInspect, coverVersion, coverGrid, search, sort, coverOnly, rules, catalogView, ...inventory }: InventoryActions & {
  scanComplete: boolean; onInspect?: Inspect; coverVersion: string; coverGrid: boolean; search: string; sort: SeriesSort;
  coverOnly: boolean; rules: TagRules; catalogView: CatalogView;
}) {
  const { rootId, version, onSignedOut } = inventory;
  const [lookupRevision, setLookupRevision] = useState(0);
  const [matchReview, setMatchReview] = useState<Series[] | null>(null);
  const [selected, setSelected] = useState<Series[] | null>(null);
  const [grouping, setGrouping] = useState(false);
  const filters = new URLSearchParams({ search, sort, coverOnly: String(coverOnly), view: catalogView });
  rules.includeTags.forEach(tag => filters.append('includeTag', tag));
  rules.excludeTags.forEach(tag => filters.append('excludeTag', tag));
  const { page, offset, setOffset, error, retry } = useInventoryPage<Series>(`${ROOTS_URL}/${rootId}/series`,
    `${version}:${lookupRevision}`, onSignedOut, filters);
  const queued = Boolean(onInspect && (page?.lookupQueued || page?.items.some(item => item.lookupQueued)));
  useEffect(() => {
    if (!queued) return;
    const timer = window.setInterval(() => setLookupRevision(value => value + 1), 3000);
    return () => window.clearInterval(timer);
  }, [queued]);
  const closeMatches = onInspect ? page?.items.filter(item => item.aniList?.close && item.aniList.revision) ?? [] : [];

  return (
    <>
      {error && <InventoryError retry={retry}>Could not load the inventory.</InventoryError>}
      {!page && !error && <p role="status">Loading inventory…</p>}
      {onInspect && page && (page.items.length > 0 || selected) && <div className="action-row">
        <Button variant="secondary" onClick={() => setSelected(current => current ? null : [])}>
          {selected ? 'Cancel selection' : 'Select series to group'}
        </Button>
        {selected && <Button disabled={selected.length < 2} onClick={() => setGrouping(true)}>
          Group selected ({selected.length})
        </Button>}
      </div>}
      {grouping && selected && <IntakeGroupingDialog rootId={rootId} series={selected} onSignedOut={onSignedOut}
        onClose={() => setGrouping(false)} onSaved={() => { setGrouping(false); setSelected(null); inventory.onDataChanged(); }} />}
      {closeMatches.length > 0 && <Button variant="secondary" className="justify-self-start"
        onClick={() => setMatchReview(closeMatches)}>Review close matches ({closeMatches.length})</Button>}
      {matchReview && <ReviewIntakeMatches series={matchReview} onSignedOut={onSignedOut}
        onSaved={inventory.onDataChanged} onClose={() => setMatchReview(null)} />}
      {page?.total === 0 && (search || coverOnly || rules.includeTags.length || rules.excludeTags.length ?
        <p>No series match these filters.</p> : scanComplete && (catalogView === 'library' ?
          <div className="action-row"><p className="m-0">No confirmed series are published in this folder.</p>
            <ActionLink variant="secondary" href="#tools/metadata">Review associations and metadata</ActionLink></div> :
          <p>{queued ? 'Waiting for queued AniList suggestions…' : catalogView === 'all' ? 'No supported files found.' : 'No series match this view.'}</p>))}
      {page && page.total > 0 && (
        <>
          {coverGrid ? <CoverGrid series={page.items} coverVersion={coverVersion} /> : (
            <ul className="m-0 grid list-none gap-2 p-0" role="list" aria-label="Series groups">
              {page.items.map(series => <SeriesRow key={series.key} series={series} catalogView={catalogView}
                onInspect={onInspect} coverVersion={coverVersion} {...inventory} selection={selected ? {
                  checked: selected.some(item => item.key === series.key),
                  disabled: selected.length >= 50 && !selected.some(item => item.key === series.key),
                  toggle: () => setSelected(current => current?.some(item => item.key === series.key)
                    ? current.filter(item => item.key !== series.key) : [...(current ?? []), series]),
                } : undefined} />)}
            </ul>
          )}
          <PageControls label="Series pages" offset={offset} count={page.items.length} total={page.total} onChange={setOffset} />
        </>
      )}
    </>
  );
}

function IntakeDetails({ item, rootId, rootPath, version, onSignedOut, onClose }: InventoryProps & {
  item: Inspection; rootPath: string; onClose: () => void;
}) {
  const fileList = item.kind === 'series' ? `${ROOTS_URL}/${rootId}/files` : '';
  const originals = useInventoryPage<CatalogEntry>(fileList, version, onSignedOut,
    item.kind === 'series' ? { seriesKey: item.series.key, view: 'all' } : undefined);
  const separator = item.kind === 'file' ? item.file.relativePath.lastIndexOf('/') : -1;
  const evidence = item.kind === 'file' ? item.file.filenameEvidence : null;
  const title = item.kind === 'series' ? 'Series intake details' : item.kind === 'folder' ? 'Folder intake details' : 'File intake details';
  return (
    <Modal title={title} onClose={onClose}>
      <dl className="m-0 grid gap-3 text-sm wrap-anywhere">
        <div>
          <dt className="text-muted">Intake root</dt>
          <dd><code>{rootPath}</code></dd>
        </div>
        {item.kind === 'folder' && <div>
          <dt className="text-muted">Original folder</dt><dd><code>{item.directory || rootPath}</code></dd>
        </div>}
        {item.kind === 'series' && <>
          <div><dt className="text-muted">Series</dt><dd className="font-semibold">{item.series.title}</dd></div>
          <div><dt className="text-muted">Indexed files</dt><dd>{item.series.fileCount.toLocaleString()}</dd></div>
          <div><dt className="text-muted">Original locations</dt><dd>
            {originals.error && <InventoryError retry={originals.retry}>Could not load original locations.</InventoryError>}
            {!originals.page && !originals.error && <span role="status">Loading original locations…</span>}
            {originals.page && <>
              <ul className="m-0 grid max-h-48 list-none gap-1 overflow-auto p-0" aria-label="Original file paths" role="list">
                {originals.page.items.map(file => <li key={file.id}><code>{file.relativePath}</code></li>)}
              </ul>
              <PageControls label="Original locations pages" offset={originals.offset} count={originals.page.items.length}
                total={originals.page.total} onChange={originals.setOffset} />
            </>}
          </dd></div>
          <div><dt className="text-muted">Metadata</dt><dd>{item.series.hasMetadata ? 'Saved' : 'Awaiting review'}</dd></div>
          <div><dt className="text-muted">Artwork</dt><dd>{item.series.hasCover ? 'Available' : 'Missing cover'}</dd></div>
        </>}
        {item.kind === 'file' && <>
          <div><dt className="text-muted">Detected title</dt><dd className="font-semibold">{intakeFileTitle(item.file) ?? 'Unidentified file'}</dd></div>
          <div><dt className="text-muted">Original filename</dt><dd><code>{item.file.relativePath.slice(separator + 1)}</code></dd></div>
          <div><dt className="text-muted">Original folder</dt>
            <dd><code>{separator < 0 ? rootPath : item.file.relativePath.slice(0, separator)}</code></dd></div>
          {evidence?.alternateTitle && <div><dt className="text-muted">Alternate title</dt><dd>{evidence.alternateTitle}</dd></div>}
          {evidence && (evidence.event || evidence.creator || evidence.notes.length > 0) && <div>
            <dt className="text-muted">Filename notes</dt><dd>{[evidence.event, evidence.creator, ...evidence.notes].filter(Boolean).join(' · ')}</dd>
          </div>}
          <div><dt className="text-muted">Format and size</dt><dd>{item.file.extension.toUpperCase()} · {formatSize(item.file.size)}</dd></div>
          <div><dt className="text-muted">SHA-256</dt><dd>{item.file.sha256 ? <code>{item.file.sha256}</code> : 'Unavailable'}</dd></div>
          <div><dt className="text-muted">Series association</dt>
            <dd>{item.file.associationConfirmed ? 'Confirmed' : item.file.hasSeriesSuggestion ? 'Unconfirmed' : 'No series suggestion'}</dd></div>
          {!item.file.hasSeriesMetadata && <div><dt className="text-muted">Metadata</dt><dd>Missing series metadata</dd></div>}
          {!item.file.hasCover && !item.file.seriesCoverKey && <div><dt className="text-muted">Artwork</dt><dd>Missing cover</dd></div>}
        </>}
      </dl>
      {item.kind === 'file' && (item.file.indexedLocations ?? 0) > 1 &&
        <ActionLink variant="secondary" className="mt-4"
          href={duplicateToolsHref({ rootId, seriesKey: item.file.seriesKey }, item.file.id)} onClick={onClose}>
          Compare indexed copies in Tools
        </ActionLink>}
    </Modal>
  );
}

export function CatalogInventory({ scanComplete, intake, rootPath, hasCovers, coverVersion, managedFolders, intakeTarget, onInventoryChanged, ...inventory }:
  InventoryProps & { scanComplete: boolean; intake: boolean; rootPath: string; hasCovers: boolean; coverVersion: string;
    managedFolders: LibraryRoot[]; intakeTarget?: LibraryRoot; onInventoryChanged: () => void }) {
  const [selectedView, setView] = useState<InventoryView | null>(null);
  const [intakeView, setIntakeView] = useState<typeof intakeViews[number]['value']>('all');
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
  const catalog = { ...inventory, version: `${inventory.version}:${catalogRevision}`, managedFolders, intakeTarget,
    onDataChanged: () => {
      setCatalogRevision(value => value + 1);
      setItem(null);
      onInventoryChanged();
    }, onMoved: () => {
      setNotice('File change completed. A verified recovery copy is in Organize under File operation history.');
      onInventoryChanged();
    } };
  const onInspect = intake ? setItem : undefined;
  const catalogView = intake ? intakeView : 'library';
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
        {intake && <Select label="Intake filter" value={intakeView} options={intakeViews} onChange={value => {
          setIntakeView(value);
          setItem(null);
        }} />}
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
      {view === 'files' ? <FileInventory key={`${catalogView}:${query}`} {...catalog} onInspect={onInspect}
        management={intake ? catalog : undefined} search={query} catalogView={catalogView}
        onCoverageChanged={intake ? undefined : catalog.onDataChanged} /> :
        <GroupedInventory key={JSON.stringify([view, catalogView, query, sort, coverOnly, rules.includeTags, rules.excludeTags])}
          {...catalog} scanComplete={scanComplete} onInspect={onInspect} catalogView={catalogView}
          coverVersion={coverVersion} coverGrid={view === 'covers'} search={query} sort={sort} coverOnly={coverOnly && hasCovers} rules={rules} />}
      {intake && item && <IntakeDetails item={item} {...inventory} rootPath={rootPath} onClose={() => setItem(null)} />}
    </section>
  );
}
