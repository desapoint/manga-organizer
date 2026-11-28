import { Link2, Search } from 'lucide-react';
import { useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import { PageControls, useInventoryPage } from './CatalogInventory';
import { Button, Modal, SearchField } from './ui';

const endpoint = '/api/file-associations';
type Match = { id: string; title: string; alternateTitles: string[]; kind: 'manga' | 'doujin'; providerId: number | null };
type Preview = {
  currentTitle: string | null; target: Match; count: number; revision: string;
  locations: { rootId: string; folder: string; relativePath: string }[];
};
type Props = {
  rootId: string; fileId: string; contentId: string; seriesKey: string; relativePath: string;
  confirmed: boolean; onSignedOut: () => void; onSaved: () => void;
};

export function FileAssociationEditor(props: Props) {
  const [open, setOpen] = useState(false);
  return <>
    <Button variant="ghost" icon={Link2} onClick={() => setOpen(true)}>
      {props.confirmed ? 'Change series' : 'Choose existing series'}
    </Button>
    {open && <AssociationDialog {...props} onClose={() => setOpen(false)}
      onSaved={() => { setOpen(false); props.onSaved(); }} />}
  </>;
}

function AssociationDialog({ rootId, fileId, contentId, seriesKey, relativePath, confirmed, onSignedOut, onClose, onSaved }:
  Props & { onClose: () => void }) {
  const [search, setSearch] = useState('');
  const [preview, setPreview] = useState<Preview | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const url = `${endpoint}/${rootId}/files/${fileId}`;

  async function review(metadataId: string) {
    setBusy(true);
    setError('');
    try {
      const response = await write(url + '/preview', 'POST', { contentId, seriesKey, metadataId });
      if (response.status === 401) return onSignedOut();
      await ensureSuccess(response);
      setPreview(await response.json() as Preview);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not preview this association.');
    } finally {
      setBusy(false);
    }
  }

  async function apply() {
    if (!preview) return;
    setBusy(true);
    setError('');
    try {
      const response = await write(url, 'PUT', { contentId, seriesKey, metadataId: preview.target.id, revision: preview.revision });
      if (response.status === 401) return onSignedOut();
      await ensureSuccess(response);
      onSaved();
    } catch (reason) {
      setPreview(null);
      setError(reason instanceof Error ? reason.message : 'Could not save this association.');
    } finally {
      setBusy(false);
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSearch(String(new FormData(event.currentTarget).get('search') ?? '').trim());
    setError('');
  }

  return <Modal wide title={confirmed ? 'Change file series' : 'Choose existing series'} onClose={() => { if (!busy) onClose(); }}>
    {error && <p role="alert" className="m-0 text-danger">{error}</p>}
    <p className="m-0 text-sm text-muted"><code className="wrap-anywhere">{relativePath}</code></p>
    {preview ? <section className="grid gap-3" aria-label="Association preview">
      <div>
        {preview.currentTitle && <p className="m-0 text-sm text-muted">Currently linked to {preview.currentTitle}</p>}
        <p className="m-0 font-semibold">Assign to {preview.target.title}</p>
        <p className="m-0 text-sm text-muted">{preview.count} indexed {preview.count === 1 ? 'location shares' : 'locations share'} these exact bytes.</p>
      </div>
      <ul aria-label="Affected file locations" className="m-0 grid list-none gap-2 rounded-lg border border-line p-3" role="list">
        {preview.locations.map(location => <li key={location.rootId + location.relativePath} className="wrap-anywhere">
          <code>{location.relativePath}</code>
          <span className="block text-sm text-muted">{location.folder}</span>
        </li>)}
      </ul>
      {preview.count > preview.locations.length && <p className="m-0 text-sm text-muted">
        {preview.count - preview.locations.length} additional indexed locations
      </p>}
      <p className="m-0 text-sm text-muted">Review chapter coverage separately if the previous grouping was incorrect.</p>
      <div className="action-row">
        <Button disabled={busy} onClick={() => void apply()}>Confirm file association</Button>
        <Button variant="secondary" disabled={busy} onClick={() => setPreview(null)}>Choose another series</Button>
      </div>
    </section> : <>
      <form className="action-row" onSubmit={submit}>
        <SearchField name="search" aria-label="Search saved series" className="min-w-0 flex-1" placeholder="Title or alternate title"
          maxLength={160} defaultValue={search} disabled={busy} />
        <Button variant="secondary" icon={Search} type="submit" disabled={busy}>Search</Button>
      </form>
      <AssociationMatches key={search} search={search} busy={busy} onSignedOut={onSignedOut} onChoose={review} />
    </>}
  </Modal>;
}

function AssociationMatches({ search, busy, onSignedOut, onChoose }: {
  search: string; busy: boolean; onSignedOut: () => void; onChoose: (metadataId: string) => void;
}) {
  const query = useInventoryPage<Match>(endpoint + '/series', '', onSignedOut, { search });
  return <>
    {!query.page && !query.error && <p role="status">Loading saved series…</p>}
    {query.error && <div className="action-row"><span role="alert" className="text-danger">Could not load saved series.</span>
      <Button variant="secondary" onClick={query.retry}>Retry</Button></div>}
    {query.page?.total === 0 && <p>No saved series match this search.</p>}
    {!!query.page?.items.length && <ul aria-label="Saved series" className="m-0 max-h-72 list-none divide-y divide-line overflow-auto p-0" role="list">
      {query.page.items.map(match => <li key={match.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
        <div className="min-w-0 wrap-anywhere">
          <strong className="block">{match.title}</strong>
          <span className="text-sm text-muted">{match.alternateTitles.length > 0 && `${match.alternateTitles.join(' · ')} · `}
            {match.kind === 'doujin' ? 'Doujin' : 'Manga'} · {match.providerId ? `AniList #${match.providerId}` : 'Manual metadata'}
          </span>
        </div>
        <Button variant="secondary" disabled={busy} aria-label={`Review association with ${match.title}`}
          onClick={() => onChoose(match.id)}>Review</Button>
      </li>)}
    </ul>}
    {query.page && <PageControls label="Saved series pages" offset={query.offset} count={query.page.items.length}
      total={query.page.total} onChange={query.setOffset} />}
  </>;
}

async function write(url: string, method: 'POST' | 'PUT', body: object) {
  return await fetch(url, { method, headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() }, body: JSON.stringify(body) });
}

async function ensureSuccess(response: Response) {
  if (response.ok) return;
  const failure = await response.json().catch(() => null) as { message?: string } | null;
  throw new Error(failure?.message ?? 'Could not update the file association.');
}
