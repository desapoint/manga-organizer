import { ArrowUpRight, FolderCog, Library } from 'lucide-react';
import type { LibraryRoot } from './library';
import { ActionLink, ScanBadge, SectionHeader } from './ui';

export function Overview({ roots }: { roots: LibraryRoot[] }) {
  const scanned = roots.filter(root => root.lastScannedAt !== null);
  const indexed = roots.reduce((total, root) => total + root.fileCount, 0);
  const review = roots.filter(root => ['unavailable', 'incomplete', 'emptyReview', 'interrupted'].includes(root.state));
  const hasLibrary = roots.some(root => root.kind !== 'import');
  const activity = [...roots].sort((first, second) => (second.lastScannedAt ?? '').localeCompare(first.lastScannedAt ?? ''));

  return (
    <>
      <section className="surface grid gap-5 rounded-2xl p-[clamp(1rem,3vw,2rem)]" aria-labelledby="collection-heading">
        <SectionHeader id="collection-heading" title="Collection at a glance" />
        <dl className="m-0 grid grid-cols-3 gap-3 border-y border-line py-4">
          {[
            { label: 'Folders', value: roots.length },
            { label: 'Indexed files', value: indexed },
            { label: 'Need review', value: review.length },
          ].map(item => (
            <div key={item.label} className="flex min-w-0 flex-col-reverse gap-1">
              <dt className="text-sm text-muted">{item.label}</dt>
              <dd className="m-0 text-2xl font-bold tracking-tight sm:text-4xl">{item.value.toLocaleString()}</dd>
            </div>
          ))}
        </dl>
        <div className="action-row">
          {hasLibrary && <ActionLink icon={Library} href="#library">Open library</ActionLink>}
          <ActionLink variant={roots.length ? 'secondary' : 'primary'} icon={FolderCog} href="#settings">
            {roots.length ? 'Manage folders' : 'Add a library folder'}
          </ActionLink>
        </div>
      </section>
      <section className="grid gap-3" aria-labelledby="scan-activity-heading">
        <SectionHeader id="scan-activity-heading" title="Folder activity" />
        {roots.length === 0 ? <p className="m-0 text-muted">Folder status appears after you add a folder.</p> : (
          <ul className="surface m-0 list-none divide-y divide-line p-0" role="list">
            {activity.map(root => (
              <li key={root.id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
                <div className="grid gap-0.5">
                  <strong>{root.name || root.path}</strong>
                  {root.lastScannedAt && <time className="text-sm text-muted" dateTime={root.lastScannedAt}>{new Date(root.lastScannedAt).toLocaleString()}</time>}
                </div>
                <ScanBadge state={root.state} />
              </li>
            ))}
          </ul>
        )}
        {scanned.length === 0 && roots.length > 0 && <a href={hasLibrary ? '#library' : '#intake'} className="inline-flex w-fit items-center gap-1 text-sm text-accent-readable">
          Start an inventory scan <ArrowUpRight className="size-4" aria-hidden="true" />
        </a>}
      </section>
    </>
  );
}
