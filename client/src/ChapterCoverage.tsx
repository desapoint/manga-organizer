import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import { Button } from './ui';

export function ChapterCoverage({ url, contentId, volumeHint, chapterHint, onSaved, onSignedOut }: {
  url: string; contentId: string; volumeHint: string | null; chapterHint: string | null; onSaved: () => void; onSignedOut: () => void;
}) {
  const [start, setStart] = useState<{ volume: string; chapters: string; source?: string } | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    const request = new AbortController();
    async function load() {
      const response = await fetch(url + '?' + new URLSearchParams({ contentId }), { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const chapters = await response.json() as { volume: string | null; chapter: string; source?: string }[];
      if (request.signal.aborted) return;
      setStart({ volume: chapters.length ? chapters[0]?.volume ?? '' : volumeHint ?? '',
        chapters: chapters.length ? chapters.map(item => item.chapter).join(', ') : chapterHint ?? '', source: chapters[0]?.source });
    }
    void load().catch(() => { if (!request.signal.aborted) setError('Could not load chapter coverage.'); });
    return () => request.abort();
  }, [url, contentId, volumeHint, chapterHint, onSignedOut]);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    setBusy(true);
    setError('');
    setSaved(false);
    try {
      const response = await fetch(url, { method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: JSON.stringify({ contentId, volume: data.get('volume'), chapters: String(data.get('chapters') ?? '').split(',').map(value => value.trim()).filter(Boolean) }) });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        const failure = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(failure?.message ?? 'Could not save chapter coverage.');
      }
      setSaved(true);
      onSaved();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save chapter coverage.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="grid gap-3">
      {!start && !error && <p role="status">Loading chapter coverage…</p>}
      {start && <form onSubmit={save} className="max-w-2xl">
        <label className="grid gap-1">Volume label (optional)<input name="volume" maxLength={13} defaultValue={start.volume} /></label>
        <label className="grid gap-1">Chapters in this file<input name="chapters" maxLength={6000} defaultValue={start.chapters} placeholder="1, 1.5, 2" /></label>
        {start.source && <p className="m-0 text-sm text-muted">Detected from {start.source === 'archive' ? 'chapter folders in the archive' : 'the chapter range in the filename'}.</p>}
        <p className="m-0 text-sm text-muted">List the individual chapters you verified. Clearing a manual assignment restores any detected coverage.</p>
        <Button type="submit" disabled={busy} className="justify-self-start">Save coverage</Button>
      </form>}
      {saved && <p role="status" className="m-0 text-positive">Chapter coverage saved.</p>}
      {error && <p role="alert" className="m-0 text-danger">{error}</p>}
    </div>
  );
}
