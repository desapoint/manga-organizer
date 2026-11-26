import { BookOpen } from 'lucide-react';
import { useState } from 'react';
import { ROOTS_URL } from './library';

type CoverProps = { rootId: string; revision: string; className?: string };

export function SeriesCover({ seriesKey, available, ...props }: CoverProps & { seriesKey: string; available: boolean }) {
  return <Cover {...props} path={`series/${seriesKey}`} available={available} />;
}

export function FileCover({ fileId, available, seriesCoverKey, ...props }: CoverProps & {
  fileId: string; available: boolean; seriesCoverKey: string | null;
}) {
  const path = available ? `files/${fileId}` : seriesCoverKey ? `series/${seriesCoverKey}` : undefined;
  const title = available ? 'File cover' : seriesCoverKey ? 'Series cover' : 'No cover';
  return <Cover {...props} path={path} available={Boolean(path)} title={title} />;
}

function Cover({ rootId, path, available, revision, className, title }: CoverProps & { path?: string; available: boolean; title?: string }) {
  const url = `${ROOTS_URL}/${rootId}/${path}/cover?revision=${encodeURIComponent(revision)}`;
  const [failed, setFailed] = useState('');
  return (
    <span title={title} className={`grid place-items-center overflow-hidden bg-nav-active text-nav-icon ${className ?? ''}`}>
      {available && failed !== url ? <img src={url} alt="" loading="lazy" decoding="async" onError={() => setFailed(url)}
        className="h-full w-full object-cover transition-transform duration-200 group-hover:scale-[1.025] motion-reduce:transform-none" /> :
        <BookOpen className="size-6" aria-hidden="true" />}
    </span>
  );
}
