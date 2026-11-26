import { Files, ScanSearch } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { ChapterComparison } from './ChapterComparison';
import { CoverageMark } from './CoverageMark';
import { DuplicateBatch } from './DuplicateBatch';
import type { DuplicateScope } from './DuplicateBatch';
import { DuplicateFileEvidence } from './DuplicateFileEvidence';
import { byteGroups, chapterCoverage, coverageLabels, fileColors, fileKind, fileName, filePattern, isBundle, sameCoverageSpan } from './duplicateCoverage';
import type { Chapter, CoverageFile, CoverageState } from './duplicateCoverage';
import { formatSize } from './library';
import { Select } from './Select';
import { FileCover } from './SeriesCover';
import { Button, Modal, SearchField, SectionHeader } from './ui';

type Review = { files: CoverageFile[]; expected: Chapter[]; canReviewQuarantine: boolean };
type Compared = { candidate: CoverageFile; kind: string; reason: string };
type FileScope = { source: CoverageFile; comparisons: Compared[]; canReviewQuarantine: boolean };
type ReviewProps = {
  rootId: string; seriesKey?: string; fileId?: string; version: string; onSignedOut: () => void; onChanged: () => void;
};

const percentage = new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 1 });
const display = {
  matching: { track: 'bg-positive', text: 'text-positive' },
  multiple: { track: 'bg-violet', text: 'text-accent-readable' },
  single: { track: 'bg-control', text: 'text-ink' },
  missing: { track: 'bg-danger/65', text: 'text-danger' },
};
const states = Object.keys(coverageLabels) as CoverageState[];
const filters = [{ value: 'all', label: 'All chapters' }, ...states.map(value => ({ value, label: coverageLabels[value] }))];

export function DuplicateReviewDialog(props: ReviewProps & { onClose: () => void }) {
  return <Modal title="Compare this file" wide onClose={props.onClose}><DuplicateReview {...props} /></Modal>;
}

export function DuplicateReview({ rootId, seriesKey, fileId, version, onSignedOut, onChanged }: ReviewProps) {
  const [review, setReview] = useState<Review | null>(null);
  const [comparisons, setComparisons] = useState<Compared[]>([]);
  const [error, setError] = useState('');
  const [retry, setRetry] = useState(0);
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState('all');
  const [order, setOrder] = useState('asc');
  const [selectedChapter, setSelectedChapter] = useState('');
  const [selectedFile, setSelectedFile] = useState(fileId ?? '');
  const [hoveredFile, setHoveredFile] = useState('');
  const [lockedFile, setLockedFile] = useState('');
  const [batchScope, setBatchScope] = useState<DuplicateScope | null>(null);
  const [fileLimit, setFileLimit] = useState(30);
  const [groupLimit, setGroupLimit] = useState(15);
  const inspector = useRef<HTMLElement>(null);

  useEffect(() => {
    const request = new AbortController();
    const suffix = fileId ? fileId : 'series/' + seriesKey;
    void (async () => {
      const response = await fetch(`/api/file-operations/duplicates/${rootId}/${suffix}`, { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      if (fileId) {
        const scope = await response.json() as FileScope;
        if (request.signal.aborted) return;
        setComparisons(scope.comparisons);
        setReview({ files: [scope.source, ...scope.comparisons.map(item => item.candidate)], expected: [],
          canReviewQuarantine: scope.canReviewQuarantine });
      } else {
        const scope = await response.json() as Review;
        if (request.signal.aborted) return;
        setReview(scope);
        setComparisons([]);
      }
      setError('');
    })().catch(() => { if (!request.signal.aborted) setError('Could not load duplicate coverage.'); });
    return () => request.abort();
  }, [fileId, onSignedOut, retry, rootId, seriesKey, version]);

  const chapters = useMemo(() => review ? chapterCoverage(review.files, review.expected) : [], [review]);
  const colors = useMemo(() => review ? fileColors(review.files) : new Map<string, string>(), [review]);
  const groups = useMemo(() => review ? byteGroups(review.files) : [], [review]);
  if (!review) return error ? <div className="action-row p-4">
    <p role="alert" className="m-0 text-danger">{error}</p><Button variant="secondary" onClick={() => setRetry(value => value + 1)}>Retry</Button>
  </div> : <p role="status" className="m-0 p-4">Loading duplicate coverage…</p>;

  const active = chapters.find(chapter => chapter.key === selectedChapter);
  const focus = review.files.find(file => file.fileId === fileId);
  const inspected = review.files.find(file => file.fileId === selectedFile);
  const comparison = comparisons.find(item => item.candidate.fileId === selectedFile);
  const highlighted = hoveredFile || lockedFile;
  const counts = { matching: 0, multiple: 0, single: 0, missing: 0 };
  for (const chapter of chapters) counts[chapter.state]++;
  const normalized = query.trim().toLocaleLowerCase();
  const visible = chapters.filter(chapter => (filter === 'all' || filter === chapter.state) && (!normalized ||
    chapter.chapter.includes(normalized) || chapter.files.some(file => file.fullPath.toLocaleLowerCase().includes(normalized))));
  if (order === 'desc') visible.reverse();
  const fileResults = review.files.filter(file => !normalized || file.fullPath.toLocaleLowerCase().includes(normalized) ||
    file.chapters.some(chapter => chapter.chapter.includes(normalized)));
  const visibleGroups = groups.filter(group => group.some(file => fileResults.includes(file)));
  const allFileCount = review.files.length;
  const rate = chapters.length ? counts.matching / chapters.length : 0;
  const compact = focus && (focus.chapters.length > 0 || focus.volumeArchive) &&
    review.files.every(file => file === focus || sameCoverageSpan(focus, file));

  function showInspector() {
    requestAnimationFrame(() => {
      inspector.current?.focus({ preventScroll: true });
      inspector.current?.scrollIntoView({ block: 'nearest' });
    });
  }

  function inspectFile(file: CoverageFile, jumpToInspector: boolean) {
    setSelectedFile(file.fileId);
    setLockedFile(value => value !== file.fileId ? file.fileId : '');
    if (jumpToInspector) showInspector();
  }

  function refresh() {
    setSelectedFile(fileId ?? '');
    setLockedFile('');
    setHoveredFile('');
    setRetry(value => value + 1);
    onChanged();
  }

  const batch = batchScope && <DuplicateBatch scope={batchScope} files={review.files} chapters={chapters} colors={colors} version={version}
    rootId={rootId} canReview={review.canReviewQuarantine && (batchScope.kind === 'chapter' || batchScope.file.rootId === rootId)}
    onClose={() => setBatchScope(null)} onChanged={refresh} onSignedOut={onSignedOut} />;

  if (focus && compact) return <section aria-label="File comparison" className="grid gap-4 p-3">
    {error && <p role="alert" className="m-0 text-danger">{error}</p>}
    <div className="flex flex-wrap items-start justify-between gap-2.5">
      <div>
        <SectionHeader title={isBundle(focus) ? focus.volume ? `Compare volume ${focus.volume}` : 'Compare bundles' : `Compare chapter ${focus.chapters[0]?.chapter}`} />
        <p className="m-0 mt-1 text-xs text-muted">{allFileCount === 1 ? 'No other candidates indexed.' : `${allFileCount - 1} candidate${allFileCount === 2 ? '' : 's'}`}</p>
      </div>
      {allFileCount > 1 && <Button variant="secondary" icon={ScanSearch} onClick={() => setBatchScope({ kind: 'file', file: focus })}>
        {isBundle(focus) ? 'Review volume duplicates' : 'Review exact copies'}
      </Button>}
    </div>
    <DuplicateFileEvidence files={review.files} colors={colors} version={version} onSignedOut={onSignedOut}
      notes={new Map(comparisons.map(item => [item.candidate.fileId, item.reason]))} />
    {batch}
  </section>;

  function fileButton(file: CoverageFile, jumpToInspector = false) {
    return <button key={file.fileId} type="button" aria-pressed={highlighted === file.fileId && lockedFile === file.fileId}
      onPointerEnter={event => { if (event.pointerType !== 'touch') setHoveredFile(file.fileId); }}
      onPointerLeave={() => setHoveredFile('')}
      onFocus={event => { if (event.currentTarget.matches(':focus-visible')) setHoveredFile(file.fileId); }}
      onBlur={() => setHoveredFile('')} onClick={() => inspectFile(file, jumpToInspector)}
      style={highlighted === file.fileId ? { borderColor: colors.get(file.fileId) } : undefined}
      className="flex min-h-11 min-w-0 cursor-pointer items-start gap-2 rounded-md border border-line bg-panel px-2.5 py-2 text-left">
      <CoverageMark pattern={filePattern(file)} color={colors.get(file.fileId)} />
      <span className="min-w-0 wrap-anywhere"><strong className="block text-xs">{fileName(file)}</strong>
        <span className="text-xs text-muted">{fileKind(file)}{file.chapters.length > 0 && ` · ${file.chapters.length} ${file.chapters.length === 1 ? 'chapter' : 'chapters'}`}</span></span>
    </button>;
  }

  return <div className="grid min-w-0 gap-4 bg-canvas p-3 sm:p-4">
    {error && <p role="alert" className="m-0 text-danger">{error}</p>}
    <section aria-label="Chapter duplicate coverage" className="grid min-w-0 gap-4 rounded-xl border border-line bg-panel p-3.5 sm:p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div><span className="text-[0.6875rem] font-medium tracking-wide text-muted uppercase">{fileId ? 'File and candidates' : 'Series integrity'}</span>
          <SectionHeader title="Chapter duplicate coverage" />
          <p className="m-0 mt-1 text-xs text-muted">{focus ? `${fileName(focus)} · ${allFileCount - 1} candidates` :
            `${chapters.length} known chapters · ${allFileCount} indexed files`}</p>
        </div>
        {chapters.length > 0 && <div><strong className="block text-2xl leading-none font-bold text-positive tabular-nums sm:text-[1.75rem]">{percentage.format(rate)}</strong>
          <p className="m-0 mt-1 text-xs text-muted">Chapters with matching indexed bytes</p></div>}
      </div>
      {chapters.length > 0 && <>
        <div aria-hidden="true" className="flex h-2 overflow-hidden rounded-full bg-elevated">
          {states.map(state => <span key={state} className={display[state].track} style={{ width: `${counts[state] / chapters.length * 100}%` }} />)}
        </div>
        <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
          {states.map(state => <span key={state} className="text-muted"><strong className={display[state].text}>{counts[state]}</strong> {coverageLabels[state]}</span>)}
        </div>
      </>}
      <div className="grid items-start gap-4 desktop:grid-cols-[minmax(0,1fr)_minmax(15rem,20rem)]">
        <div className="grid min-w-0 gap-3">
          <h3 className="m-0 text-base font-semibold">All chapters</h3>
          <div className="flex flex-wrap items-end gap-2">
            <SearchField aria-label="Search chapters or file paths" placeholder="Search chapter or path" value={query}
              onChange={event => { setQuery(event.target.value); setFileLimit(30); setGroupLimit(15); }} className="min-w-0 flex-1" />
            <Select label="Chapter status" value={filter} options={filters} onChange={setFilter} />
            <Select label="Chapter order" value={order} options={[{ value: 'asc', label: 'Oldest first' },
              { value: 'desc', label: 'Newest first' }]} onChange={setOrder} />
          </div>
          <div role="group" aria-label="File band styles" className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted">
            <span className="inline-flex items-baseline gap-1.5"><CoverageMark pattern="solid" color="var(--color-control)" />Volume or bundle</span>
            <span className="inline-flex items-baseline gap-1.5"><CoverageMark pattern="striped" color="var(--color-control)" />Standalone chapter</span>
          </div>
          {visible.length ? <ChapterComparison chapters={visible} colors={colors} selected={selectedChapter} highlighted={highlighted}
            onSelect={key => {
              setSelectedChapter(selectedChapter === key ? '' : key);
              setSelectedFile('');
              setLockedFile('');
              if (selectedChapter !== key && !window.matchMedia('(min-width: 50rem)').matches) showInspector();
            }} /> :
            <p className="m-0 text-sm text-muted">{chapters.length ? 'No chapters match the current filters.' : 'No chapter coverage is indexed. The files are listed below.'}</p>}
        </div>
        <aside ref={inspector} tabIndex={-1} aria-label="Chapter and file review" className="grid min-w-0 scroll-mt-5 gap-3 rounded-lg border border-line bg-inset p-3 desktop:sticky desktop:top-3">
          <h3 className="m-0 text-sm font-semibold">{active ? `Chapter ${active.chapter}${active.volume ? ` · Vol. ${active.volume}` : ''}` : 'File review'}</h3>
          {active ? <>
            <p className="m-0 text-xs text-muted">{coverageLabels[active.state]} · {active.files.length} {active.files.length === 1 ? 'file' : 'files'}</p>
            {active.files.length > 1 && <Button variant="secondary" icon={ScanSearch} className="justify-self-start"
              onClick={() => setBatchScope({ kind: 'chapter', chapter: active })}>Review chapter duplicates</Button>}
            <div className="grid gap-1.5">{active.files.map(file => fileButton(file))}</div>
          </> : !inspected && <p className="m-0 text-sm text-muted">Select a chapter to review its files, or select a file from the inventory.</p>}
          {inspected && <div className="grid min-w-0 gap-2 rounded-md border border-line bg-panel p-2.5">
            <div className="flex min-w-0 items-start gap-2">
              <FileCover rootId={inspected.rootId} fileId={inspected.fileId} available={inspected.hasCover} seriesCoverKey={inspected.seriesCoverKey}
                revision={version} className="h-14 w-9 shrink-0 rounded" />
              <div className="min-w-0 wrap-anywhere"><strong className="block text-sm">{fileName(inspected)}</strong>
                <span className="text-xs text-muted">{inspected.format.toUpperCase()} · {formatSize(inspected.size)} · {fileKind(inspected)}</span></div>
            </div>
            <code className="text-xs text-muted wrap-anywhere">{inspected.fullPath}</code>
            <details><summary className="cursor-pointer text-xs text-muted">Content identity</summary>
              <code className="text-xs wrap-anywhere">{inspected.sha256 ?? 'Hash unavailable'}</code>
            </details>
            {comparison && <p className="m-0 text-xs text-muted">{comparison.reason}</p>}
            <Button variant="secondary" icon={ScanSearch} className="justify-self-start" onClick={() => setBatchScope({ kind: 'file', file: inspected })}>
              {isBundle(inspected) ? inspected.chapters.length ? 'Review volume chapters' : 'Review volume duplicates' : 'Review exact copies'}
            </Button>
          </div>}
        </aside>
      </div>
    </section>
    <div className="grid min-w-0 items-start gap-4 desktop:grid-cols-2">
      <section aria-label="Matching byte groups" className="grid min-w-0 gap-3 rounded-xl border border-line bg-panel p-3.5 sm:p-4">
        <SectionHeader title="Matching byte groups" />
        <p className="m-0 text-xs text-muted">Indexed hashes group locations. Quarantine review checks physical identity and whole-file coverage.</p>
        {visibleGroups.slice(0, groupLimit).map(group => <details key={group[0].fileId} className="rounded-lg border border-line px-2.5">
          <summary className="min-h-11 cursor-pointer py-2 text-sm wrap-anywhere">{fileName(group[0])} · {group.length} paths</summary>
          <div className="grid gap-1.5 pb-2">{group.map(file => fileButton(file, true))}</div>
        </details>)}
        {!visibleGroups.length && <p className="m-0 text-sm text-muted">No matching indexed byte groups in this view.</p>}
        {visibleGroups.length > groupLimit && <Button variant="secondary" className="justify-self-start" onClick={() => setGroupLimit(value => value + 15)}>Show more groups</Button>}
      </section>
      <section aria-label="Files in comparison" className="grid min-w-0 gap-3 rounded-xl border border-line bg-panel p-3.5 sm:p-4">
        <SectionHeader title={fileId ? 'Files in comparison' : 'All series files'} />
        <div className="grid gap-1.5">{fileResults.slice(0, fileLimit).map(file => fileButton(file, true))}</div>
        {!fileResults.length && <p className="m-0 text-sm text-muted">No files match the search.</p>}
        {fileResults.length > fileLimit && <Button variant="secondary" icon={Files} className="justify-self-start" onClick={() => setFileLimit(value => value + 30)}>Show more files</Button>}
      </section>
    </div>
    {batch}
  </div>;
}
