import { useState } from 'react';

export type Chapter = { volume: string | null; chapter: string };
type Side = 'source' | 'candidate';
type FileCoverage = { volumeArchive: boolean; chapters: Chapter[] };
type ChapterComparisonProps = {
  shared: Chapter[];
  sourceOnly: Chapter[];
  candidateOnly: Chapter[];
  labels: Record<Side, string>;
  unresolved: boolean;
  files: Record<Side, FileCoverage>;
};

const singleChapter = { swatch: 'bg-mint', band: 'bg-mint/30' };
const multiChapter = {
  source: { swatch: 'bg-violet', band: 'bg-violet/30' },
  candidate: { swatch: 'bg-coral', band: 'bg-coral/30' },
};
const sides = ['source', 'candidate'] as const;
const legendGroups = [
  { kind: 'single', title: 'Single-chapter files' },
  { kind: 'multi', title: 'Volumes and chapter bundles' },
] as const;
const numbers = new Intl.Collator(undefined, { numeric: true });
const percentage = new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 1 });

function fileKind(file: FileCoverage) {
  if (file.volumeArchive || file.chapters.length > 1) return 'multi';
  return file.chapters.length === 1 ? 'single' : 'unknown';
}

export function ChapterComparison({ shared, sourceOnly, candidateOnly, labels, unresolved, files }: ChapterComparisonProps) {
  const [hoveredFile, setHoveredFile] = useState<Side | null>(null);
  const [focusedFile, setFocusedFile] = useState<Side | null>(null);
  const [pinnedFile, setPinnedFile] = useState<Side | null>(null);
  const [hoveredChapter, setHoveredChapter] = useState<string | null>(null);
  const [focusedChapter, setFocusedChapter] = useState<string | null>(null);
  const [pinnedChapter, setPinnedChapter] = useState<string | null>(null);
  const highlighted = hoveredFile ?? focusedFile ?? pinnedFile;
  const inspected = hoveredChapter ?? focusedChapter ?? pinnedChapter;
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
  const detail = chapters.find(chapter => chapter.key === inspected);
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
    <div role="group" aria-label="File coverage legend" className="grid gap-2 sm:grid-cols-2 sm:gap-4">
      {legendGroups.map(({ kind, title }) => {
        const entries = sides.filter(side => kinds[side] === kind);
        return <div key={kind} role="group" aria-label={title} className="min-w-0 rounded-lg border border-line px-2.5 pt-2 pb-1">
          <strong className="block text-xs font-semibold text-muted">{title}</strong>
          <div className="flex flex-wrap items-center gap-x-2">
            {entries.length ? entries.map(side => <button key={side} type="button" aria-pressed={pinnedFile === side}
              disabled={!files[side].chapters.length}
              onPointerEnter={event => { if (!event.currentTarget.disabled && event.pointerType !== 'touch') setHoveredFile(side); }}
              onPointerLeave={() => setHoveredFile(null)}
              onFocus={event => { if (event.currentTarget.matches(':focus-visible')) setFocusedFile(side); }} onBlur={() => setFocusedFile(null)}
              onClick={() => setPinnedFile(value => value === side ? null : side)}
              className={`inline-flex min-h-11 items-center gap-1.5 rounded-md border-0 px-1.5 text-left text-xs font-semibold enabled:cursor-pointer disabled:opacity-70 sm:min-h-9 ${highlighted === side ? 'bg-elevated text-ink' : 'bg-transparent text-detail'}`}>
              <span aria-hidden="true" className={`size-2.5 shrink-0 rounded-xs ${color(side).swatch}`} />{labels[side]}
            </button>) : <span className="flex min-h-8 items-center px-1.5 text-xs text-muted sm:min-h-9">None</span>}
          </div>
        </div>;
      })}
    </div>
    {chapters.length > 0 ? <>
      <div className="grid gap-3">
        {[...chaptersByVolume].map(([volume, group]) => {
          const title = volume ? `Volume ${volume}` : 'Chapters without a volume';
          return <div key={volume} className="grid gap-1.5">
            <strong className="text-xs font-semibold text-muted">{title}</strong>
            <ul role="list" aria-label={title} className="m-0 grid list-none grid-cols-[repeat(auto-fill,2.75rem)] gap-1.5 p-0 sm:grid-cols-[repeat(auto-fill,2.375rem)]">
              {group.map(({ chapter, coveredBy, key }) => <li key={key} className="min-w-0">
                <button type="button" aria-label={`Chapter ${chapter}, ${title}: ${coveredBy.map(side => labels[side]).join(' and ')}`}
                  aria-pressed={pinnedChapter === key}
                  onPointerEnter={event => { if (event.pointerType !== 'touch') setHoveredChapter(key); }} onPointerLeave={() => setHoveredChapter(null)}
                  onFocus={event => { if (event.currentTarget.matches(':focus-visible')) setFocusedChapter(key); }} onBlur={() => setFocusedChapter(null)}
                  onClick={() => setPinnedChapter(value => value === key ? null : key)}
                  className={`relative block size-11 cursor-pointer overflow-hidden rounded-lg border bg-elevated p-0 sm:size-[2.375rem] ${inspected === key ? 'border-accent-readable' : 'border-line'}`}>
                  <span aria-hidden="true" className="absolute inset-0 flex flex-col gap-px">
                    {coveredBy.map(side => <span key={side} data-file={side}
                      className={`min-h-0 flex-1 motion-safe:transition-opacity ${color(side).band} ${highlighted && highlighted !== side ? 'opacity-25' : ''}`} />)}
                  </span>
                  <span aria-hidden="true" className="absolute inset-0 grid place-items-center px-0.5 text-xs leading-none font-semibold text-ink tabular-nums wrap-anywhere">{chapter}</span>
                </button>
              </li>)}
            </ul>
          </div>;
        })}
      </div>
      <p className="m-0 min-h-9 py-1 text-sm text-accent-readable sm:min-h-7">
        {detail ? <><strong>Chapter {detail.chapter}</strong>{detail.volume && ` · Vol. ${detail.volume}`} · {detail.coveredBy.map(side => labels[side]).join(' + ')}</> : 'Hover or select a chapter to inspect coverage.'}
      </p>
    </> : <p className="m-0 text-sm text-muted">Chapter coverage unknown for both files.</p>}
  </section>;
}
