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

export type LibraryRoot = {
  id: string;
  path: string;
  name: string | null;
  kind: FolderKind;
  state: ScanState;
  fileCount: number;
  lastScannedAt: string | null;
  skippedLinks: number;
};

export function scanIsActive(root: LibraryRoot) {
  return root.state === 'queued' || root.state === 'scanning';
}
