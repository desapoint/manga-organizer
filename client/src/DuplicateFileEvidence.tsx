import { useState } from 'react';
import type { ReactNode } from 'react';
import { CoverageMark } from './CoverageMark';
import { fileName, filePattern } from './duplicateCoverage';
import type { CoverageFile } from './duplicateCoverage';
import { formatSize } from './library';
import { EditReleaseTags } from './ReleaseTagsEditor';
import { FileCover } from './SeriesCover';
import { Button } from './ui';
import { useArchiveEvidence } from './useArchiveEvidence';

function FileEvidence({ file, color, note, onSignedOut, version, actions }: {
  file: CoverageFile; color: string | undefined; note?: string; onSignedOut: () => void; version: string; actions?: ReactNode;
}) {
  const { rootId, fileId } = file;
  const evidence = useArchiveEvidence(rootId, fileId, onSignedOut, version);

  return <article className="grid min-w-0 content-start gap-2.5 rounded-lg border border-line bg-inset p-3">
    <div className="flex min-w-0 items-start gap-2.5">
      <FileCover rootId={rootId} fileId={fileId} available={file.hasCover} seriesCoverKey={null} revision={version}
        className="h-20 w-[3.375rem] shrink-0 rounded" />
      <div className="min-w-0 wrap-anywhere">
        <h4 className="m-0 flex items-baseline gap-1.5 text-sm font-semibold"><CoverageMark pattern={filePattern(file)} color={color} />{fileName(file)}</h4>
        <p className="m-0 mt-1 text-xs text-muted">{file.format.toUpperCase()} · {formatSize(file.size)}{file.volume && ` · Vol. ${file.volume}`}</p>
        <p className="m-0 mt-1 text-xs text-muted">{evidence ? `${evidence.imagePages} image ${evidence.imagePages === 1 ? 'page' : 'pages'} in archive` :
          evidence === undefined ? 'Loading archive details…' : 'Archive page count unavailable'}</p>
        <p className="m-0 mt-1 text-xs text-muted">{file.chapters.length ? `${file.chapters.length} indexed ${file.chapters.length === 1 ? 'chapter' : 'chapters'}` : 'Chapter coverage unknown'}</p>
      </div>
    </div>
    <code className="text-xs text-muted wrap-anywhere">{file.fullPath}</code>
    {note && <p className="m-0 text-xs text-muted">{note}</p>}
    {file.sha256 && <div className="action-row">{actions}<EditReleaseTags rootId={rootId} fileId={fileId} onSignedOut={onSignedOut} /></div>}
    <details open={Boolean(evidence?.comicInfo.length)} className="border-t border-line">
      <summary className="min-h-9 cursor-pointer py-2 text-xs font-semibold">ComicInfo and indexed hash</summary>
      {evidence?.comicInfo.length ? <dl className="m-0 grid grid-cols-[auto_minmax(0,1fr)] gap-x-2 text-xs">
        {evidence.comicInfo.map(({ label, value }) => <div key={label} className="contents"><dt>{label}</dt><dd className="m-0 text-muted wrap-anywhere">{value}</dd></div>)}
      </dl> : <p className="m-0 text-xs text-muted">{evidence ? 'No ComicInfo fields found.' : 'ComicInfo unavailable.'}</p>}
      {file.sha256 && <code className="mt-2 block text-xs text-muted wrap-anywhere">SHA-256 · {file.sha256}</code>}
    </details>
  </article>;
}

export function DuplicateFileEvidence({ files, colors, notes, version, onSignedOut, fileActions }: {
  files: CoverageFile[]; colors: Map<string, string>; notes?: Map<string, string>; version: string; onSignedOut: () => void;
  fileActions?: (file: CoverageFile) => ReactNode;
}) {
  const [limit, setLimit] = useState(6);
  return <div className="grid gap-3">
    <div className="grid gap-3 sm:grid-cols-2">
      {files.slice(0, limit).map(file => <FileEvidence key={file.fileId} file={file} color={colors.get(file.fileId)}
        note={notes?.get(file.fileId)} version={version} onSignedOut={onSignedOut} actions={fileActions?.(file)} />)}
    </div>
    {limit < files.length && <Button variant="secondary" className="justify-self-start" onClick={() => setLimit(value => value + 6)}>Show more files</Button>}
  </div>;
}
