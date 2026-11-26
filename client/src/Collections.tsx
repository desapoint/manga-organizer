import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { CoverGrid, PageControls, useInventoryPage } from './CatalogInventory';
import type { Series } from './CatalogInventory';
import { COLLECTIONS_URL, CollectionForm, collectionRequest, useTagNames } from './CollectionControls';
import type { Collection } from './CollectionControls';
import { Select } from './Select';
import { Button, Modal } from './ui';

function CollectionSeries({ collection, version, onSignedOut }: {
  collection: Collection; version: string; onSignedOut: () => void;
}) {
  const { page, offset, setOffset, error, retry } = useInventoryPage<Series>(COLLECTIONS_URL + '/' + collection.id + '/series', version, onSignedOut);
  if (error) return <div className="action-row"><p role="alert">Could not load collection series.</p><Button variant="secondary" onClick={retry}>Retry</Button></div>;
  if (!page) return <p role="status">Loading collection…</p>;
  if (!page.total) return <p className="m-0 text-sm text-muted">{collection.filters
    ? 'No library series match these filters.' : 'Add series from Manage collections on a series page.'}</p>;
  return <>
    <CoverGrid series={page.items} coverVersion={version} />
    <PageControls label="Collection pages" offset={offset} count={page.items.length} total={page.total} onChange={setOffset} />
  </>;
}

export function Collections({ version, onSignedOut }: { version: string; onSignedOut: () => void }) {
  const [collections, setCollections] = useState<Collection[] | null>(null);
  const [selectedId, setSelectedId] = useState('');
  const [editor, setEditor] = useState<'new' | 'edit' | null>(null);
  const [confirm, setConfirm] = useState<Collection | null>(null);
  const [revision, setRevision] = useState(0);
  const [loadingError, setLoadingError] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const selected = collections?.find(item => item.id === selectedId) ?? collections?.[0];
  const tags = useTagNames(editor ? COLLECTIONS_URL + '/tags' : '', version, onSignedOut);

  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      const response = await fetch(COLLECTIONS_URL, { signal: controller.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const items = await response.json() as Collection[];
      if (!controller.signal.aborted) {
        setCollections(items);
        setLoadingError('');
      }
    })().catch(() => { if (!controller.signal.aborted) setLoadingError('Could not load collections.'); });
    return () => controller.abort();
  }, [revision, onSignedOut]);

  function saved(item: Collection) {
    setSelectedId(item.id);
    setEditor(null);
    setRevision(value => value + 1);
  }

  async function remove(item: Collection) {
    setBusy(true);
    setError('');
    try {
      await collectionRequest(COLLECTIONS_URL + '/' + item.id, 'DELETE', onSignedOut);
      setConfirm(null);
      setSelectedId('');
      setRevision(value => value + 1);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not remove the collection.');
    } finally {
      setBusy(false);
    }
  }

  return <div className="grid min-w-0 gap-4">
    <div className="action-row">
      <Button icon={Plus} onClick={() => setEditor('new')}>New collection</Button>
      {selected && <>
        <Button variant="secondary" icon={Pencil} onClick={() => setEditor('edit')}>Edit collection</Button>
        <Button variant="ghost" icon={Trash2} onClick={() => { setError(''); setConfirm(selected); }}>Remove collection</Button>
      </>}
    </div>
    {loadingError && <div className="action-row"><p role="alert">{loadingError}</p>
      <Button variant="secondary" onClick={() => setRevision(value => value + 1)}>Retry</Button></div>}
    {!collections && !loadingError && <p role="status">Loading collections…</p>}
    {collections?.length === 0 && !editor && <p className="m-0 text-sm text-muted">Create a manual collection or one based on filters.</p>}
    {editor && <Modal title={editor === 'new' ? 'Create collection' : 'Edit collection'} onClose={() => setEditor(null)}>
      {tags.error && <p role="alert" className="m-0 text-danger">Could not load saved tags.</p>}
      <CollectionForm key={editor + (editor === 'edit' ? selected?.id : '')} initial={editor === 'edit' ? selected : undefined}
        tags={tags.tags} onCancel={() => setEditor(null)} onSaved={saved} onSignedOut={onSignedOut} />
    </Modal>}
    {selected && <section className="surface grid gap-4 p-4" aria-label="Collection results">
      <Select label="Collection" value={selected.id} options={(collections ?? []).map(item => ({
        value: item.id, label: item.name, description: item.filters ? 'Dynamic filters' : 'Manual',
      }))} onChange={setSelectedId} className="max-w-md" />
      <CollectionSeries key={selected.id} collection={selected} version={version + ':' + revision} onSignedOut={onSignedOut} />
    </section>}
    {confirm && <Modal title="Remove collection" onClose={() => { if (!busy) setConfirm(null); }}>
      <p className="wrap-anywhere">Remove {confirm.name} from Collections? Its series and files remain in your library.</p>
      {error && <p role="alert" className="text-danger">{error}</p>}
      <div className="action-row">
        <Button variant="secondary" disabled={busy} onClick={() => void remove(confirm)}>Remove collection</Button>
        <Button variant="ghost" disabled={busy} onClick={() => setConfirm(null)}>Cancel</Button>
      </div>
    </Modal>}
  </div>;
}
