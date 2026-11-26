import { ArrowLeft, ChevronLeft, ChevronRight, Eye, EyeOff } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { getCsrfToken } from './api';
import { ActionLink, Button } from './ui';

type Progress = { pageIndex: number; revision: number; completed: boolean };
type Book = { title: string; pageCount: number; progress: Progress };
type Update = { updateId: string; baseRevision: number; pageIndex: number; completed?: boolean };

function updateId() {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6]! & 15) | 64;
  bytes[8] = (bytes[8]! & 63) | 128;
  const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
  return [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)].join('-');
}

export function Reader({ contentId, backToIntake, onSignedOut }: { contentId: string; backToIntake: boolean; onSignedOut: () => void }) {
  const url = '/api/reader/' + contentId;
  const back = backToIntake ? '#intake' : '#library';
  const [book, setBook] = useState<Book | null>(null);
  const [page, setPage] = useState(0);
  const [loaded, setLoaded] = useState<number | null>(null);
  const [showControls, setShowControls] = useState(true);
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
      if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey ||
        event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) return;
      const step = event.key === 'ArrowLeft' ? 1 : event.key === 'ArrowRight' ? -1 : 0;
      if (step) {
        event.preventDefault();
        setPage(current => Math.max(0, Math.min(book!.pageCount - 1, current + step)));
        setPageError(false);
      }
    }
    window.addEventListener('keydown', key);
    return () => window.removeEventListener('keydown', key);
  }, [book]);

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
    <main className="reader-shell min-h-svh bg-reader-canvas text-reader-ink">
      {(showControls || !book) && <header className="sticky top-0 z-10 flex flex-wrap items-center justify-between gap-3 border-b border-line
        bg-reader-panel px-4 pt-[max(.75rem,env(safe-area-inset-top))] pb-3">
        <ActionLink variant="ghost" icon={ArrowLeft} href={back}>{backToIntake ? 'Back to intake' : 'Back to library'}</ActionLink>
        <div className="min-w-0 flex-1 text-center">
          <h1 className="m-0 text-lg leading-tight font-semibold wrap-anywhere">{book?.title ?? 'Reader'}</h1>
          {book && <p className="m-0 text-sm text-reader-muted">Page {page + 1} of {book.pageCount}</p>}
        </div>
        {book && <Button variant="ghost" icon={EyeOff} onClick={() => setShowControls(false)}>Hide controls</Button>}
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
      {book && <div className="mx-auto grid min-h-[70svh] max-w-screen-2xl place-items-center gap-3 px-3 py-3">
        {pageError ? <p role="alert" className="text-danger">This page changed or could not be read. Return to the folder and rescan.</p> :
          <img key={page} src={url + '/pages/' + page} alt={'Page ' + (page + 1) + ' of ' + book.pageCount}
            onLoad={() => { setLoaded(page); savePosition(page); }} onError={() => { setLoaded(null); setPageError(true); }}
            className="max-h-[78svh] w-full max-w-screen-xl object-contain" />}
      </div>}
      {book && showControls && <nav aria-label="Right-to-left reading controls" className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between
        gap-2 border-t border-line bg-reader-panel px-3 pt-2 pb-[max(.5rem,env(safe-area-inset-bottom))]">
        <Button icon={ChevronLeft} variant="secondary" onClick={() => move(1)} disabled={page + 1 >= book.pageCount}>Next page</Button>
        {progress.completed ? <Button variant="ghost" disabled={saving || !!conflict || failed} onClick={() => savePosition(page, false)}>Mark unread</Button> :
          page + 1 === book.pageCount && <Button disabled={saving || !!conflict || failed || loaded !== page}
            onClick={() => savePosition(page, true)}>Mark finished</Button>}
        <Button icon={ChevronRight} variant="secondary" onClick={() => move(-1)} disabled={page === 0}>Previous page</Button>
      </nav>}
      {book && !showControls && <Button icon={Eye} className="fixed end-4 bottom-[max(1rem,env(safe-area-inset-bottom))]"
        onClick={() => setShowControls(true)}>Show controls</Button>}
    </main>
  );
}
