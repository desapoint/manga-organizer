import { useId, useState } from 'react';
import type { CSSProperties } from 'react';
import { CoverageMark } from './CoverageMark';
import { coverageLabels, fileKind, fileName, filePattern } from './duplicateCoverage';
import type { CoverageChapter } from './duplicateCoverage';

type ChapterHover = { key: string; position: CSSProperties };

function tooltipPosition(tile: HTMLButtonElement, x?: number, y?: number): CSSProperties {
  const bounds = tile.getBoundingClientRect();
  const dialog = tile.closest('dialog')?.getBoundingClientRect();
  const left = dialog?.left ?? 0;
  const right = dialog?.right ?? window.innerWidth;
  const bottom = dialog?.bottom ?? window.innerHeight;
  const width = Math.min(270, right - left - 16);
  const anchorX = x ?? bounds.right;
  const anchorY = y ?? bounds.bottom;
  const offsetX = Math.max(left + 8, Math.min(anchorX + 10, right - width - 8)) - bounds.left;
  const placeAbove = bottom - anchorY < 180 && anchorY - (dialog?.top ?? 0) > bottom - anchorY;
  return { width, left: offsetX, ...(placeAbove ? { bottom: bounds.bottom - anchorY + 12 } : { top: anchorY - bounds.top + 12 }) };
}

export function ChapterComparison({ chapters, colors, selected, highlighted, onSelect }: {
  chapters: CoverageChapter[];
  colors: Map<string, string>;
  selected: string;
  highlighted: string;
  onSelect: (key: string) => void;
}) {
  const tooltipId = useId();
  const [hover, setHover] = useState<ChapterHover | null>(null);
  return <ul role="list" aria-label="All chapter files" className="m-0 grid list-none grid-cols-[repeat(auto-fill,2.75rem)] gap-1.5 p-0 sm:grid-cols-[repeat(auto-fill,2.375rem)]">
    {chapters.map(({ chapter, volume, files, state, key }) => {
      const current = selected === key;
      const tooltip = !current && hover?.key === key ? hover : null;
      const activeFile = files.find(file => file.fileId === highlighted);
      const status = coverageLabels[state];
      return <li key={key} className="relative min-w-0">
        <button type="button" aria-pressed={current} aria-describedby={tooltip ? tooltipId : undefined}
          aria-label={`Chapter ${chapter}${volume ? `, volume ${volume}` : ''}: ${status}, ${files.length} ${files.length === 1 ? 'file' : 'files'}`}
          onPointerEnter={event => { if (event.pointerType !== 'touch') setHover({ key, position: tooltipPosition(event.currentTarget, event.clientX, event.clientY) }); }}
          onPointerLeave={() => setHover(null)}
          onFocus={event => { if (event.currentTarget.matches(':focus-visible')) setHover({ key, position: tooltipPosition(event.currentTarget) }); }}
          onBlur={() => setHover(null)}
          onClick={() => { setHover(null); onSelect(key); }}
          style={activeFile ? { borderColor: colors.get(activeFile.fileId) } : undefined}
          className={`relative block size-11 cursor-pointer overflow-hidden rounded-lg border p-0 sm:size-[2.375rem]
            ${state === 'missing' ? 'bg-danger/10' : 'bg-elevated'}
            ${current ? 'border-accent-readable ring-1 ring-accent-readable' : tooltip ? 'border-control' : 'border-line'}`}>
          <span aria-hidden="true" className="absolute inset-0 flex flex-col">
            {files.map(file => <CoverageMark key={file.fileId} fileId={file.fileId} pattern={filePattern(file)} color={colors.get(file.fileId)} variant="band"
              dimmed={Boolean(highlighted) && highlighted !== file.fileId} />)}
          </span>
          <span aria-hidden="true" className="coverage-label absolute inset-0 grid place-items-center px-0.5 text-xs leading-none font-semibold tabular-nums wrap-anywhere">{chapter}</span>
        </button>
        {tooltip && <div id={tooltipId} role="tooltip" style={tooltip.position}
          className="pointer-events-none absolute z-20 grid max-h-40 gap-1.5 overflow-hidden rounded-lg border border-control bg-elevated p-2.5 text-xs shadow-lg">
          <strong>Chapter {chapter}{volume && ` · Vol. ${volume}`}</strong>
          {files.slice(0, 3).map(file => <div key={file.fileId} className="flex min-w-0 items-start gap-2">
            <CoverageMark pattern={filePattern(file)} color={colors.get(file.fileId)} />
            <div className="min-w-0"><div className="truncate">{fileName(file)}</div><div className="text-muted">{fileKind(file)}</div></div>
          </div>)}
          {files.length > 3 && <span className="text-muted">{files.length - 3} more files · Select chapter to see all</span>}
          {!files.length && <span className="text-muted">Expected chapter · No indexed file</span>}
        </div>}
      </li>;
    })}
  </ul>;
}
