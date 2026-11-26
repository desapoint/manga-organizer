import { Archive, ShieldCheck } from 'lucide-react';
import { useState } from 'react';
import { DuplicateFileEvidence } from './DuplicateFileEvidence';
import { byteGroups, chapterKey, fileName, isBundle, matchingCoverage, sameVolume } from './duplicateCoverage';
import type { CoverageChapter, CoverageFile } from './duplicateCoverage';
import { postFileOperation } from './FileOperations';
import type { Operation } from './FileOperations';
import { Button, Modal } from './ui';

const MAX_MARKED_FILES = 50;
type Pair = { source: CoverageFile; keep: CoverageFile };
type Preview = Pair & { plan?: Operation; error?: string };
type BatchResult = { id: string; result: Operation | null; error: string | null };
export type DuplicateScope = { kind: 'file'; file: CoverageFile } | { kind: 'chapter'; chapter: CoverageChapter };

function quarantineCandidates(files: CoverageFile[], nearby: CoverageChapter[], rootId: string, focus?: CoverageFile) {
  const coveredFiles = new Set(nearby.flatMap(chapter => chapter.files.map(file => file.fileId)));
  const exactMatches = byteGroups(files.filter(file => file.rootId === rootId));
  const pairs: Pair[] = [];
  for (const group of exactMatches) {
    const remaining = [...group].sort((a, b) => Number(b.fileId === focus?.fileId) - Number(a.fileId === focus?.fileId));
    while (remaining.length > 1) {
      const keep = remaining.shift()!;
      const matches = remaining.filter(file => matchingCoverage(file, keep));
      for (const file of matches) remaining.splice(remaining.indexOf(file), 1);
      if (!focus || keep.fileId === focus.fileId || (isBundle(focus) && !isBundle(keep) &&
        [keep, ...matches].some(file => coveredFiles.has(file.fileId)))) {
        pairs.push(...matches.map(source => ({ source, keep })));
      }
    }
  }
  return pairs;
}

export function DuplicateBatch({ scope, files, chapters, colors, version, rootId, canReview, onClose, onChanged, onSignedOut }: {
  scope: DuplicateScope; files: CoverageFile[]; chapters: CoverageChapter[]; rootId: string;
  colors: Map<string, string>; version: string; canReview: boolean; onClose: () => void; onChanged: () => void; onSignedOut: () => void;
}) {
  const file = scope.kind === 'file' ? scope.file : undefined;
  const chapter = scope.kind === 'chapter' ? scope.chapter : undefined;
  const volume = file && isBundle(file);
  const [marked, setMarked] = useState<string[]>([]);
  const [previews, setPreviews] = useState<Preview[] | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const [results, setResults] = useState<BatchResult[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const keys = new Set(file?.chapters.map(chapterKey));
  const covered = chapters.filter(chapter => keys.has(chapter.key));
  const reviewFiles = chapter ? chapter.files.filter(item => !isBundle(item)) : files;
  const candidates = canReview ? quarantineCandidates(reviewFiles, covered, file?.rootId ?? rootId, file) : [];
  const volumeFiles = file && volume ? [file, ...files.filter(other => other.fileId !== file.fileId && sameVolume(file, other))] : [];
  const ids = previews?.flatMap(item => item.plan ? [item.plan.id] : []) ?? [];

  async function preview() {
    setBusy(true);
    setError('');
    const items: Preview[] = [];
    for (const pair of candidates.filter(item => marked.includes(item.source.fileId))) {
      try {
        const plan = await postFileOperation('/quarantine-preview', onSignedOut, {
          rootId: pair.source.rootId, sourceFile: pair.source.fileId, keepFile: pair.keep.fileId,
        });
        if (!plan) break;
        items.push({ ...pair, plan });
      } catch (reason) {
        items.push({ ...pair, error: reason instanceof Error ? reason.message : 'Could not verify these copies.' });
      }
    }
    setAcknowledged(false);
    setPreviews(items);
    setBusy(false);
  }

  async function apply() {
    setBusy(true);
    setError('');
    try {
      setResults(await postFileOperation<BatchResult[]>('/batch-apply', onSignedOut, { ids }));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not finish the selected operations. Check file operation history.');
    } finally {
      setBusy(false);
    }
  }

  function close() {
    if (busy) return;
    onClose();
    if (results?.some(item => item.result?.state === 'completed')) onChanged();
  }

  return <Modal title={chapter ? 'Review chapter duplicates' : volume ? covered.length ? 'Review volume chapters' : 'Review volume duplicates' : 'Review exact copies'} wide onClose={close}>
    <div className="grid gap-4">
      {file && <p className="m-0 font-semibold wrap-anywhere">{fileName(file)}</p>}
      {volumeFiles.length > 1 && !previews && <details open={covered.length === 0} className="rounded-lg border border-line px-3 pb-2">
        <summary className="min-h-11 cursor-pointer py-2.5 font-semibold">Compare volume files ({volumeFiles.length})</summary>
        <DuplicateFileEvidence files={volumeFiles} colors={colors} version={version} onSignedOut={onSignedOut} />
      </details>}
      {chapter && <details open={previews === null} className="rounded-lg border border-line px-3">
        <summary className="min-h-11 cursor-pointer py-2.5 font-semibold">Chapter {chapter.chapter}{chapter.volume && ` · Vol. ${chapter.volume}`} files</summary>
        <ul aria-label="Chapter files and protection" className="m-0 grid list-none gap-2 pb-3 pl-0" role="list">
          {chapter.files.map(item => <li key={item.fileId} className="border-t border-line pt-2 text-sm wrap-anywhere">
            <strong>{item.relativePath}</strong>
            <p className="m-0 text-xs text-muted">{isBundle(item) ? 'Volume or bundle · Review from its file entry' : 'Standalone chapter'}</p>
          </li>)}
        </ul>
      </details>}
      {file && volume && <details open={previews === null} className="rounded-lg border border-line px-3">
        <summary className="flex min-h-11 cursor-pointer items-center font-semibold">Chapters covered by this file ({covered.length})</summary>
        <ul className="m-0 grid list-none gap-2 pb-3 pl-0" role="list">
          {covered.map(chapter => <li key={chapter.key} className="border-t border-line pt-2 text-sm">
            <strong>Chapter {chapter.chapter}{chapter.volume && ` · Vol. ${chapter.volume}`}</strong>
            <ul className="m-0 list-none pl-0 text-muted" role="list">
              {chapter.files.filter(other => other.fileId !== file.fileId).map(other => <li key={other.fileId} className="wrap-anywhere">{other.relativePath}</li>)}
              {chapter.files.length === 1 && <li>No other indexed release</li>}
            </ul>
          </li>)}
          {!covered.length && <li className="text-sm text-muted">Chapter coverage is unknown.</li>}
        </ul>
      </details>}
      {error && <p role="alert" className="m-0 text-danger">{error}</p>}
      {!previews && <>
        {candidates.length ? <>
          <p className="m-0 text-sm text-muted">Mark copies for quarantine. Each one keeps the listed file with the same indexed bytes and coverage.</p>
          <div className="flex flex-wrap items-center gap-2">
            <Button variant="secondary" onClick={() => setMarked(candidates.slice(0, MAX_MARKED_FILES).map(item => item.source.fileId))}>Mark exact copies</Button>
            {marked.length > 0 && <Button variant="ghost" onClick={() => setMarked([])}>Clear marks</Button>}
          </div>
          <ul role="list" className="m-0 grid list-none gap-2 p-0">
            {candidates.map(({ source, keep }) => <li key={source.fileId} className="rounded-lg border border-line p-2.5">
              <label className="flex min-h-11 items-start gap-2 text-sm">
                <input type="checkbox" className="min-h-0 p-0" checked={marked.includes(source.fileId)}
                  disabled={busy || (marked.length >= MAX_MARKED_FILES && !marked.includes(source.fileId))}
                  onChange={event => setMarked(previous => event.target.checked ? [...previous, source.fileId] : previous.filter(id => id !== source.fileId))} />
                <span className="min-w-0 wrap-anywhere"><strong className="block">{source.relativePath}</strong><span className="text-muted">Keep {keep.relativePath}</span></span>
              </label>
            </li>)}
          </ul>
          <Button icon={ShieldCheck} className="justify-self-start" disabled={busy || !marked.length} onClick={() => void preview()}>Preview {marked.length} marked</Button>
        </> : <p className="m-0 text-sm text-muted">{canReview ? 'No exact copies with matching whole-file coverage in this review.' : 'Quarantine is available in scanned folders with managed file operations enabled.'}</p>}
      </>}
      {previews && <section aria-label="Confirm quarantine" className="grid gap-3">
        <ul role="list" className="m-0 grid list-none gap-2 p-0">
          {previews.map(({ source, keep, plan, error: problem }) => {
            const result = results?.find(item => item.id === plan?.id);
            return <li key={source.fileId} className="grid gap-1 rounded-lg border border-line p-3 text-sm wrap-anywhere">
              <strong>{source.relativePath}</strong>
              <span className="text-muted">Keep {keep.relativePath}</span>
              <span className="text-muted">{source.chapters.length ? `Whole-file coverage · ${source.chapters.map(chapter => chapter.chapter).join(', ')}` : 'Chapter coverage unknown'}</span>
              {plan && <span className="font-mono text-xs text-muted">{plan.target}</span>}
              {(problem || result?.error) && <span role="status" className="text-warning">{problem ?? result?.error}</span>}
              {result?.result?.state === 'completed' && <span className="text-positive">Quarantined · Restore from file operation history</span>}
              {result?.result && result.result.state !== 'completed' && <span className="text-warning">{result.result.message ?? 'Review file operation history.'}</span>}
            </li>;
          })}
        </ul>
        {!results && <>
          {ids.length > 0 && <label className="flex min-h-11 items-center gap-2">
            <input type="checkbox" className="min-h-0 p-0" checked={acknowledged} disabled={busy} onChange={event => setAcknowledged(event.target.checked)} />
            I reviewed the files to quarantine and the copies to retain.
          </label>}
          <div className="action-row">
            {ids.length > 0 && <Button icon={Archive} disabled={busy || !acknowledged} onClick={() => void apply()}>Confirm quarantine</Button>}
            <Button variant="secondary" disabled={busy} onClick={() => setPreviews(null)}>Back to review</Button>
          </div>
        </>}
      </section>}
    </div>
  </Modal>;
}
