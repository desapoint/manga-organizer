import { useEffect, useState } from 'react';
import { getCsrfToken } from './api';
import { CoverImage } from './SeriesCover';
import { Button } from './ui';

type Choice = { id: string; label: string };
type Options = { total: number; items: Choice[]; preferredLabel: string | null; aniListCoverUrl: string | null };
const pageSize = 12;
const maxImageBytes = 8 * 1024 * 1024;

export function SeriesCoverEditor({ url, published, onSaved, onSignedOut }: {
  url: string;
  published?: boolean;
  onSaved: () => void;
  onSignedOut: () => void;
}) {
  const [options, setOptions] = useState<Options | null>(null);
  const [offset, setOffset] = useState(0);
  const [revision, setRevision] = useState(() => Date.now());
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const scope = '?published=' + String(!!published);

  useEffect(() => {
    const request = new AbortController();
    async function load() {
      const response = await fetch(url + '/cover-options' + scope + '&offset=' + offset + '&limit=' + pageSize, { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const available = await response.json() as Options;
      if (!request.signal.aborted) setOptions(available);
    }
    void load().catch(() => { if (!request.signal.aborted) setError('Could not load cover options.'); });
    return () => request.abort();
  }, [url, scope, offset, revision, onSignedOut]);

  async function saveCover(path: 'cover-preference' | 'cover-upload', change: { candidateId: string } | { useProvider: boolean } | File | null) {
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const headers = { 'X-CSRF-Token': await getCsrfToken() };
      const response = await fetch(url + '/' + path + scope, change instanceof File ? {
        method: 'PUT', headers, body: change,
      } : change ? {
        method: 'PUT', headers: { ...headers, 'Content-Type': 'application/json' }, body: JSON.stringify(change),
      } : { method: 'DELETE', headers });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        const failure = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(failure?.message ?? 'Could not save the series cover. Reload the cover options and try again.');
      }
      setFile(null);
      setRevision(value => value + 1);
      setNotice(change ? 'Series cover saved.' : 'Automatic cover restored.');
      onSaved();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save the series cover.');
    } finally {
      setBusy(false);
    }
  }

  return <div className="grid gap-3 py-2">
    {options && <>
      <div className="flex flex-wrap items-center gap-3">
        <CoverImage src={url + '/cover?revision=' + revision} className="h-24 w-16 rounded-md" title="Current series cover" />
        <div className="min-w-0 flex-1">
          <p className="m-0 text-sm font-semibold">Current series cover</p>
          <p className="m-0 text-sm text-muted wrap-anywhere">{options.preferredLabel ?? 'Automatic cover'}</p>
        </div>
        {options.preferredLabel && <Button variant="secondary" disabled={busy}
          onClick={() => void saveCover('cover-preference', null)}>Use automatic cover</Button>}
      </div>
      {(options.aniListCoverUrl || options.items.length > 0) && <ul className="m-0 grid list-none gap-2 p-0 sm:grid-cols-2" role="list" aria-label="Available covers">
        {options.aniListCoverUrl && offset === 0 && <li>
          <Button variant="secondary" align="start" className="max-w-full whitespace-normal text-start"
            disabled={busy} onClick={() => void saveCover('cover-preference', { useProvider: true })}>
            <CoverImage src={options.aniListCoverUrl} className="h-14 w-10 shrink-0 rounded" /> Use AniList cover
          </Button>
        </li>}
        {options.items.map(choice => <li key={choice.id} className="min-w-0">
          <Button variant="secondary" align="start" className="max-w-full whitespace-normal text-start"
            disabled={busy} onClick={() => void saveCover('cover-preference', { candidateId: choice.id })}>
            <CoverImage src={url + '/cover-options/' + choice.id + scope} className="h-14 w-10 shrink-0 rounded" />
            <span className="min-w-0 wrap-anywhere">{choice.label}</span>
          </Button>
        </li>)}
      </ul>}
      {options.total > pageSize && <div className="action-row" aria-label="Cover pages">
        <Button variant="secondary" disabled={busy || offset === 0} onClick={() => setOffset(offset - pageSize)}>Previous</Button>
        <span className="text-sm text-muted">{offset + 1}–{Math.min(offset + pageSize, options.total)} of {options.total}</span>
        <Button variant="secondary" disabled={busy || offset + pageSize >= options.total} onClick={() => setOffset(offset + pageSize)}>Next</Button>
      </div>}
    </>}
    {!options && !error && <p role="status" className="m-0 text-sm text-muted">Loading covers…</p>}
    <label className="grid min-w-0 gap-1 text-sm">Upload a cover
      <input key={revision} type="file" accept="image/jpeg,image/png,image/webp" disabled={busy}
        className="min-w-0" onChange={event => {
          const image = event.target.files?.[0] ?? null;
          setFile(image && image.size <= maxImageBytes ? image : null);
          setError(image && image.size > maxImageBytes ? 'Choose an image up to 8 MiB.' : '');
        }} />
    </label>
    <Button variant="secondary" className="justify-self-start" disabled={busy || !file}
      onClick={() => { if (file) void saveCover('cover-upload', file); }}>Upload and use cover</Button>
    {notice && <p role="status" className="m-0 text-sm text-muted">{notice}</p>}
    {error && <p role="alert" className="m-0 text-sm text-danger">{error}</p>}
  </div>;
}
