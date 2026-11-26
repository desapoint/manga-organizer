import { useState } from 'react';

export type Chapter = { volume: string | null; chapter: string };
type Side = 'source' | 'candidate';
type ChapterComparisonProps = {
  shared: Chapter[];
  sourceOnly: Chapter[];
  candidateOnly: Chapter[];
  labels: Record<Side, string>;
};

const files = {
  source: { marker: 'A', color: 'bg-violet' },
  candidate: { marker: 'B', color: 'bg-mint' },
} as const;
const sides = ['source', 'candidate'] as const;
const numbers = new Intl.Collator(undefined, { numeric: true });

export function ChapterComparison({ shared, sourceOnly, candidateOnly, labels }: ChapterComparisonProps) {
  const [hovered, setHovered] = useState<Side | null>(null);
  const [focused, setFocused] = useState<Side | null>(null);
  const [pinned, setPinned] = useState<Side | null>(null);
  const highlighted = hovered ?? focused ?? pinned;
  const volumes = [
    ...shared.map(chapter => ({ ...chapter, coveredBy: sides })),
    ...sourceOnly.map(chapter => ({ ...chapter, coveredBy: ['source'] as const })),
    ...candidateOnly.map(chapter => ({ ...chapter, coveredBy: ['candidate'] as const })),
  ].sort((a, b) => numbers.compare(a.volume ?? '', b.volume ?? '') || numbers.compare(a.chapter, b.chapter));
  const chaptersByVolume = new Map<string | null, typeof volumes>();
  for (const chapter of volumes) {
    const group = chaptersByVolume.get(chapter.volume);
    if (group) group.push(chapter);
    else chaptersByVolume.set(chapter.volume, [chapter]);
  }

  if (!volumes.length) return <p className="m-0 text-sm text-muted">Chapter coverage unknown for both files.</p>;

  return <div className="grid gap-3">
    <div role="group" aria-label="File coverage legend" className="flex flex-wrap gap-2">
      {sides.map(side => <button key={side} type="button" aria-pressed={pinned === side}
        onPointerEnter={event => { if (event.pointerType !== 'touch') setHovered(side); }} onPointerLeave={() => setHovered(null)}
        onFocus={event => { if (event.currentTarget.matches(':focus-visible')) setFocused(side); }} onBlur={() => setFocused(null)}
        onClick={() => setPinned(value => value === side ? null : side)}
        className={`flex min-h-11 items-center gap-2 rounded-lg border px-2.5 text-left text-sm ${highlighted === side ? 'border-control bg-elevated' : 'border-line bg-inset'}`}>
        <span aria-hidden="true" className={`grid size-5 place-items-center rounded-sm text-xs font-bold text-accent-ink ${files[side].color}`}>{files[side].marker}</span>
        {labels[side]}
      </button>)}
    </div>
    {[...chaptersByVolume].map(([volume, chapters]) => {
      const title = volume ? `Volume ${volume}` : 'Chapters without a volume';
      return <div key={volume} className="grid gap-1.5">
        <strong className="text-sm text-muted">{title}</strong>
        <ul role="list" aria-label={title} className="m-0 grid list-none grid-cols-[repeat(auto-fill,minmax(3.5rem,1fr))] gap-1.5 p-0">
          {chapters.map(({ chapter, coveredBy }) => <li key={chapter} className="min-w-0 overflow-hidden rounded-md border border-control bg-inset text-center">
            <span className="block wrap-anywhere px-1 py-0.5 text-xs font-semibold tabular-nums">Ch. {chapter}</span>
            <div className="flex h-9 flex-col gap-px">
              {coveredBy.map(side => <span key={side} data-file={side}
                className={`grid min-h-0 flex-1 place-items-center text-[0.625rem] leading-none font-bold text-accent-ink motion-safe:transition-opacity ${files[side].color} ${highlighted && highlighted !== side ? 'opacity-25' : ''}`}>
                <span aria-hidden="true">{files[side].marker}</span><span className="sr-only">{labels[side]}</span>
              </span>)}
            </div>
          </li>)}
        </ul>
      </div>;
    })}
  </div>;
}
