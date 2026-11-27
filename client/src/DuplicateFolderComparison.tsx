import { Archive, Folders } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { DuplicateBatch, exactCopyPairs } from './DuplicateBatch';
import { chapterKey } from './duplicateCoverage';
import type { CoverageFile } from './duplicateCoverage';
import { formatSize } from './library';
import type { LibraryRoot, SeriesLocation } from './library';
import { Select } from './Select';
import { ActionLink, Button, SectionHeader } from './ui';

type Folder = { id: string; rootId: string; directory: string; label: string; files: CoverageFile[] };
type Comparison = { indexes: Set<string>; hashes: Set<string>; volumes: Set<string> };

function groupFolders(files: CoverageFile[], roots: LibraryRoot[] | null) {
  const folders = new Map<string, Folder>();
  for (const file of files) {
    const segments = file.relativePath.split('/').slice(0, -1);
    const directories = segments.length ? segments.map((_, index) => segments.slice(0, index + 1).join('/')) : [''];
    const root = roots?.find(root => root.id === file.rootId);
    for (const directory of directories) {
      const id = file.rootId + '/' + directory;
      const folder = folders.get(id) ?? {
        id, rootId: file.rootId, directory,
        label: `${root?.name || root?.path || file.rootId} · ${directory || 'Root files'}`, files: [],
      };
      folder.files.push(file);
      folders.set(id, folder);
    }
  }
  return [...folders.values()].sort((first, second) => first.label.localeCompare(second.label));
}

function disjoint(first: Folder, second: Folder) {
  if (first.rootId !== second.rootId) return true;
  if (first.directory === second.directory) return false;
  // The root-files option contains only files directly in the configured folder.
  return !first.directory || !second.directory ||
    !first.directory.startsWith(second.directory + '/') && !second.directory.startsWith(first.directory + '/');
}

function evidence(files: CoverageFile[]): Comparison {
  return {
    indexes: new Set(files.flatMap(file => file.chapters.map(chapterKey))),
    hashes: new Set(files.flatMap(file => file.sha256 ? [file.sha256] : [])),
    volumes: new Set(files.flatMap(file => file.volumeArchive && file.volume ? [file.volume] : [])),
  };
}

function counterpart(file: CoverageFile, other: Comparison) {
  if (file.sha256 && other.hashes.has(file.sha256)) return 'Same indexed bytes';
  if (file.chapters.some(chapter => other.indexes.has(chapterKey(chapter)))) return 'Overlapping chapters · Different bytes';
  if (file.volumeArchive && file.volume && other.volumes.has(file.volume)) return 'Same volume number · Review coverage';
  return 'No verified counterpart';
}

export function DuplicateFolderComparison({ rootId, seriesKey, roots, version, onSignedOut, onChanged }: SeriesLocation & {
  roots: LibraryRoot[] | null; version: string; onSignedOut: () => void; onChanged: () => void;
}) {
  const [files, setFiles] = useState<CoverageFile[] | null>(null);
  const [error, setError] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const [leftChoice, setLeft] = useState('');
  const [rightChoice, setRight] = useState('');
  const [choice, setChoice] = useState<'left' | 'right' | 'both' | null>(null);
  const [review, setReview] = useState<ReturnType<typeof exactCopyPairs> | null>(null);

  useEffect(() => {
    const request = new AbortController();
    setError(false);
    void fetch(`/api/file-operations/duplicates/${rootId}/series/${seriesKey}/folder-files`, { signal: request.signal })
      .then(async response => {
        if (response.status === 401) return onSignedOut();
        if (!response.ok) throw new Error();
        const result = await response.json() as CoverageFile[];
        if (!request.signal.aborted) setFiles(result);
      }).catch(() => { if (!request.signal.aborted) setError(true); });
    return () => request.abort();
  }, [rootId, seriesKey, onSignedOut, version, attempt]);

  const folders = useMemo(() => groupFolders(files ?? [], roots), [files, roots]);
  const choices = folders.filter(folder => folders.some(other => disjoint(folder, other)));
  const first = choices.find(folder => folder.id === leftChoice) ?? choices.find(folder => folder.rootId === rootId) ?? choices[0];
  const otherChoices = first ? choices.filter(folder => disjoint(first, folder)) : [];
  const second = otherChoices.find(folder => folder.id === rightChoice) ?? otherChoices[0];
  if (error) return <div className="action-row">
    <p role="alert" className="m-0 text-danger">Could not load series folders.</p>
    <Button variant="secondary" onClick={() => setAttempt(value => value + 1)}>Retry</Button>
  </div>;
  if (!files) return <p role="status">Loading series folders…</p>;
  if (!first || !second) return <p>Only one separate folder is indexed for this series. Scan the folder containing the other copy.</p>;

  const leftEvidence = evidence(first.files);
  const rightEvidence = evidence(second.files);
  const keep = choice === 'left' ? first : choice === 'right' ? second : null;
  const sources = choice === 'left' ? second : first;
  const sourceRoot = roots?.find(root => root.id === sources.rootId);
  const keepRoot = roots?.find(root => root.id === keep?.rootId);
  const canReview = Boolean(sourceRoot?.managed && sourceRoot.state === 'complete' && keepRoot?.state === 'complete');
  const pairs = keep ? exactCopyPairs(sources.files, keep.files) : [];
  return <section aria-label="Folder comparison" className="grid gap-4">
    <div className="grid gap-3 sm:grid-cols-2">
      <Select label="First folder" value={first.id} options={choices.map(folder => ({ value: folder.id, label: folder.label }))}
        onChange={id => { setLeft(id); setChoice(null); }} />
      <Select label="Second folder" value={second.id} options={otherChoices.map(folder => ({ value: folder.id, label: folder.label }))}
        onChange={id => { setRight(id); setChoice(null); }} />
    </div>
    <div className="grid min-w-0 gap-3 desktop:grid-cols-2">
      <FolderContents key={first.id} folder={first} other={rightEvidence} selected={choice === 'left'} onSelect={() => setChoice('left')} />
      <FolderContents key={second.id} folder={second} other={leftEvidence} selected={choice === 'right'} onSelect={() => setChoice('right')} />
    </div>
    <div className="surface grid gap-2.5 p-3">
      <div className="action-row">
        <Button variant="secondary" icon={Folders} aria-pressed={choice === 'both'} onClick={() => setChoice('both')}>Keep both folders</Button>
        {keep && canReview && pairs.length > 0 && <Button icon={Archive} onClick={() => setReview(pairs)}>Review exact copies to remove</Button>}
      </div>
      {choice === 'both' && <p role="status" className="m-0 text-muted">Both folders selected to keep.</p>}
      {keep && <p role="status" className="m-0 text-sm text-muted">
        Keep <strong className="text-ink">{keep.label}</strong>. Reviewable exact copies in the other folder: {canReview ? pairs.length : 0}.
      </p>}
      {keep && !canReview && <p className="m-0 text-sm text-muted">Quarantine requires a completed scan of both locations and managed file operations for the folder you remove copies from.
        {' '}<ActionLink variant="ghost" href="#settings">Library settings</ActionLink></p>}
    </div>
    {review && <DuplicateBatch scope={{ kind: 'copies', pairs: review }} files={[]} chapters={[]} colors={new Map()}
      version={version} rootId={sources.rootId} canReview={canReview} onSignedOut={onSignedOut}
      onClose={() => setReview(null)} onChanged={() => { setChoice(null); setAttempt(value => value + 1); onChanged(); }} />}
  </section>;
}

function FolderContents({ folder, other, selected, onSelect }: {
  folder: Folder; other: Comparison; selected: boolean; onSelect: () => void;
}) {
  const [limit, setLimit] = useState(20);
  const rows = folder.files.map(file => ({ file, status: counterpart(file, other) }));
  const exact = rows.filter(row => row.status === 'Same indexed bytes').length;
  const unique = rows.filter(row => row.status === 'No verified counterpart').length;
  return <section className={`surface grid min-w-0 content-start gap-3 p-3 ${selected ? 'border-accent-readable' : ''}`}
    aria-label={folder.label}>
    <div className="grid gap-1">
      <SectionHeader title={folder.directory || 'Root files'} />
      <p className="m-0 text-sm text-muted">{rows.length} {rows.length === 1 ? 'file' : 'files'} · {exact} same bytes · {unique} without a verified counterpart</p>
    </div>
    <Button variant="secondary" className="justify-self-start" aria-pressed={selected} onClick={onSelect}>Keep this folder</Button>
    <ul className="m-0 list-none divide-y divide-line p-0" role="list">
      {rows.slice(0, limit).map(({ file, status }) => <li key={file.fileId} className="grid gap-0.5 py-2 text-sm wrap-anywhere">
        <code>{file.relativePath}</code>
        <span className="text-muted">{status} · {formatSize(file.size)}</span>
        <span className="text-muted">{file.chapters.length ? file.chapters.slice(0, 6).map(chapter =>
          chapter.volume ? `Vol. ${chapter.volume} Ch. ${chapter.chapter}` : `Ch. ${chapter.chapter}`).join(', ')
          + (file.chapters.length > 6 ? ` · +${file.chapters.length - 6} more` : '') :
          file.volumeArchive && file.volume ? `Volume ${file.volume} · Chapter coverage unknown` : 'Chapter coverage unknown'}</span>
      </li>)}
    </ul>
    {limit < rows.length && <Button variant="ghost" className="justify-self-start" onClick={() => setLimit(value => value + 20)}>Show more files</Button>}
  </section>;
}
