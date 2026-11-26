import { formatSize, scanLabels } from './library';
import type { LibraryRoot, ScanSnapshot } from './library';

function phaseDetails(scan: ScanSnapshot) {
  switch (scan.phase) {
    case 'discovering': return { title: 'Finding files', detail: `${scan.discoveredFiles.toLocaleString()} found` };
    case 'hashing': return {
      title: 'Verifying file identities',
      detail: `${scan.processedFiles.toLocaleString()} of ${scan.discoveredFiles.toLocaleString()} checked · ${formatSize(scan.bytesRead)} read`,
      value: scan.totalBytes ? scan.bytesRead : scan.processedFiles,
      total: scan.totalBytes || scan.discoveredFiles,
    };
    case 'covers': return {
      title: 'Looking for covers',
      detail: `${scan.seriesChecked.toLocaleString()} of ${scan.seriesTotal.toLocaleString()} series checked · ${scan.coversFound.toLocaleString()} covers found`,
      value: scan.seriesChecked,
      total: scan.seriesTotal,
    };
    case 'publishing': return {
      title: 'Publishing the inventory', detail: `${scan.verifiedFiles.toLocaleString()} verified files`,
    };
    default: return { title: 'Waiting for the scanner', detail: '' };
  }
}

export function ScanStatus({ root }: { root: LibraryRoot }) {
  const scan = root.scanProgress;
  if (!scan) return <p className="scan-state" role="status">{scanLabels[root.state]}</p>;

  const { title, detail, value, total } = phaseDetails(scan);
  return (
    <div className="my-3 grid gap-1.5">
      <p className="m-0 font-medium" role="status">{title}</p>
      {total ? <progress className="h-2 w-full accent-accent" aria-label={title} value={value} max={total} /> : null}
      {detail && <p className="m-0 text-sm text-muted">{detail}</p>}
      {scan.currentPath && <code className="text-xs wrap-anywhere text-muted">{scan.currentPath}</code>}
    </div>
  );
}
