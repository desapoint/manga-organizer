export type FolderRole = 'library' | 'import';
export type FolderKind = FolderRole | 'both';

export const ROOTS_URL = '/api/library/roots';

export const scanLabels = {
  idle: 'Ready to scan.',
  queued: 'Scan queued.',
  scanning: 'Scanning…',
  complete: 'Scan complete.',
  unavailable: 'Folder unavailable. Any previous inventory is still shown.',
  incomplete: 'Scan could not finish. Any previous inventory is still shown.',
  emptyReview: 'This scan found no supported files. Check the folder or mount. The previous inventory is still shown.',
  interrupted: 'Scan interrupted. Any previous inventory is still shown.',
};

export type ScanState = keyof typeof scanLabels;

export type ScanSnapshot = {
  phase: 'queued' | 'discovering' | 'hashing' | 'covers' | 'publishing';
  discoveredFiles: number;
  processedFiles: number;
  verifiedFiles: number;
  totalBytes: number;
  bytesRead: number;
  currentPath: string | null;
  seriesTotal: number;
  seriesChecked: number;
  coversFound: number;
};

export type LibraryRoot = {
  id: string;
  path: string;
  name: string | null;
  kind: FolderKind;
  state: ScanState;
  fileCount: number;
  lastScannedAt: string | null;
  skippedLinks: number;
  hasCovers: boolean;
  managed: boolean;
  scanProgress: ScanSnapshot | null;
  intakeTargetId: string | null;
};

export function intakeTargetFor(source: LibraryRoot, folders: LibraryRoot[]) {
  return folders.find(folder => folder.id === source.intakeTargetId && folder.kind !== 'import');
}

export function canTransferTo(source: LibraryRoot, target: LibraryRoot | undefined): target is LibraryRoot {
  return !!target && source.id !== target.id && source.managed && target.managed &&
    !scanIsActive(source) && !scanIsActive(target) && source.state !== 'unavailable' && target.state !== 'unavailable';
}

export function scanIsActive(root: LibraryRoot) {
  return root.state === 'queued' || root.state === 'scanning';
}

export function isReadableArchive(format: string) {
  return ['cbz', 'zip', 'cbr', 'rar'].includes(format);
}

export function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KiB`;
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MiB`;
  if (bytes < 1024 ** 4) return `${(bytes / 1024 ** 3).toFixed(1)} GiB`;
  return `${(bytes / 1024 ** 4).toFixed(1)} TiB`;
}

export type SeriesLocation = { rootId: string; seriesKey: string };

export function seriesHref(rootId: string, seriesKey: string) {
  return `#series/${rootId}/${seriesKey}`;
}

export function seriesReaderHref(contentId: string, rootId: string, seriesKey: string) {
  return `#read/${contentId}/series/${rootId}/${seriesKey}`;
}

export function seriesLocation(hash: string): SeriesLocation | null {
  const [, rootId, seriesKey] = /^#series\/([a-f0-9-]{36})\/([a-f0-9]{64})$/i.exec(hash) ?? [];
  return rootId && seriesKey ? { rootId, seriesKey } : null;
}

export function duplicateToolsHref(location?: SeriesLocation, fileId?: string) {
  return '#tools/duplicates' + (location ? `/${location.rootId}/${location.seriesKey}` : '') + (fileId ? `/${fileId}` : '');
}

export function organizeToolsHref(rootId?: string, seriesKey?: string) {
  return '#tools/organize' + (rootId ? `/${rootId}` : '') + (seriesKey ? `/${seriesKey}` : '');
}

export function versionsToolsHref(series?: SeriesLocation) {
  return '#tools/versions' + (series ? `/${series.rootId}/${series.seriesKey}` : '');
}

export function toolsLocation(hash: string) {
  if (hash === '#tools') return { module: 'home' as const };
  if (hash === '#tools/metadata') return { module: 'metadata' as const };
  if (hash === '#tools/maintenance') return { module: 'maintenance' as const };
  const versions = /^#tools\/versions(?:\/([a-f0-9-]{36})\/([a-f0-9]{64}))?$/i.exec(hash);
  if (versions) return { module: 'versions' as const, series: versions[1] && versions[2] ? { rootId: versions[1], seriesKey: versions[2] } : null };
  const organize = /^#tools\/organize(?:\/([a-f0-9-]{36})(?:\/([a-f0-9]{64}))?)?$/i.exec(hash);
  if (organize) return { module: 'organize' as const, rootId: organize[1], seriesKey: organize[2] };
  const match = /^#tools\/duplicates(?:\/([a-f0-9-]{36})\/([a-f0-9]{64})(?:\/([a-f0-9]{32}))?)?$/i.exec(hash);
  if (!match) return null;
  const [, rootId, seriesKey, fileId] = match;
  return { module: 'duplicates' as const, series: rootId && seriesKey ? { rootId, seriesKey } : null, fileId };
}
