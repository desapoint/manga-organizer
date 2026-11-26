import { ArrowLeft, ChevronLeft, ChevronRight, Eye, EyeOff, Settings2, ZoomIn, ZoomOut } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { getCsrfToken } from './api';
import { ActionLink, Button, Modal, SegmentedControl } from './ui';

type Progress = { pageIndex: number; revision: number; completed: boolean };
type Book = { title: string; pageCount: number; progress: Progress };
type Update = { updateId: string; baseRevision: number; pageIndex: number; completed?: boolean };
type Fit = 'page' | 'width' | 'original';
const fitKey = 'manga-organizer-reader-fit';
const fitOptions = [{ value: 'page', label: 'Fit page' }, { value: 'width', label: 'Fit width' },
  { value: 'original', label: 'Original size' }] as const;

function readFit(): Fit {
  try {
    const stored = localStorage.getItem(fitKey);
    if (stored === 'width' || stored === 'original') return stored;
  } catch {
    // A blocked storage setting still allows reading with the default fit.
  }
  return 'page';
}

function ReaderImage({ src, alt, fit, zoom, onLoad, onError }: {
  src: string; alt: string; fit: Fit; zoom: number; onLoad: () => void; onError: () => void;
}) {
  const [size, setSize] = useState<{ width: number; height: number } | null>(null);
  const base = size && (fit === 'original' ? `${size.width}px` : fit === 'width' ? '100cqw'
    : `min(100cqw, ${100 * size.width / size.height}cqh)`);

  return <img src={src} alt={alt} draggable={false} className="block h-auto max-w-none shrink-0 select-none"
    style={base ? { width: `calc(${base} * ${zoom / 100})` } : { visibility: 'hidden' }}
    onLoad={event => {
      setSize({ width: event.currentTarget.naturalWidth, height: event.currentTarget.naturalHeight });
      onLoad();
    }} onError={onError} />;
}

function updateId() {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6]! & 15) | 64;
  bytes[8] = (bytes[8]! & 63) | 128;
  const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
  return [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)].join('-');
}

export function Reader({ contentId, back, backLabel, onSignedOut }: {
  contentId: string; back: string; backLabel: string; onSignedOut: () => void;
}) {
  const url = '/api/reader/' + contentId;
  const [book, setBook] = useState<Book | null>(null);
  const [page, setPage] = useState(0);
  const [loaded, setLoaded] = useState<number | null>(null);
  const [showControls, setShowControls] = useState(true);
  const [settings, setSettings] = useState(false);
  const [fit, setFit] = useState(readFit);
  const [zoom, setZoom] = useState(100);
  const [bookError, setBookError] = useState('');
  const [pageError, setPageError] = useState(false);
  const [progress, setProgress] = useState<Progress>({ pageIndex: 0, revision: 0, completed: false });
  const [conflict, setConflict] = useState<Progress | null>(null);
  const [failed, setFailed] = useState(false);
  const [saving, setSaving] = useState(false);
  const latest = useRef(progress);
  const blocked = useRef(false);
  const retry = useRef<Update | null>(null);
  const active = useRef(true);
  const serial = useRef(Promise.resolve());
  const visibilityButton = useRef<HTMLButtonElement>(null);
  const restoreControlsFocus = useRef(false);

  useEffect(() => {
    if (restoreControlsFocus.current) {
      visibilityButton.current?.focus({ preventScroll: true });
      restoreControlsFocus.current = false;
    }
  }, [showControls]);

  function toggleControls() {
    restoreControlsFocus.current = true;
    setShowControls(value => !value);
  }

  function changeFit(value: Fit) {
    setFit(value);
    try {
      localStorage.setItem(fitKey, value);
    } catch {
      // The setting remains active for this reader session.
    }
  }

  useEffect(() => {
    const request = new AbortController();
    active.current = true;

    async function open() {
      const response = await fetch(url, { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        const body = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(body?.message ?? 'Could not open this file.');
      }
      const details = await response.json() as Book;
      if (request.signal.aborted) return;
      latest.current = details.progress;
      setProgress(details.progress);
      setBook(details);
      setPage(Math.min(details.progress.pageIndex, details.pageCount - 1));
    }

    void open().catch(error => {
      if (!request.signal.aborted) setBookError(error instanceof Error ? error.message : 'Could not open this file.');
    });
    return () => {
      active.current = false;
      request.abort();
    };
  }, [url, onSignedOut]);

  function move(amount: number) {
    if (!book) return;
    setPage(current => Math.max(0, Math.min(book.pageCount - 1, current + amount)));
    setPageError(false);
  }

  useEffect(() => {
    if (!book) return;
    function key(event: KeyboardEvent) {
      if (event.defaultPrevented || settings || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey ||
        (event.target instanceof HTMLElement && event.target.closest('input, textarea, select, [contenteditable], dialog'))) return;
      if (event.key === 'Escape' && !showControls) {
        restoreControlsFocus.current = true;
        setShowControls(true);
        return;
      }
      const step = event.key === 'ArrowLeft' ? 1 : event.key === 'ArrowRight' ? -1 : 0;
      if (step) {
        event.preventDefault();
        setPage(current => Math.max(0, Math.min(book!.pageCount - 1, current + step)));
        setPageError(false);
      }
    }
    window.addEventListener('keydown', key);
    return () => window.removeEventListener('keydown', key);
  }, [book, settings, showControls]);

  async function write(update: Update) {
    if (active.current) setSaving(true);
    try {
      const response = await fetch(url + '/progress', {
        method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: JSON.stringify(update),
      });
      if (response.status === 401) {
        onSignedOut();
        return;
      }
      if (response.status === 409) {
        const saved = await response.json() as Progress;
        blocked.current = true;
        retry.current = null;
        if (active.current) { setConflict(saved); setFailed(false); }
        return;
      }
      if (!response.ok) throw new Error();
      const saved = await response.json() as Progress;
      latest.current = saved;
      retry.current = null;
      if (active.current) { setProgress(saved); setFailed(false); }
    } catch {
      retry.current = update;
      if (active.current) setFailed(true);
    } finally {
      if (active.current) setSaving(false);
    }
  }

  function savePosition(pageIndex: number, completed?: boolean) {
    serial.current = serial.current.then(async () => {
      if (blocked.current || retry.current) return;
      const current = latest.current;
      if (completed === undefined && current.revision > 0 && current.pageIndex === pageIndex) return;
      await write({ updateId: updateId(), baseRevision: current.revision, pageIndex, completed });
    });
  }

  function acceptSavedPosition() {
    if (!conflict) return;
    latest.current = conflict;
    setProgress(conflict);
    setPage(conflict.pageIndex);
    setPageError(false);
    setConflict(null);
    blocked.current = false;
  }

  function retryPosition() {
    serial.current = serial.current.then(async () => {
      if (!retry.current || blocked.current) return;
      await write(retry.current);
      if (!retry.current && !blocked.current && loaded !== null && loaded === page && latest.current.pageIndex !== page) {
        await write({ updateId: updateId(), baseRevision: latest.current.revision, pageIndex: page });
      }
    });
  }

  return (
    <main className="reader-shell flex h-dvh flex-col overflow-hidden bg-reader-canvas text-reader-ink
      ps-[env(safe-area-inset-left)] pe-[env(safe-area-inset-right)] pt-[env(safe-area-inset-top)] pb-[env(safe-area-inset-bottom)]">
      {(showControls || !book) && <header className="flex shrink-0 flex-wrap items-center justify-between gap-x-3 gap-y-1 border-b border-line
        bg-reader-panel px-3 py-2">
        <ActionLink variant="ghost" icon={ArrowLeft} href={back}>{backLabel}</ActionLink>
        <div className="order-last w-full min-w-0 text-center sm:order-0 sm:w-auto sm:flex-1">
          <h1 className="m-0 line-clamp-2 text-lg leading-tight font-semibold wrap-anywhere" title={book?.title}>{book?.title ?? 'Reader'}</h1>
          {book && <p className="m-0 text-sm text-reader-muted">Page {page + 1} of {book.pageCount}</p>}
        </div>
        {book && <div className="flex gap-1">
          <Button variant="ghost" icon={Settings2} aria-label="Reader settings" title="Reader settings" onClick={() => setSettings(true)} />
          <Button ref={visibilityButton} variant="ghost" icon={EyeOff} aria-label="Hide controls" title="Hide controls" onClick={toggleControls} />
        </div>}
      </header>}
      {!book && !bookError && <p role="status" className="px-4">Checking archive pages…</p>}
      {bookError && <p role="alert" className="px-4 text-danger">{bookError}</p>}
      {conflict && <div className="mx-auto my-3 flex w-fit flex-wrap items-center gap-3 rounded-xl border border-warning p-3">
        <span role="status">Another device saved page {conflict.pageIndex + 1}.</span>
        <Button onClick={acceptSavedPosition}>Use saved position</Button>
      </div>}
      {failed && <div className="mx-auto my-3 flex w-fit flex-wrap items-center gap-3 rounded-xl border border-warning p-3">
        <span role="alert">Reading progress could not be saved.</span>
        <Button onClick={retryPosition} disabled={saving}>Retry save</Button>
      </div>}
      {book && <div key={page} aria-label="Reading page" className="min-h-0 min-w-0 flex-1 overflow-auto [container-type:size]"
        onClick={event => {
          if (showControls || zoom !== 100 || fit !== 'page' || pageError) return;
          const bounds = event.currentTarget.getBoundingClientRect();
          const position = (event.clientX - bounds.left) / bounds.width;
          if (position < .25) move(1);
          else if (position > .75) move(-1);
          else toggleControls();
        }}>
        <div className="flex min-h-full min-w-full w-max items-center justify-center">
          {pageError ? <p role="alert" className="w-[100cqw] p-4 text-center text-danger">This page changed or could not be read. Return to the folder and rescan.</p> :
            <ReaderImage src={url + '/pages/' + page} alt={'Page ' + (page + 1) + ' of ' + book.pageCount} fit={fit} zoom={zoom}
              onLoad={() => { setLoaded(page); savePosition(page); }} onError={() => { setLoaded(null); setPageError(true); }} />}
        </div>
      </div>}
      {book && showControls && <nav aria-label="Right-to-left reading controls" className="flex shrink-0 flex-wrap items-center justify-between
        gap-2 border-t border-line bg-reader-panel px-3 py-2">
        <Button icon={ChevronLeft} variant="secondary" onClick={() => move(1)} disabled={page + 1 >= book.pageCount}>Next page</Button>
        {progress.completed ? <Button variant="ghost" disabled={saving || !!conflict || failed} onClick={() => savePosition(page, false)}>Mark unread</Button> :
          page + 1 === book.pageCount && <Button disabled={saving || !!conflict || failed || loaded !== page}
            onClick={() => savePosition(page, true)}>Mark finished</Button>}
        <Button icon={ChevronRight} variant="secondary" onClick={() => move(-1)} disabled={page === 0}>Previous page</Button>
      </nav>}
      {book && !showControls && <Button ref={visibilityButton} icon={Eye} aria-label="Show controls" title="Show controls"
        className="fixed end-[max(.75rem,env(safe-area-inset-right))] bottom-[max(.75rem,env(safe-area-inset-bottom))]"
        onClick={toggleControls} />}
      {settings && <Modal title="Reader settings" onClose={() => setSettings(false)} className="bg-reader-panel">
        <div className="grid gap-4">
          <SegmentedControl label="Image fitting" options={fitOptions} value={fit} onChange={changeFit} />
          <div className="action-row" role="group" aria-label="Page zoom">
            <Button variant="secondary" icon={ZoomOut} aria-label="Zoom out" disabled={zoom <= 50} onClick={() => setZoom(zoom - 25)} />
            <output className="min-w-14 text-center tabular-nums" aria-live="polite">{zoom}%</output>
            <Button variant="secondary" icon={ZoomIn} aria-label="Zoom in" disabled={zoom >= 300} onClick={() => setZoom(zoom + 25)} />
            <Button variant="ghost" onClick={() => setZoom(100)} disabled={zoom === 100}>Reset zoom</Button>
          </div>
        </div>
      </Modal>}
    </main>
  );
}
