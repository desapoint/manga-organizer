import { Check, ExternalLink } from 'lucide-react';
import { useState } from 'react';
import { getCsrfToken } from './api';
import type { Series } from './CatalogInventory';
import { CoverImage, SeriesCover, SeriesCoverDialog } from './SeriesCover';
import { AniListSearchAction, MetadataDescription, MetadataStatus } from './SeriesInfo';
import type { Metadata } from './SeriesInfo';
import { useToast } from './Toast';
import { ActionLink, Button, Modal } from './ui';

export type IntakeAniListMatch = { metadata: Metadata; revision: string | null; close: boolean };
type Props = { series: Series; onSignedOut: () => void; onSaved: () => void };

async function confirmMatch(series: Series, onSignedOut: () => void, token: string) {
  const match = series.aniList;
  if (!match?.revision || !match.metadata.providerId) return false;
  const response = await fetch(`/api/metadata-suggestions/${series.rootId}/${series.key}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': token },
    body: JSON.stringify({ revision: match.revision, providerId: match.metadata.providerId, reviewFiles: series.reviewFiles }),
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

export function IntakeSeriesMatch({ series, coverVersion, onSignedOut, onSaved }: Props & { coverVersion: string }) {
  const match = series.aniList;
  const metadata = match?.metadata;
  const [details, setDetails] = useState<Series | null>(null);
  const [busy, setBusy] = useState(false);
  const [assigning, setAssigning] = useState(false);
  const notify = useToast();
  const saving = busy || assigning;

  async function apply(review: Series) {
    setBusy(true);
    try {
      if (await confirmMatch(review, onSignedOut, await getCsrfToken())) {
        setDetails(null);
        notify('success', `Linked ${series.title} to AniList.`);
        onSaved();
      }
    } catch (reason) {
      notify('error', reason instanceof Error ? reason.message : 'Could not confirm this match.');
    } finally {
      setBusy(false);
    }
  }

  return <>
    <div className="intake-series-match flex min-w-0 items-center">
      {metadata ? <button type="button" onClick={() => setDetails(series)} aria-label={`AniList details for ${metadata.title}`}
        title={metadata.title} disabled={saving}
        className="flex w-full min-w-0 cursor-pointer items-center gap-2 rounded-lg border-0 bg-transparent px-1 py-0 text-start text-ink
          enabled:hover:bg-nav-hover disabled:cursor-default">
        <CoverImage src={metadata.providerDetails?.coverUrl ?? undefined} className="intake-series-thumbnail" />
        <span className="min-w-0">
          <strong className="block truncate text-sm">{metadata.title}</strong>
          <span className="block truncate text-xs text-muted">{match.revision ? match.close ? 'Close AniList match' : 'AniList suggestion' : 'Linked to AniList'}</span>
        </span>
      </button> : series.lookupQueued && <span className="truncate px-1 text-xs text-muted">AniList lookup queued</span>}
    </div>
    <div className="intake-series-confirm grid min-w-0 justify-items-end">
      {(match?.revision || saving) && <Button icon={Check} variant="secondary" className="w-full max-w-39 px-2"
        disabled={saving} aria-busy={saving} onClick={() => void apply(series)}>
        <span className="truncate">{saving ? 'Linking…' : 'Confirm match'}</span>
      </Button>}
    </div>
    <div className="intake-series-search">
      <AniListSearchAction rootId={series.rootId} seriesKey={series.key} title={series.title} lookupTitle={series.lookupTitle} iconOnly
        reviewFiles={series.reviewFiles} disabled={saving} onAssigningChange={setAssigning} onChange={onSaved} onSignedOut={onSignedOut} />
    </div>
    {details?.aniList && <IntakeMatchDialog series={details} match={details.aniList} coverVersion={coverVersion} busy={busy}
      onClose={() => { if (!busy) setDetails(null); }} onConfirm={() => void apply(details)} />}
  </>;
}

function IntakeMatchDialog({ series, match, coverVersion, busy, onClose, onConfirm }: {
  series: Series; match: IntakeAniListMatch; coverVersion: string; busy: boolean; onClose: () => void; onConfirm: () => void;
}) {
  const [coverOpen, setCoverOpen] = useState(false);
  const { metadata } = match;
  const preferredSrc = metadata.providerDetails?.coverUrl ?? undefined;
  const artwork = { rootId: series.rootId, seriesKey: series.key, available: series.hasCover, preferredSrc,
    preferredLargeSrc: metadata.providerDetails?.coverLargeUrl ?? undefined, revision: coverVersion };
  const canEnlarge = !!preferredSrc || series.hasCover;
  const cover = <SeriesCover {...artwork}
    className="h-36 max-h-[45dvh] w-24 rounded-lg sm:h-54 sm:w-36 [&_img]:object-top" />;

  return <>
    <Modal title={metadata.title} wide onClose={onClose}
      headerClassName="border-b border-line pb-3 sm:pb-4 [&_h2]:wrap-anywhere [&_h2]:text-xl sm:[&_h2]:text-2xl">
      <div className="grid gap-5 sm:grid-cols-[9rem_minmax(0,1fr)] sm:gap-6">
        <aside aria-label="Cover and publication status" className="grid min-w-0 justify-items-start self-start gap-2.5">
          {canEnlarge ? <button type="button" aria-label={`Enlarge cover for ${metadata.title}`}
            className="group block cursor-pointer rounded-lg border-0 bg-transparent p-0" onClick={() => setCoverOpen(true)}>
            {cover}
          </button> : cover}
          {metadata.providerUrl && <ActionLink variant="secondary" icon={ExternalLink} href={metadata.providerUrl}
            target="_blank" rel="noreferrer">Open AniList</ActionLink>}
          <MetadataStatus metadata={metadata} />
        </aside>
        <MetadataDescription metadata={metadata} layout="preview" />
      </div>
      {match.revision && <footer className="action-row mt-4 justify-end border-t border-line pt-4">
        {series.reviewFiles && <p className="m-0 text-end text-sm text-muted">
          This confirms {series.reviewFiles.length.toLocaleString()} reviewed {series.reviewFiles.length === 1 ? 'file' : 'files'}.
        </p>}
        <Button icon={Check} disabled={busy} onClick={onConfirm}>{busy ? 'Confirming…' : 'Confirm match'}</Button>
      </footer>}
    </Modal>
    {coverOpen && <SeriesCoverDialog {...artwork} title={`${metadata.title} cover`} wide onClose={() => setCoverOpen(false)} />}
  </>;
}

export function ReviewIntakeMatches({ series, onSignedOut, onSaved, onClose }: Omit<Props, 'series'> & { series: Series[]; onClose: () => void }) {
  const [selected, setSelected] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const notify = useToast();

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
      if (saved) {
        notify('success', `${saved} series linked to AniList.`);
        onSaved();
      }
    } catch {
      errors.push('Could not start confirmation. Refresh and try again.');
    } finally {
      const [firstError] = errors;
      if (firstError) notify('error', errors.length === 1 ? firstError : `${errors.length} matches could not be confirmed. ${firstError}`);
      if (saved || !errors.length) onClose();
      setBusy(false);
    }
  }

  return <Modal title="Review close AniList matches" wide onClose={() => { if (!busy) onClose(); }}>
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
  </Modal>;
}
