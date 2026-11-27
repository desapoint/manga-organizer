import { Check, ExternalLink } from 'lucide-react';
import { useState } from 'react';
import { getCsrfToken } from './api';
import type { Series } from './CatalogInventory';
import { CoverImage } from './SeriesCover';
import { AniListSearchAction, MetadataDescription } from './SeriesInfo';
import type { Metadata } from './SeriesInfo';
import { ActionLink, Button, Modal } from './ui';

export type IntakeAniListMatch = { metadata: Metadata; revision: string | null; close: boolean };
type Props = { series: Series; onSignedOut: () => void; onSaved: () => void };

async function confirmMatch(series: Series, onSignedOut: () => void, token: string) {
  const match = series.aniList;
  if (!match?.revision || !match.metadata.providerId) return false;
  const response = await fetch(`/api/metadata-suggestions/${series.rootId}/${series.key}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': token },
    body: JSON.stringify({ revision: match.revision, providerId: match.metadata.providerId }),
  });
  if (response.status === 401) {
    onSignedOut();
    return false;
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { message?: string } | null;
    throw new Error(problem?.message ?? 'Could not confirm this match.');
  }
  return true;
}

export function IntakeSeriesMatch({ series, onSignedOut, onSaved }: Props) {
  const match = series.aniList;
  const metadata = match?.metadata;
  const [details, setDetails] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  async function apply() {
    setBusy(true);
    setError('');
    try {
      if (await confirmMatch(series, onSignedOut, await getCsrfToken())) {
        setDetails(false);
        onSaved();
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not confirm this match.');
    } finally {
      setBusy(false);
    }
  }

  return <div className="flex min-w-0 flex-wrap items-center justify-end gap-2">
    {error && <p role="alert" className="m-0 text-sm text-danger">{error}</p>}
    {metadata ? <button type="button" onClick={() => setDetails(true)} aria-label={`AniList details for ${metadata.title}`}
      className="flex min-h-11 min-w-0 cursor-pointer items-center gap-2 rounded-lg border-0 bg-transparent p-1 text-start text-ink hover:bg-nav-hover">
      <CoverImage src={metadata.providerDetails?.coverUrl ?? undefined} className="h-12 w-8 shrink-0 rounded-sm" />
      <span className="min-w-0 wrap-anywhere">
        <strong className="block text-sm">{metadata.title}</strong>
        <span className="text-xs text-muted">{match.revision ? match.close ? 'Close AniList match' : 'AniList suggestion' : 'Linked to AniList'}</span>
      </span>
    </button> : series.lookupQueued && <span className="text-xs text-muted">AniList lookup queued</span>}
    {match?.revision && <Button icon={Check} variant="secondary" disabled={busy} onClick={() => void apply()}>Confirm match</Button>}
    <AniListSearchAction rootId={series.rootId} seriesKey={series.key} title={series.title} lookupTitle={series.lookupTitle} iconOnly
      onChange={onSaved} onSignedOut={onSignedOut} />
    {details && metadata && <Modal title={metadata.title} wide onClose={() => { if (!busy) setDetails(false); }}>
      <div className="grid gap-3 sm:grid-cols-[auto_minmax(0,1fr)]">
        <CoverImage src={metadata.providerDetails?.coverUrl ?? undefined} className="h-36 w-24 rounded-lg" />
        <MetadataDescription metadata={metadata} />
      </div>
      <div className="action-row">
        {metadata.providerUrl && <ActionLink variant="secondary" icon={ExternalLink} href={metadata.providerUrl}
          target="_blank" rel="noreferrer">Open AniList</ActionLink>}
        {match.revision && <Button icon={Check} disabled={busy} onClick={() => void apply()}>Confirm match</Button>}
      </div>
      {error && <p role="alert" className="m-0 text-sm text-danger">{error}</p>}
    </Modal>}
  </div>;
}

export function ReviewIntakeMatches({ series, onSignedOut, onSaved, onClose }: Omit<Props, 'series'> & { series: Series[]; onClose: () => void }) {
  const [selected, setSelected] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [outcome, setOutcome] = useState<{ saved: number; errors: string[] } | null>(null);

  async function apply() {
    setBusy(true);
    const errors: string[] = [];
    let saved = 0;
    try {
      const token = await getCsrfToken();
      for (const item of series.filter(item => selected.includes(item.key))) {
        try {
          if (!await confirmMatch(item, onSignedOut, token)) break;
          saved++;
        } catch (reason) {
          const message = reason instanceof Error ? reason.message : 'Could not confirm match.';
          errors.push(`${item.title} · ${message}`);
        }
      }
      if (saved) onSaved();
    } catch {
      errors.push('Could not start confirmation. Refresh and try again.');
    } finally {
      setOutcome({ saved, errors });
      setBusy(false);
    }
  }

  return <Modal title="Review close AniList matches" wide onClose={() => { if (!busy) onClose(); }}>
    {outcome ? <>
      <p role="status" className="m-0">{outcome.saved} series confirmed.</p>
      {outcome.errors.map(message => <p key={message} role="alert" className="m-0 text-sm text-danger">{message}</p>)}
      <Button variant="secondary" onClick={onClose}>Done</Button>
    </> : <>
      <Button variant="secondary" disabled={busy} onClick={() => setSelected(series.map(item => item.key))}>Select all on this page</Button>
      <ul className="m-0 grid max-h-80 list-none divide-y divide-line overflow-y-auto p-0" role="list">
        {series.map(item => <li key={item.key} className="py-2">
          <label className="flex min-h-11 items-center gap-2 text-sm wrap-anywhere">
            <input type="checkbox" checked={selected.includes(item.key)} disabled={busy} onChange={event => {
              setSelected(current => event.target.checked ? [...current, item.key] : current.filter(id => id !== item.key));
            }} />
            <span><strong className="block">{item.title}</strong><span className="text-muted">AniList · {item.aniList?.metadata.title}</span></span>
          </label>
        </li>)}
      </ul>
      <Button icon={Check} disabled={busy || selected.length === 0}
        onClick={() => void apply()}>{busy ? 'Confirming…' : `Confirm selected (${selected.length})`}</Button>
    </>}
  </Modal>;
}
