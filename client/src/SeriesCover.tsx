import { BookOpen } from 'lucide-react';
import { useState } from 'react';
import { ROOTS_URL } from './library';

type CoverProps = { rootId: string; revision: string; className?: string };

export function SeriesCover({ seriesKey, available, preferredSrc, ...props }: CoverProps & {
  seriesKey: string; available: boolean; preferredSrc?: string;
}) {
  if (preferredSrc) return <CoverImage src={preferredSrc} className={props.className} />;
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
  const src = available ? `${ROOTS_URL}/${rootId}/${path}/cover?revision=${encodeURIComponent(revision)}` : undefined;
  return <CoverImage src={src} className={className} title={title} />;
}

export function CoverImage({ src, className, title }: { src?: string; className?: string; title?: string }) {
  const [failed, setFailed] = useState('');
  return (
    <span title={title} className={`grid place-items-center overflow-hidden bg-nav-active text-nav-icon ${className ?? ''}`}>
      {src && failed !== src ? <img src={src} alt="" loading="lazy" decoding="async" referrerPolicy="no-referrer" onError={() => setFailed(src)}
        className="h-full w-full object-cover transition-transform duration-200 group-hover:scale-[1.025] motion-reduce:transform-none" /> :
        <BookOpen className="size-6" aria-hidden="true" />}
    </span>
  );
}
