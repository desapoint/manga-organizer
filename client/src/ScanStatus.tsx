import { Folder, Info, File } from 'lucide-react';
import { useId, useState } from 'react';
import { formatSize, hasPendingScanLookups, scanIsActive, scanLabels } from './library';
import type { LibraryRoot, ScanSnapshot } from './library';
import { Button, Modal } from './ui';

const stages = [
  { phase: 'discovering', label: 'Find files' },
  { phase: 'hashing', label: 'Verify files' },
  { phase: 'covers', label: 'Find covers' },
  { phase: 'publishing', label: 'Index files' },
  { phase: 'metadata', label: 'Look up AniList suggestions' },
];

function count(done: number, total: number, unit: string) {
  return `${done.toLocaleString()} / ${total.toLocaleString()} ${unit}`;
}

type PhaseDetails = { title: string; counter?: string; detail?: string; value?: number; total?: number };

function phaseDetails(scan: ScanSnapshot): PhaseDetails {
  switch (scan.phase) {
    case 'discovering': return { title: 'Finding files', counter: `${scan.discoveredFiles.toLocaleString()} found` };
    case 'hashing': return {
      title: 'Verifying file identities',
      counter: count(scan.processedFiles, scan.discoveredFiles, 'files'),
      detail: `${scan.hashedFiles.toLocaleString()} hashed · ${scan.reusedFiles.toLocaleString()} reused · ${formatSize(scan.bytesRead)} read`,
      value: scan.processedFiles,
      total: scan.discoveredFiles,
    };
    case 'covers': return {
      title: 'Looking for covers',
      counter: count(scan.seriesChecked, scan.seriesTotal, 'series'),
      detail: `${scan.coversFound.toLocaleString()} covers found`,
      value: scan.seriesChecked,
      total: scan.seriesTotal,
    };
    case 'publishing': return { title: 'Publishing the inventory', counter: `${scan.discoveredFiles.toLocaleString()} files` };
    default: return { title: 'Waiting for the scanner' };
  }
}

export function ScanStatus({ root }: { root: LibraryRoot }) {
  const titleId = useId();
  const scan = root.scanProgress;
  const lookups = root.metadataProgress;
  const pendingLookups = hasPendingScanLookups(root);
  const inMetadata = root.state === 'complete' && pendingLookups && lookups;
  const phase = scan?.phase ?? (inMetadata ? 'metadata' : 'complete');
  const current = phase === 'complete' ? stages.length : stages.findIndex(step => step.phase === phase);
  const active = (scanIsActive(root) && !scan?.requestedAction) || !!inMetadata;
  const stepsVisible = scan || root.state === 'complete' && root.lastScan;
  const path = scan?.currentPath;
  const separator = path?.lastIndexOf('/') ?? -1;
  const folder = separator >= 0 ? path?.slice(0, separator) : null;
  const details: PhaseDetails | null = scan ? phaseDetails(scan) : inMetadata ? {
    title: 'Looking up AniList suggestions',
    counter: count(lookups.completed, lookups.total, 'series'),
    value: lookups.completed,
    total: lookups.total,
  } : null;
  const status = scan?.requestedAction === 'pause' ? 'Saving scan progress…'
    : scan?.requestedAction === 'stop' ? 'Stopping scan…'
    : root.state === 'paused' || root.state === 'stopped' ? scanLabels[root.state] : '';

  return <div className="mt-4 grid min-w-0 gap-2">
    {stepsVisible && <ol aria-label="Scan steps" className="m-0 grid list-none grid-cols-5 gap-1.5 p-0">
      {stages.map(({ phase: step, label }, index) => {
        const state = index < current ? 'Completed' : index === current ? 'Current' : 'Not started';
        const color = state === 'Completed' ? 'bg-positive/70' : state === 'Current' ? 'bg-accent' : 'bg-line';
        return <li key={step} aria-current={state === 'Current' ? 'step' : undefined} title={`${label} · ${state}`}
          className={`h-1.5 rounded-full ${color}`}><span className="sr-only">{label} · {state}</span></li>;
      })}
    </ol>}
    {details ? <>
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <p id={titleId} className="m-0 font-semibold" role="status">{status ? `${status} ${details.title}` : details.title}</p>
        {details.counter && <span className="ms-auto text-end text-sm text-muted tabular-nums">{details.counter}</span>}
      </div>
      {phase !== 'queued' && <progress aria-labelledby={titleId} max={details.total || 1} value={details.value}
        className="progress-bar scan-progress-bar" data-animated={active} />}
      {details.detail && <p className="m-0 text-sm text-muted">{details.detail}</p>}
      {path && <div className="flex min-w-0 flex-wrap items-start gap-2">
        {folder && <code aria-label="Current folder" className="inline-flex max-w-full items-center gap-1.5 rounded-lg border border-line bg-inset px-2 py-1 text-xs text-muted wrap-anywhere">
          <Folder className="size-3.5 shrink-0" aria-hidden="true" /><span className="min-w-0">{folder}</span>
        </code>}
        <code aria-label="Current file" className="inline-flex max-w-full items-center gap-1.5 rounded-lg border border-line bg-inset px-2 py-1 text-xs text-ink wrap-anywhere">
          <File className="size-3.5 shrink-0" aria-hidden="true" /><span className="min-w-0">{separator < 0 ? path : path.slice(separator + 1)}</span>
        </code>
      </div>}
    </> : <p className="m-0 text-sm text-muted" role="status">{scanLabels[root.state]}</p>}
  </div>;
}

export function ScanFolderInfo({ root, title }: { root: LibraryRoot; title: string }) {
  const [open, setOpen] = useState(false);
  const last = root.lastScan;
  const seconds = last && (last.elapsedMilliseconds / 1000).toLocaleString(undefined, { maximumFractionDigits: 1 });
  return <>
    <Button variant="ghost" icon={Info} aria-label={`${title} information`} title="Folder information" onClick={() => setOpen(true)} />
    {open && <Modal title={`${title} information`} onClose={() => setOpen(false)}>
      <dl className="m-0 grid gap-3 text-sm">
        <div><dt className="text-muted">Scanned files</dt><dd>{root.fileCount.toLocaleString()}</dd></div>
        <div><dt className="text-muted">Last indexed</dt><dd>{root.lastScannedAt
          ? <time dateTime={root.lastScannedAt}>{new Date(root.lastScannedAt).toLocaleString()}</time> : 'Never'}</dd></div>
        {root.skippedLinks > 0 && <div><dt className="text-muted">Linked paths skipped</dt><dd>{root.skippedLinks.toLocaleString()}</dd></div>}
        {last && <div><dt className="text-muted">{last.full ? 'Last full verification' : 'Last quick scan'}</dt>
          <dd>{last.hashedFiles.toLocaleString()} hashed · {last.reusedFiles.toLocaleString()} reused · {formatSize(last.bytesRead)} read · {seconds} s</dd></div>}
        {root.metadataProgress && <div><dt className="text-muted">AniList lookups</dt>
          <dd>{count(root.metadataProgress.completed, root.metadataProgress.total, 'series processed')}</dd></div>}
      </dl>
    </Modal>}
  </>;
}
