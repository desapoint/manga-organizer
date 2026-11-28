import { BookOpen } from 'lucide-react';
import { useState } from 'react';
import { ROOTS_URL } from './library';
import { Modal } from './ui';

type CoverProps = { rootId: string; revision: string; className?: string };
type SeriesCoverProps = CoverProps & {
  seriesKey: string; available: boolean; preferredSrc?: string; preferredLargeSrc?: string; highResolution?: boolean;
};
type FileCoverProps = CoverProps & { fileId: string; available: boolean; seriesCoverKey: string | null };
const enlargedCover = 'rounded-lg [&_img]:max-h-[70dvh] [&_img]:object-contain';

export function SeriesCover({ seriesKey, available, preferredSrc, preferredLargeSrc, highResolution, ...props }: SeriesCoverProps) {
  if (preferredSrc) return <CoverImage src={highResolution && preferredLargeSrc ? preferredLargeSrc : preferredSrc}
    fallbackSrc={highResolution ? preferredSrc : undefined} className={props.className} />;
  return <Cover {...props} path={`series/${seriesKey}`} available={available} highResolution={highResolution} />;
}

export function SeriesCoverDialog({ title, onClose, wide, ...artwork }: Omit<SeriesCoverProps, 'className'> & {
  title: string; onClose: () => void; wide?: boolean;
}) {
  return <Modal title={title} onClose={onClose} wide={wide}>
    <SeriesCover {...artwork} highResolution className={enlargedCover} />
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

export function ProviderCoverPreview({ src, largeSrc, title, className }: {
  src?: string; largeSrc?: string; title: string; className: string;
}) {
  const [open, setOpen] = useState(false);
  const image = <CoverImage src={src} className={className} />;
  if (!src) return image;

  return <>
    <button type="button" className="group block cursor-pointer rounded-md border-0 bg-transparent p-0"
      aria-label={`Enlarge AniList cover for ${title}`} onClick={() => setOpen(true)}>{image}</button>
    {open && <Modal title={`${title} · AniList cover`} wide onClose={() => setOpen(false)}>
      <CoverImage src={largeSrc ?? src} fallbackSrc={src} className={enlargedCover} />
    </Modal>}
  </>;
}

function Cover({ rootId, path, available, revision, className, title, highResolution }: CoverProps & {
  path?: string; available: boolean; title?: string; highResolution?: boolean;
}) {
  const src = available ? `${ROOTS_URL}/${rootId}/${path}/cover?revision=${encodeURIComponent(revision)}` : undefined;
  return <CoverImage src={highResolution && src ? src + '&highResolution=true' : src}
    fallbackSrc={highResolution ? src : undefined} className={className} title={title} />;
}

export function CoverImage({ src, fallbackSrc, className, title }: { src?: string; fallbackSrc?: string; className?: string; title?: string }) {
  const [failed, setFailed] = useState<string[]>([]);
  const image = [src, fallbackSrc].find(url => url && !failed.includes(url));
  return (
    <span title={title} className={`grid place-items-center overflow-hidden bg-nav-active text-nav-icon ${className ?? ''}`}>
      {image ? <img src={image} alt="" loading="lazy" decoding="async" referrerPolicy="no-referrer" onError={() => setFailed(urls => [...urls, image])}
        className="h-full w-full object-cover transition-transform duration-200 group-hover:scale-[1.025] motion-reduce:transform-none" /> :
        <BookOpen className="size-6" aria-hidden="true" />}
    </span>
  );
}
