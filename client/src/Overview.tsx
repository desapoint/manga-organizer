import { ArrowUpRight, BookOpen, FolderCog, Library } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { LibraryRoot } from './library';
import { SeriesCover } from './SeriesCover';
import { ActionLink, ScanBadge, SectionHeader } from './ui';

type Resume = { contentId: string; title: string; rootId: string; seriesKey: string; hasCover: boolean;
  lastScannedAt: string | null; pageIndex: number; pageCount: number };

export function Overview({ roots, onSignedOut }: { roots: LibraryRoot[]; onSignedOut: () => void }) {
  const [resume, setResume] = useState<Resume | null>(null);
  const inventoryVersion = roots.map(root => `${root.id}:${root.lastScannedAt}`).join('|');

  useEffect(() => {
    const request = new AbortController();
    async function readResume() {
      const response = await fetch('/api/reader/resume', { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      const result = response.ok && response.status !== 204 ? await response.json() as Resume : null;
      if (!request.signal.aborted) setResume(result);
    }
    void readResume().catch(() => { if (!request.signal.aborted) setResume(null); });
    return () => request.abort();
  }, [inventoryVersion, onSignedOut]);
  const scanned = roots.filter(root => root.lastScannedAt !== null);
  const indexed = roots.reduce((total, root) => total + root.fileCount, 0);
  const review = roots.filter(root => ['unavailable', 'incomplete', 'emptyReview', 'interrupted'].includes(root.state));
  const hasLibrary = roots.some(root => root.kind !== 'import');
  const activity = [...roots].sort((first, second) => (second.lastScannedAt ?? '').localeCompare(first.lastScannedAt ?? ''));

  return (
    <>
      {resume && <section className="surface flex flex-wrap items-center gap-4 p-4 sm:p-5" aria-labelledby="resume-heading">
        <SeriesCover available={resume.hasCover} seriesKey={resume.seriesKey} rootId={resume.rootId}
          revision={resume.lastScannedAt ?? ''} className="h-24 w-16 shrink-0 rounded-md sm:h-32 sm:w-22" />
        <div className="grid flex-1 gap-2">
          <SectionHeader id="resume-heading" title="Continue reading" />
          <strong>{resume.title}</strong>
          <div className="grid max-w-72 gap-1 text-sm text-muted">
            <span>Page {resume.pageIndex + 1} of {resume.pageCount}</span>
            <progress aria-label="Reading position" className="h-2 w-full accent-accent" value={resume.pageIndex + 1} max={resume.pageCount} />
          </div>
          <ActionLink className="justify-self-start" icon={BookOpen} href={`#read/${resume.contentId}`}>Resume reading</ActionLink>
        </div>
      </section>}
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
