import { BookOpen } from 'lucide-react';
import { useState } from 'react';
import { ROOTS_URL } from './library';

type CoverProps = { rootId: string; revision: string; className?: string };

export function SeriesCover({ seriesKey, available, ...props }: CoverProps & { seriesKey: string; available: boolean }) {
  return <Cover {...props} path={`series/${seriesKey}`} available={available} />;
}

export function FileCover({ fileId, ...props }: CoverProps & { fileId: string }) {
  return <Cover {...props} path={`files/${fileId}`} available />;
}

function Cover({ rootId, path, available, revision, className }: CoverProps & { path: string; available: boolean }) {
  const url = `${ROOTS_URL}/${rootId}/${path}/cover?revision=${encodeURIComponent(revision)}`;
  const [failed, setFailed] = useState('');
  return (
    <span className={`grid place-items-center overflow-hidden bg-nav-active text-nav-icon ${className ?? ''}`}>
      {available && failed !== url ? <img src={url} alt="" loading="lazy" decoding="async" onError={() => setFailed(url)}
        className="h-full w-full object-cover transition-transform duration-200 group-hover:scale-[1.025] motion-reduce:transform-none" /> :
        <BookOpen className="size-6" aria-hidden="true" />}
    </span>
  );
}
