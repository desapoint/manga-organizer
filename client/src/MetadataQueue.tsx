import { Check, ExternalLink, RefreshCw } from 'lucide-react';
import { useEffect, useState } from 'react';
import { getCsrfToken } from './api';
import { PageControls, useInventoryPage } from './CatalogInventory';
import type { LibraryRoot } from './library';
import { Select } from './Select';
import { CoverImage, SeriesCover } from './SeriesCover';
import { MetadataDialogAction } from './SeriesInfo';
import type { Metadata } from './SeriesInfo';
import { ActionLink, Button, Modal, SearchField } from './ui';

const endpoint = '/api/metadata-suggestions';

type Proposal = { match: Metadata & { providerId: number }; matchingTitle: string | null };
type QueueRow = {
  rootId: string; seriesKey: string; title: string; hasCover: boolean; queued: boolean; revision: string | null;
  candidates: Proposal[] | null; message: string | null; conflictingMetadata: boolean;
};

export function MetadataQueue({ roots, version, onSignedOut }: {
  roots: LibraryRoot[] | null; version: string; onSignedOut: () => void;
}) {
  const [root, setRoot] = useState('');
  const [search, setSearch] = useState('');
  return <>
    <div className="surface flex flex-wrap items-end gap-3 p-4">
      <SearchField aria-label="Find series missing metadata" placeholder="Search by series title" value={search} maxLength={160}
        onChange={event => setSearch(event.target.value)} className="min-w-0 flex-1" />
      <Select label="Folder" value={root} options={[{ value: '', label: 'All folders' }, ...(roots ?? []).map(folder => ({
        value: folder.id, label: folder.name || folder.path,
      }))]} onChange={setRoot} />
    </div>
    <SuggestionPage key={JSON.stringify([root, search])} root={root} search={search.trim()} version={version}
      roots={roots} onSignedOut={onSignedOut} />
  </>;
}

function SuggestionPage({ root, search, version, roots, onSignedOut }: {
  root: string; search: string; version: string; roots: LibraryRoot[] | null; onSignedOut: () => void;
}) {
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [refreshError, setRefreshError] = useState('');
  const { page, error, offset, setOffset, retry } = useInventoryPage<QueueRow>(endpoint, `${version}:${revision}`,
    onSignedOut, root ? { root, search } : { search });
  const queued = !!page?.items.some(item => item.queued && !item.conflictingMetadata);
  const refreshable = page?.items.filter(item => !item.conflictingMetadata && !item.queued) ?? [];
  const reload = () => setRevision(value => value + 1);
  useEffect(() => {
    if (!queued) return;
    const timer = window.setInterval(() => setRevision(value => value + 1), 3000);
    return () => window.clearInterval(timer);
  }, [queued]);

  async function refreshPage() {
    setBusy(true);
    setRefreshError('');
    setNotice('');
    try {
      const result = await post<{ queued: number }>(endpoint + '/refresh',
        { items: refreshable.map(({ rootId, seriesKey }) => ({ rootId, seriesKey })) }, onSignedOut);
      if (result) {
        setNotice(result.queued === 0 ? 'This page is already up to date or queued.' : `Queued ${result.queued} series for lookup.`);
        reload();
      }
    } catch (reason) {
      setRefreshError(reason instanceof Error ? reason.message : 'Could not queue this page.');
    } finally {
      setBusy(false);
    }
  }

  return <section aria-label="Metadata suggestions" className="grid min-w-0 gap-3">
    <div className="action-row">
      <Button variant="secondary" icon={RefreshCw} disabled={busy || refreshable.length === 0} onClick={() => void refreshPage()}>
        Rescan this page
      </Button>
      {notice && <span role="status" className="text-sm text-muted">{notice}</span>}
    </div>
    {refreshError && <p role="alert" className="m-0 text-danger">{refreshError}</p>}
    {error && <div className="action-row"><span role="alert" className="text-danger">Could not load metadata suggestions.</span>
      <Button variant="secondary" onClick={retry}>Retry</Button></div>}
    {!page && !error && <p role="status">Loading metadata suggestions…</p>}
    {page?.total === 0 && <p>No unresolved series match these filters.</p>}
    {page && page.total > 0 && <>
      <ul aria-label="Series awaiting metadata" className="m-0 grid list-none gap-2 p-0" role="list">
        {page.items.map(item => <SuggestionRow key={item.rootId + item.seriesKey} item={item} version={version}
          folder={roots?.find(folder => folder.id === item.rootId)} onSignedOut={onSignedOut} onSaved={reload} />)}
      </ul>
      <PageControls label="Metadata suggestion pages" offset={offset} count={page.items.length} total={page.total} onChange={setOffset} />
    </>}
  </section>;
}

function SuggestionRow({ item, version, folder, onSignedOut, onSaved }: {
  item: QueueRow; version: string; folder?: LibraryRoot; onSignedOut: () => void; onSaved: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [showCandidates, setShowCandidates] = useState(false);
  const [error, setError] = useState('');
  const exact = item.candidates?.filter(candidate => candidate.matchingTitle !== null) ?? [];
  const suggestion = exact.length === 1 ? exact[0] : undefined;

  async function confirm(proposal: Proposal) {
    setBusy(true);
    setError('');
    try {
      const saved = await post<Metadata>(`${endpoint}/${item.rootId}/${item.seriesKey}`,
        { revision: item.revision, providerId: proposal.match.providerId }, onSignedOut);
      if (saved) {
        setShowCandidates(false);
        onSaved();
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save this match.');
    } finally {
      setBusy(false);
    }
  }

  const display = item.conflictingMetadata ? 'Existing metadata assignments conflict. Review them manually.' :
    item.message ?? (item.queued ? 'AniList lookup queued.' : item.candidates?.length === 0 ? 'No AniList matches found.' :
      item.candidates ? suggestion ? 'One exact title match' : 'Choose a candidate or search manually.' : 'No AniList lookup yet.');

  return <li className="surface grid min-w-0 gap-3 p-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1.35fr)] sm:items-start sm:p-4">
    <div className="flex min-w-0 items-center gap-3">
      <SeriesCover rootId={item.rootId} seriesKey={item.seriesKey} available={item.hasCover} revision={version}
        className="h-16 w-11 shrink-0 rounded-md" />
      <div className="min-w-0">
        <strong className="block">{item.title}</strong>
        {folder && <p className="m-0 text-sm text-muted">{folder.name || folder.path}</p>}
        <p className="m-0 text-sm text-muted">{display}</p>
      </div>
    </div>
    <div className="grid min-w-0 gap-2">
      {error && <p role="alert" className="m-0 text-danger">{error}</p>}
      {!item.conflictingMetadata && suggestion && <ProposalSummary proposal={suggestion} />}
      <div className="action-row">
        {!item.conflictingMetadata && suggestion && <Button variant="primary" icon={Check} disabled={busy}
          onClick={() => void confirm(suggestion)}>Confirm match</Button>}
        {!item.conflictingMetadata && !!item.candidates?.length && (item.candidates.length > 1 || !suggestion) &&
          <Button variant="secondary" disabled={busy} onClick={() => setShowCandidates(true)}>Review matches ({item.candidates.length})</Button>}
        <MetadataDialogAction rootId={item.rootId} seriesKey={item.seriesKey} title={item.title} metadata={null}
          onSignedOut={onSignedOut} onChange={onSaved} actionLabel="Assign manually" />
      </div>
    </div>
    {showCandidates && <Modal title={`AniList matches for ${item.title}`} wide onClose={() => { if (!busy) setShowCandidates(false); }}>
      {error && <p role="alert" className="text-danger">{error}</p>}
      <ul className="m-0 list-none divide-y divide-line p-0" role="list">
        {item.candidates?.map(proposal => <li key={proposal.match.providerId} className="flex flex-wrap items-center justify-between gap-2 py-3">
          <ProposalSummary proposal={proposal} />
          <Button variant="secondary" icon={Check} disabled={busy} onClick={() => void confirm(proposal)}>Choose match</Button>
        </li>)}
      </ul>
    </Modal>}
  </li>;
}

function ProposalSummary({ proposal: { match, matchingTitle } }: { proposal: Proposal }) {
  return <div className="flex min-w-0 items-center gap-3">
    <CoverImage src={match.providerDetails?.coverUrl ?? undefined} className="h-16 w-11 shrink-0 rounded-md" />
    <div className="min-w-0">
      <strong className="block">{match.title}</strong>
      {matchingTitle && matchingTitle !== match.title && <p className="m-0 text-sm text-muted">Matched title {matchingTitle}</p>}
      {match.providerUrl && <ActionLink variant="ghost" icon={ExternalLink} href={match.providerUrl} target="_blank" rel="noreferrer">Open AniList</ActionLink>}
    </div>
  </div>;
}

async function post<T>(url: string, body: object, onSignedOut: () => void) {
  const response = await fetch(url, { method: 'POST', headers: {
    'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken(),
  }, body: JSON.stringify(body) });
  if (response.status === 401) {
    onSignedOut();
    return null;
  }
  if (!response.ok) {
    const failure = await response.json().catch(() => null) as { message?: string } | null;
    throw new Error(failure?.message ?? 'Could not save the metadata queue change.');
  }
  return await response.json() as T;
}
