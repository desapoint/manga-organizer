import { useId, useState } from 'react';
import type { CSSProperties } from 'react';

export type Chapter = { volume: string | null; chapter: string };
type Side = 'source' | 'candidate';
type FileCoverage = { relativePath: string; volumeArchive: boolean; chapters: Chapter[] };
type ChapterHover = { key: string; position: CSSProperties };
type ChapterComparisonProps = {
  shared: Chapter[];
  sourceOnly: Chapter[];
  candidateOnly: Chapter[];
  labels: Record<Side, string>;
  unresolved: boolean;
  files: Record<Side, FileCoverage>;
};

const singleChapter = { swatch: 'bg-mint', band: 'bg-mint/30', outline: 'border-mint' };
const multiChapter = {
  source: { swatch: 'bg-violet', band: 'bg-violet/30', outline: 'border-violet' },
  candidate: { swatch: 'bg-coral', band: 'bg-coral/30', outline: 'border-coral' },
};
const sides = ['source', 'candidate'] as const;
const kindNames = { single: 'Single chapter', multi: 'Volume or bundle', unknown: 'Coverage unknown' };
const numbers = new Intl.Collator(undefined, { numeric: true });
const percentage = new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 1 });

function fileKind(file: FileCoverage): keyof typeof kindNames {
  if (file.volumeArchive || file.chapters.length > 1) return 'multi';
  return file.chapters.length === 1 ? 'single' : 'unknown';
}

function tooltipPosition(tile: HTMLButtonElement, x?: number, y?: number): CSSProperties {
  const bounds = tile.getBoundingClientRect();
  const dialog = tile.closest('dialog')?.getBoundingClientRect();
  const left = dialog?.left ?? 0;
  const right = dialog?.right ?? window.innerWidth;
  const bottom = dialog?.bottom ?? window.innerHeight;
  const width = Math.min(224, right - left - 16);
  const anchorX = x ?? bounds.right;
  const anchorY = y ?? bounds.bottom;
  const offsetX = Math.max(left + 8, Math.min(anchorX + 10, right - width - 8)) - bounds.left;
  const spaceAbove = anchorY - (dialog?.top ?? 0);
  const above = bottom - anchorY < 110 && spaceAbove > bottom - anchorY;
  return { width, left: offsetX, ...(above ? { bottom: bounds.bottom - anchorY + 12 } : { top: anchorY - bounds.top + 12 }) };
}

export function ChapterComparison({ shared, sourceOnly, candidateOnly, labels, unresolved, files }: ChapterComparisonProps) {
  const detailsId = useId();
  const tooltipId = useId();
  const [activeFile, setActiveFile] = useState<Side | null>(null);
  const [pinnedFile, setPinnedFile] = useState<Side | null>(null);
  const [hoveredChapter, setHoveredChapter] = useState<ChapterHover | null>(null);
  const [focusedChapter, setFocusedChapter] = useState<ChapterHover | null>(null);
  const [pinnedChapter, setPinnedChapter] = useState<string | null>(null);
  const highlighted = activeFile ?? pinnedFile;
  const hoverTarget = hoveredChapter ?? focusedChapter;
  const tooltip = hoverTarget?.key === pinnedChapter ? null : hoverTarget;
  const kinds = { source: fileKind(files.source), candidate: fileKind(files.candidate) };
  const color = (side: Side) => kinds[side] === 'multi' ? multiChapter[side] : singleChapter;
  const chapters = [
    ...shared.map(chapter => ({ ...chapter, coveredBy: sides })),
    ...sourceOnly.map(chapter => ({ ...chapter, coveredBy: ['source'] as const })),
    ...candidateOnly.map(chapter => ({ ...chapter, coveredBy: ['candidate'] as const })),
  ].map(chapter => ({ ...chapter, key: `${chapter.volume ?? ''}:${chapter.chapter}` }))
    .sort((a, b) => numbers.compare(a.volume ?? '', b.volume ?? '') || numbers.compare(a.chapter, b.chapter));
  const chaptersByVolume = new Map<string | null, typeof chapters>();
  for (const chapter of chapters) {
    const group = chaptersByVolume.get(chapter.volume);
    if (group) group.push(chapter);
    else chaptersByVolume.set(chapter.volume, [chapter]);
  }
  const detail = chapters.find(chapter => chapter.key === pinnedChapter);
  const comparable = !unresolved && (shared.length > 0 || (sourceOnly.length > 0 && candidateOnly.length > 0));
  const overlap = comparable ? shared.length / chapters.length : 0;

  return <section aria-label="Chapter comparison" className="grid min-w-0 gap-3 rounded-xl border border-line bg-panel p-3.5 sm:gap-4 sm:p-5">
    <div className="flex flex-wrap items-start justify-between gap-x-5 gap-y-2">
      <div>
        <span className="text-[0.6875rem] font-medium tracking-wide text-muted uppercase">Release comparison</span>
        <h3 className="m-0 text-xl leading-tight font-bold tracking-tight">Chapter coverage</h3>
        <p className="m-0 mt-1 text-xs text-muted">Compare coverage indexed for the two files.</p>
      </div>
      {comparable ? <div>
        <strong className="block text-2xl leading-none font-bold text-positive tabular-nums sm:text-[1.75rem]">{percentage.format(overlap)}</strong>
        <p className="m-0 mt-1 text-xs text-muted">{shared.length} of {chapters.length} chapters shared</p>
      </div> : chapters.length > 0 && <span className="rounded-md border border-warning/35 bg-warning/10 px-2 py-1 text-xs text-warning">Overlap unresolved</span>}
    </div>

    {comparable && <div aria-hidden="true" className="h-2 overflow-hidden rounded-full bg-elevated">
      <div className="h-full rounded-full bg-positive" style={{ width: `${overlap * 100}%` }} />
    </div>}
    {chapters.length > 0 ? <>
      <div className="grid gap-3">
        {[...chaptersByVolume].map(([volume, group]) => {
          const title = volume ? `Volume ${volume}` : 'Chapters without a volume';
          return <div key={volume} className="grid gap-1.5">
            <strong className="text-xs font-semibold text-muted">{title}</strong>
            <ul role="list" aria-label={title} className="m-0 grid list-none grid-cols-[repeat(auto-fill,2.75rem)] gap-1.5 p-0 sm:grid-cols-[repeat(auto-fill,2.375rem)]">
              {group.map(({ chapter, coveredBy, key }) => {
                const current = pinnedChapter === key;
                const showTooltip = tooltip?.key === key;
                const activeCoverage = highlighted && coveredBy.some(side => side === highlighted);
                const border = current || showTooltip ? 'border-accent-readable' : activeCoverage ? color(highlighted).outline : 'border-line';
                return <li key={key} className="relative min-w-0">
                  <button type="button" aria-label={`Chapter ${chapter}, ${title}: ${coveredBy.map(side => labels[side]).join(' and ')}`}
                    aria-pressed={current} aria-controls={detailsId} aria-describedby={showTooltip ? tooltipId : undefined}
                    onPointerEnter={event => {
                      if (event.pointerType !== 'touch') {
                        setFocusedChapter(null);
                        setHoveredChapter({ key, position: tooltipPosition(event.currentTarget, event.clientX, event.clientY) });
                      }
                    }} onPointerLeave={() => setHoveredChapter(null)}
                    onFocus={event => {
                      if (event.currentTarget.matches(':focus-visible')) {
                        setHoveredChapter(null);
                        setFocusedChapter({ key, position: tooltipPosition(event.currentTarget) });
                      }
                    }} onBlur={() => setFocusedChapter(null)}
                    onClick={() => { setPinnedChapter(current ? null : key); setPinnedFile(null); }}
                    className={`relative block size-11 cursor-pointer overflow-hidden rounded-lg border bg-elevated p-0 sm:size-[2.375rem] ${border}`}>
                    <span aria-hidden="true" className="absolute inset-0 flex flex-col gap-px">
                      {coveredBy.map(side => <span key={side} data-file={side}
                        className={`min-h-0 flex-1 motion-safe:transition-opacity ${color(side).band} ${highlighted && highlighted !== side ? 'opacity-25' : ''}`} />)}
                    </span>
                    <span aria-hidden="true" className="absolute inset-0 grid place-items-center px-0.5 text-xs leading-none font-semibold text-ink tabular-nums wrap-anywhere">{chapter}</span>
                  </button>
                  {showTooltip && <div id={tooltipId} role="tooltip" style={tooltip.position}
                    className="pointer-events-none absolute z-10 grid gap-1.5 rounded-lg border border-control bg-elevated p-2.5 text-xs shadow-lg">
                    <strong className="wrap-anywhere">Chapter {chapter}{volume && ` · Vol. ${volume}`}</strong>
                    {coveredBy.map(side => <div key={side} className="flex min-w-0 items-start gap-2">
                      <span aria-hidden="true" className={`mt-1 size-2 shrink-0 rounded-xs ${color(side).swatch}`} />
                      <span>{labels[side]} <span className="text-muted">· {kindNames[kinds[side]]}</span></span>
                    </div>)}
                  </div>}
                </li>;
              })}
            </ul>
          </div>;
        })}
      </div>
      <div id={detailsId}>
        {detail ? <section aria-label="Files for selected chapter" className="grid gap-2.5 rounded-lg border border-line bg-inset p-3">
          <h4 className="m-0 text-sm font-semibold">Chapter {detail.chapter}{detail.volume && ` · Vol. ${detail.volume}`} files</h4>
          <div className="grid gap-2 sm:grid-cols-2">
            {detail.coveredBy.map(side => <button key={side} type="button" aria-pressed={pinnedFile === side}
              onPointerEnter={event => { if (event.pointerType !== 'touch') setActiveFile(side); }} onPointerLeave={() => setActiveFile(null)}
              onFocus={event => { if (event.currentTarget.matches(':focus-visible')) setActiveFile(side); }} onBlur={() => setActiveFile(null)}
              onClick={() => setPinnedFile(value => value === side ? null : side)}
              className={`flex min-h-11 min-w-0 cursor-pointer items-start gap-2 rounded-md border px-2.5 py-2 text-left text-sm ${highlighted === side ? `${color(side).outline} bg-elevated` : 'border-line bg-panel'}`}>
              <span aria-hidden="true" className={`mt-1.5 size-2.5 shrink-0 rounded-xs ${color(side).swatch}`} />
              <span className="min-w-0 wrap-anywhere"><strong className="block text-xs">{files[side].relativePath.split('/').at(-1)}</strong>
                <span className="text-xs text-muted">{labels[side]} · {kindNames[kinds[side]]}</span></span>
            </button>)}
          </div>
        </section> : <p className="m-0 py-1 text-sm text-accent-readable">Select a chapter to review its files.</p>}
      </div>
    </> : <p className="m-0 text-sm text-muted">Chapter coverage unknown for both files.</p>}
  </section>;
}
