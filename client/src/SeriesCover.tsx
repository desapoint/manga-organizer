import { BookOpen } from 'lucide-react';
import { useState } from 'react';
import { ROOTS_URL } from './library';
import { Modal } from './ui';

type CoverProps = { rootId: string; revision: string; className?: string };
type SeriesCoverProps = CoverProps & { seriesKey: string; available: boolean; preferredSrc?: string };
type FileCoverProps = CoverProps & { fileId: string; available: boolean; seriesCoverKey: string | null };
const enlargedCover = 'rounded-lg [&_img]:max-h-[70dvh] [&_img]:object-contain';

export function SeriesCover({ seriesKey, available, preferredSrc, ...props }: SeriesCoverProps) {
  if (preferredSrc) return <CoverImage src={preferredSrc} className={props.className} />;
  return <Cover {...props} path={`series/${seriesKey}`} available={available} />;
}

export function SeriesCoverDialog({ title, onClose, wide, ...artwork }: Omit<SeriesCoverProps, 'className'> & {
  title: string; onClose: () => void; wide?: boolean;
}) {
  return <Modal title={title} onClose={onClose} wide={wide}>
    <SeriesCover {...artwork} className={enlargedCover} />
  </Modal>;
}

export function FileCover({ fileId, available, seriesCoverKey, ...props }: FileCoverProps) {
  const path = available ? `files/${fileId}` : seriesCoverKey ? `series/${seriesCoverKey}` : undefined;
  const title = available ? 'File cover' : seriesCoverKey ? 'Series cover' : 'No cover';
  return <Cover {...props} path={path} available={Boolean(path)} title={title} />;
}

export function FileCoverDialog({ title, onClose, ...artwork }: Omit<FileCoverProps, 'className'> & {
  title: string; onClose: () => void;
}) {
  return <Modal title={`${title} cover`} onClose={onClose}>
    <FileCover {...artwork} className={enlargedCover} />
  </Modal>;
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
