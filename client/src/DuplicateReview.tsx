import { Archive, ShieldCheck } from 'lucide-react';
import { useEffect, useState } from 'react';
import { ChapterComparison } from './ChapterComparison';
import type { Chapter } from './ChapterComparison';
import { postFileOperation } from './FileOperations';
import type { Operation } from './FileOperations';
import { formatSize } from './library';
import { Select } from './Select';
import { FileCover } from './SeriesCover';
import { Button, Modal, SectionHeader } from './ui';

type ComparedFile = { rootId: string; fileId: string; relativePath: string; fullPath: string; format: string; size: number;
  sha256: string; volume: string | null; chapters: Chapter[]; hasCover: boolean; seriesCoverKey: string | null };
type Comparison = {
  candidate: ComparedFile; kind: 'exact' | 'hardlink' | 'identity-unknown' | 'overlap' | 'possible-overlap' | 'alternate';
  canQuarantine: boolean; reason: string; shared: Chapter[]; sourceOnly: Chapter[]; candidateOnly: Chapter[];
};
type Review = { source: ComparedFile; comparisons: Comparison[] };

const labels: Record<Comparison['kind'], string> = {
  exact: 'Exact bytes', hardlink: 'Hardlink alias', 'identity-unknown': 'Physical identity unknown',
  overlap: 'Partial chapter overlap', 'possible-overlap': 'Possible overlap', alternate: 'Alternate release',
};

function FileEvidence({ file, heading, marker, version }: { file: ComparedFile; heading: string; marker: string; version: string }) {
  return <section className="grid min-w-0 content-start gap-3 rounded-xl border border-line bg-inset p-3" aria-label={heading}>
    <SectionHeader title={`${marker} · ${heading}`} />
    <div className="flex min-w-0 items-start gap-3">
      <div className="w-12 shrink-0 sm:w-16">
        <FileCover rootId={file.rootId} fileId={file.fileId} available={file.hasCover} seriesCoverKey={file.seriesCoverKey}
          revision={version} className="aspect-[2/3] rounded-md" />
      </div>
      <div className="grid min-w-0 flex-1 gap-1 [overflow-wrap:anywhere]">
        <strong>{file.relativePath.split('/').at(-1)}</strong>
        <span className="text-sm text-muted">{file.format.toUpperCase()} · {formatSize(file.size)}{file.volume && ` · Vol. ${file.volume}`}</span>
        <span className="text-xs text-muted">{file.chapters.length ? `${file.chapters.length} indexed ${file.chapters.length === 1 ? 'chapter' : 'chapters'}` : 'Chapter coverage unknown'}</span>
      </div>
    </div>
    <dl className="m-0 min-w-0 text-xs [overflow-wrap:anywhere]">
      <dt className="font-semibold">Path</dt><dd className="m-0"><code>{file.fullPath}</code></dd>
      <dt className="mt-2 font-semibold">SHA-256</dt><dd className="m-0"><code>{file.sha256}</code></dd>
    </dl>
  </section>;
}

export function DuplicateReview({ rootId, fileId, version, onSignedOut, onClose, onChanged }: {
  rootId: string; fileId: string; version: string; onSignedOut: () => void; onClose: () => void; onChanged: () => void;
}) {
  const [review, setReview] = useState<Review | null>(null);
  const [error, setError] = useState('');
  const [chosen, setChosen] = useState('');
  const [busy, setBusy] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [plan, setPlan] = useState<Operation | null>(null);
  const [done, setDone] = useState(false);
  const comparison = review?.comparisons.find(item => item.candidate.fileId === chosen) ?? review?.comparisons[0];
  const fileLabels = { source: plan ? 'Move to quarantine' : 'Selected file', candidate: plan ? 'Keep in library' : 'Comparison file' };

  useEffect(() => {
    const request = new AbortController();
    void (async () => {
      const response = await fetch(`/api/file-operations/duplicates/${rootId}/${fileId}`, { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const reviewed = await response.json() as Review;
      if (!request.signal.aborted) setReview(reviewed);
    })().catch(() => { if (!request.signal.aborted) setError('Could not load duplicate evidence.'); });
    return () => request.abort();
  }, [rootId, fileId, onSignedOut, version]);

  async function preview(keepFile: string) {
    setBusy(true);
    setError('');
    try {
      setPlan(await postFileOperation('/quarantine-preview', onSignedOut, { rootId, sourceFile: fileId, keepFile }));
      setAcknowledged(false);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not verify a quarantine plan.');
    } finally {
      setBusy(false);
    }
  }

  async function apply() {
    if (!plan || !acknowledged || !comparison?.canQuarantine) return;
    setBusy(true);
    setError('');
    try {
      if (await postFileOperation('/' + plan.id + '/apply', onSignedOut)) setDone(true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not confirm the move. Check file operation history.');
      setPlan(null);
    } finally {
      setBusy(false);
    }
  }

  function close() {
    if (busy) return;
    onClose();
    if (done) onChanged();
  }

  return <Modal title="Compare duplicate releases" wide onClose={close}>
    <div className="grid gap-4">
      {!review && !error && <p role="status" className="m-0">Checking indexed copies and chapter ranges…</p>}
      {error && <p role="alert" className="m-0 text-danger">{error}</p>}
      {review && !comparison && <p className="m-0 text-muted">The catalog found no exact copies or overlapping chapter labels for this file.</p>}
      {review && comparison && <>
        {review.comparisons.length > 1 && !plan && !done && <Select label="Compare with" value={comparison.candidate.fileId}
          options={review.comparisons.map(item => ({ value: item.candidate.fileId, label: item.candidate.relativePath, description: labels[item.kind] }))}
          onChange={id => { setChosen(id); setError(''); }} disabled={busy} />}
        <section className="grid min-w-0 gap-3 rounded-xl border border-line p-3" aria-label="Chapter comparison">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <strong>{labels[comparison.kind]}</strong>
            <span className={comparison.canQuarantine ? 'text-sm text-positive' : 'text-sm text-warning'}>
              {comparison.canQuarantine ? 'Eligible for reviewed quarantine' : 'Files protected from quarantine'}
            </span>
          </div>
          <ChapterComparison key={comparison.candidate.fileId} shared={comparison.shared} sourceOnly={comparison.sourceOnly}
            candidateOnly={comparison.candidateOnly} labels={fileLabels} />
          <p className="m-0 text-sm text-muted">{comparison.reason}</p>
        </section>
        <div className="grid min-w-0 gap-3 sm:grid-cols-2">
          <FileEvidence file={review.source} heading={fileLabels.source} marker="A" version={version} />
          <FileEvidence file={comparison.candidate} heading={fileLabels.candidate} marker="B" version={version} />
        </div>
        {plan && !done && <section className="grid gap-3 rounded-xl border border-warning/45 bg-warning/10 p-3" aria-label="Confirm quarantine">
          <div><strong>Quarantine destination</strong><p className="m-0"><code>{plan.target}</code></p></div>
          <label className="flex items-center gap-2"><input type="checkbox" checked={acknowledged} disabled={busy}
            onChange={event => setAcknowledged(event.target.checked)} />I reviewed both files and the chapter coverage.</label>
          <div className="action-row">
            <Button icon={Archive} onClick={() => void apply()} disabled={busy || !acknowledged}>Confirm quarantine</Button>
            <Button variant="secondary" onClick={() => setPlan(null)} disabled={busy}>Back to comparison</Button>
          </div>
        </section>}
        {done ? <p role="status" className="m-0 text-positive">Moved into quarantine. Restore it from File operation history.</p> :
          !plan && comparison.canQuarantine &&
            <Button icon={ShieldCheck} disabled={busy} className="justify-self-start"
              onClick={() => void preview(comparison.candidate.fileId)}>Preview quarantine</Button>}
      </>}
    </div>
  </Modal>;
}
