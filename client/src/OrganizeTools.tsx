import { FolderCog } from 'lucide-react';
import { useEffect, useState } from 'react';
import { BatchOrganizer, IntakeTransfer } from './BatchOrganizer';
import { FileInventory, useInventoryPage } from './CatalogInventory';
import { EditionCoverage } from './EditionCoverage';
import { FileOperationHistory } from './FileOperations';
import { canTransferTo, intakeTargetFor, organizeToolsHref, ROOTS_URL, seriesHref } from './library';
import type { LibraryRoot } from './library';
import { Select } from './Select';
import { ActionLink, SearchField, SectionHeader } from './ui';

export function OrganizeTools({ rootId, seriesKey, roots, version, onSignedOut, onChanged }: {
  rootId?: string; seriesKey?: string; roots: LibraryRoot[] | null; version: string; onSignedOut: () => void; onChanged: () => void;
}) {
  const source = rootId ? roots?.find(root => root.id === rootId) : roots?.find(root => root.managed) ?? roots?.[0];
  return <>
    {!roots && <p role="status">Loading folders…</p>}
    {roots && !source && <div className="action-row">
      <p className="m-0">{rootId ? 'This folder is no longer configured.' : 'Configure a folder to start organizing files.'}</p>
      <ActionLink href={roots.length ? organizeToolsHref() : '#settings'} variant="secondary" icon={FolderCog}>
        {roots.length ? 'Choose another folder' : 'Folder settings'}
      </ActionLink>
    </div>}
    {source && roots && <>
      <div className="surface flex flex-wrap items-end gap-3 p-4">
        <Select label="Source folder" options={roots.map(root => ({ value: root.id, label: root.name || root.path }))}
          value={source.id} onChange={id => { window.location.hash = organizeToolsHref(id); }} />
        {!source.managed && <p className="m-0 text-sm text-muted">Move and rename are disabled for this folder.</p>}
      </div>
      <OrganizeInventory key={source.id + (seriesKey ?? '')} source={source} seriesKey={seriesKey} folders={roots} version={version}
        onSignedOut={onSignedOut} onChanged={onChanged} />
    </>}
    <FileOperationHistory version={version} onSignedOut={onSignedOut} onChanged={onChanged} />
  </>;
}

function OrganizeInventory({ source, seriesKey, folders, version, onSignedOut, onChanged }: {
  source: LibraryRoot; seriesKey?: string; folders: LibraryRoot[]; version: string; onSignedOut: () => void; onChanged: () => void;
}) {
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [revision, setRevision] = useState(0);
  const [notice, setNotice] = useState('');
  const rootId = source.id;
  const managedFolders = source.managed ? folders.filter(folder => folder.managed && (source.kind !== 'import' || folder.id === source.id)) : [];
  const destinations = managedFolders.filter(folder => folder.kind !== 'import');
  const intakeTarget = source.kind === 'library' ? undefined : intakeTargetFor(source, folders);
  const seriesUrl = `${ROOTS_URL}/${rootId}/series`;
  const { page } = useInventoryPage<{ title: string }>(seriesKey ? seriesUrl : '', version, onSignedOut, seriesKey ? { seriesKey } : undefined);
  const currentVersion = `${version}:${revision}`;
  const folder = { rootId, version: currentVersion, onSignedOut };
  const management = {
    managedFolders,
    intakeTarget: canTransferTo(source, intakeTarget) ? intakeTarget : undefined,
    onDataChanged: () => setRevision(value => value + 1),
    onMoved: () => {
      setNotice('File changes recorded in File operation history.');
      onChanged();
    },
  };

  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(search.trim()), 180);
    return () => window.clearTimeout(timer);
  }, [search]);

  return <section aria-label="Organize files" className="grid min-w-0 gap-3">
    <div className="action-row">
      <SectionHeader title={page?.items[0]?.title ?? 'Indexed files'} />
      {seriesKey && <>
        <ActionLink variant="ghost" href={organizeToolsHref(rootId)}>Show all files in folder</ActionLink>
        <ActionLink variant="ghost" href={seriesHref(rootId, seriesKey)}>Open series in Library</ActionLink>
      </>}
    </div>
    <div className="flex flex-wrap items-center gap-2">
      <SearchField aria-label="Find files to organize" placeholder="Search filename or series" value={search} maxLength={160}
        className="min-w-0 flex-1" onChange={event => setSearch(event.target.value)} />
      {destinations.length > 0 && <BatchOrganizer source={source} targets={destinations} version={currentVersion}
        onSignedOut={onSignedOut} onChanged={management.onMoved} seriesKey={seriesKey} />}
    </div>
    {source.kind !== 'library' && <IntakeTransfer source={source} folders={folders} version={currentVersion}
      onSignedOut={onSignedOut} onChanged={management.onMoved} seriesKey={seriesKey} />}
    {notice && <p role="status" className="m-0 text-sm text-positive">{notice}</p>}
    {seriesKey && <EditionCoverage url={`${seriesUrl}/${seriesKey}`} version={currentVersion}
      onSignedOut={onSignedOut} onChange={management.onDataChanged} />}
    <FileInventory key={query} {...folder} search={query} seriesKey={seriesKey} management={management} />
  </section>;
}
