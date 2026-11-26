import { Plus, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { getCsrfToken } from './api';
import { Select } from './Select';
import { Button, SearchField } from './ui';

export const COLLECTIONS_URL = '/api/library/collections';
export type TagRules = { includeTags: string[]; excludeTags: string[] };
export type CollectionFilters = TagRules & { search: string; coverOnly: boolean };
export type Collection = { id: string; name: string; filters: CollectionFilters | null; containsSeries: boolean };
export const emptyFilters: CollectionFilters = { search: '', coverOnly: false, includeTags: [], excludeTags: [] };

export async function collectionRequest(url: string, method: string, onSignedOut: () => void, data?: object) {
  const response = await fetch(url, { method, headers: {
    'X-CSRF-Token': await getCsrfToken(), 'Content-Type': 'application/json',
  }, body: data === undefined ? undefined : JSON.stringify(data) });
  if (response.status === 401) onSignedOut();
  if (!response.ok) {
    const error = await response.json().catch(() => null) as { message?: string } | null;
    throw new Error(error?.message ?? 'Could not update the collection.');
  }
  return response;
}

export function useTagNames(url: string, version: string, onSignedOut: () => void) {
  const [tags, setTags] = useState<string[]>([]);
  const [error, setError] = useState(false);
  useEffect(() => {
    if (!url) return;
    const controller = new AbortController();
    void (async () => {
      const response = await fetch(url, { signal: controller.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const tags = await response.json() as string[];
      if (!controller.signal.aborted) { setTags(tags); setError(false); }
    })().catch(() => { if (!controller.signal.aborted) setError(true); });
    return () => controller.abort();
  }, [url, version, onSignedOut]);
  return { tags, error };
}

export function TagRulesFields({ tags, rules, onChange }: {
  tags: string[]; rules: TagRules; onChange: (rules: TagRules) => void;
}) {
  function options(list: string[]) {
    return [{ value: 'pick', label: 'Choose a tag' },
      ...tags.filter(tag => !list.includes(tag)).map(tag => ({ value: 'tag:' + tag, label: tag }))];
  }

  function addTag(key: keyof TagRules, value: string) {
    if (!value.startsWith('tag:') || rules[key].length >= 20) return;
    const tag = value.slice(4);
    const other = key === 'includeTags' ? 'excludeTags' : 'includeTags';
    onChange({ ...rules, [key]: [...rules[key], tag], [other]: rules[other].filter(item => item !== tag) });
  }

  return <div className="grid gap-3">
    <div className="grid gap-3 sm:grid-cols-2">
      <Select label="Include tags" value="pick" options={options(rules.includeTags)}
        disabled={tags.length === 0 || rules.includeTags.length >= 20} onChange={value => addTag('includeTags', value)} />
      <Select label="Exclude tags" value="pick" options={options(rules.excludeTags)}
        disabled={tags.length === 0 || rules.excludeTags.length >= 20} onChange={value => addTag('excludeTags', value)} />
    </div>
    <div className="action-row">
      {(['includeTags', 'excludeTags'] as const).flatMap(key => rules[key].map(tag => {
        const action = key === 'includeTags' ? 'Include' : 'Exclude';
        return <Button key={key + tag} variant="secondary" icon={X} aria-label={'Remove ' + action.toLowerCase() + ' rule for ' + tag}
          onClick={() => onChange({ ...rules, [key]: rules[key].filter(value => value !== tag) })}>{action} {tag}</Button>;
      }))}
    </div>
    {!tags.length && <p className="m-0 text-sm text-muted">Tags appear here after you assign series metadata.</p>}
  </div>;
}

export function CollectionForm({ initial, preset, tags, onSaved, onCancel, onSignedOut }: {
  initial?: Collection; preset?: CollectionFilters; tags: string[]; onSaved: (collection: Collection) => void;
  onCancel: () => void; onSignedOut: () => void;
}) {
  const [name, setName] = useState(initial?.name ?? '');
  const [dynamic, setDynamic] = useState(Boolean(initial?.filters ?? preset));
  const [filters, setFilters] = useState(initial?.filters ?? preset ?? emptyFilters);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  async function save(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError('');
    try {
      const response = await collectionRequest(COLLECTIONS_URL + (initial ? '/' + initial.id : ''),
        initial ? 'PUT' : 'POST', onSignedOut, { name, filters: dynamic ? filters : null });
      onSaved(await response.json() as Collection);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save the collection.');
    } finally {
      setBusy(false);
    }
  }

  return <form onSubmit={save} className="grid gap-3">
    <label className="grid gap-1">Collection name<input value={name} onChange={event => setName(event.target.value)} required maxLength={100} /></label>
    {!initial && !preset && <Select label="Collection type" value={dynamic ? 'dynamic' : 'manual'}
      options={[{ value: 'manual', label: 'Manual' }, { value: 'dynamic', label: 'Dynamic filters' }]}
      onChange={value => setDynamic(value === 'dynamic')} />}
    {dynamic && <>
      <SearchField aria-label="Series title search" value={filters.search} maxLength={160}
        onChange={event => setFilters({ ...filters, search: event.target.value })} />
      <label className="flex min-h-11 items-center gap-2"><input type="checkbox" checked={filters.coverOnly}
        onChange={event => setFilters({ ...filters, coverOnly: event.target.checked })} />Series with cover art only</label>
      <TagRulesFields tags={tags} rules={filters} onChange={rules => setFilters({ ...filters, ...rules })} />
    </>}
    {error && <p role="alert" className="m-0 text-danger">{error}</p>}
    <div className="action-row">
      <Button type="submit" icon={Plus} disabled={busy || !name.trim()}>{initial ? 'Save collection' : 'Create collection'}</Button>
      <Button variant="ghost" onClick={onCancel}>Cancel</Button>
    </div>
  </form>;
}

export function SeriesCollectionMembership({ rootId, seriesKey, onSignedOut }: { rootId: string; seriesKey: string; onSignedOut: () => void }) {
  const [open, setOpen] = useState(false);
  const [collections, setCollections] = useState<Collection[] | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    const args = new URLSearchParams({ rootId, seriesKey });
    void (async () => {
      const response = await fetch(COLLECTIONS_URL + '?' + args, { signal: controller.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const collectionList = await response.json() as Collection[];
      if (!controller.signal.aborted) {
        setError('');
        setCollections(collectionList.filter(collection => collection.filters === null));
      }
    })().catch(() => { if (!controller.signal.aborted) setError('Could not load your collections.'); });
    return () => controller.abort();
  }, [open, onSignedOut, rootId, seriesKey]);

  async function change(collection: Collection, included: boolean) {
    setBusy(true);
    setError('');
    setCollections(items => items?.map(item => item.id === collection.id ? { ...item, containsSeries: included } : item) ?? null);
    try {
      await collectionRequest(COLLECTIONS_URL + '/' + collection.id + '/membership', 'PUT', onSignedOut, { rootId, seriesKey, included });
    } catch (reason) {
      setCollections(items => items?.map(item => item.id === collection.id ? collection : item) ?? null);
      setError(reason instanceof Error ? reason.message : 'Could not update the collection.');
    } finally {
      setBusy(false);
    }
  }

  return <details className="surface p-4" onToggle={event => setOpen(event.currentTarget.open)}>
    <summary className="min-h-11 cursor-pointer py-2 text-sm font-semibold">Manage collections</summary>
    {error && <p role="alert" className="m-0 text-danger">{error}</p>}
    {!collections && !error && <p role="status">Loading collections…</p>}
    {collections?.length === 0 && <p className="m-0 text-sm text-muted">Create a manual collection in <a href="#collections">Collections</a> to add this series.</p>}
    {collections?.map(collection => <label key={collection.id} className="flex min-h-11 items-center gap-2 wrap-anywhere">
      <input type="checkbox" disabled={busy} checked={collection.containsSeries}
        onChange={event => void change(collection, event.target.checked)} />{collection.name}
    </label>)}
  </details>;
}
