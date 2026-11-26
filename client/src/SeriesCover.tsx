import { BookOpen } from 'lucide-react';
import { useState } from 'react';
import { ROOTS_URL } from './library';

export function SeriesCover({ rootId, seriesKey, available, revision, className }: {
  rootId: string; seriesKey: string; available: boolean; revision: string; className?: string;
}) {
  const url = `${ROOTS_URL}/${rootId}/series/${seriesKey}/cover?revision=${encodeURIComponent(revision)}`;
  const [failed, setFailed] = useState('');
  return (
    <span className={`grid place-items-center overflow-hidden bg-nav-active text-nav-icon ${className ?? ''}`}>
      {available && failed !== url ? <img src={url} alt="" loading="lazy" decoding="async" onError={() => setFailed(url)}
        className="h-full w-full object-cover transition-transform duration-200 group-hover:scale-[1.025] motion-reduce:transform-none" /> :
        <BookOpen className="size-6" aria-hidden="true" />}
    </span>
  );
}
