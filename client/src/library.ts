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
};

export function scanIsActive(root: LibraryRoot) {
  return root.state === 'queued' || root.state === 'scanning';
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
