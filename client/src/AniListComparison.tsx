import { useState } from 'react';
import { PageControls, useInventoryPage } from './CatalogInventory';
import { formatSize, isReadableArchive, ROOTS_URL } from './library';
import { Select } from './Select';
import { FileCover, FileCoverDialog, ProviderCoverPreview } from './SeriesCover';
import { MetadataDescription, MetadataStatus, useSeriesDetails } from './SeriesInfo';
import type { Metadata } from './SeriesInfo';
import { Button, Modal } from './ui';
import { useArchiveEvidence } from './useArchiveEvidence';

export type ComparisonFile = {
  id: string;
  relativePath: string;
  extension: string;
  size: number;
  modifiedAt: string;
  hasCover: boolean;
  volume: string | null;
  chapter: string | null;
  filenameEvidence?: { title: string; alternateTitle: string | null; creator: string | null } | null;
};

export function AniListComparison({ rootId, seriesKey, title, published, candidate, localFile, onSignedOut, onClose }: {
  rootId: string; seriesKey: string; title: string; published?: boolean; candidate: Metadata;
  localFile?: ComparisonFile; onSignedOut: () => void; onClose: () => void;
}) {
  const [selectedId, setSelectedId] = useState('');
  const [enlargedFile, setEnlargedFile] = useState<ComparisonFile | null>(null);
  const inventory = useInventoryPage<ComparisonFile>(localFile ? '' : `${ROOTS_URL}/${rootId}/files`, '', onSignedOut,
    { seriesKey, view: published ? 'library' : 'all' });
  const { details, error } = useSeriesDetails({ rootId, seriesKey, version: '', onSignedOut, published });
  const releases = inventory.page?.items ?? [];
  const file = localFile ?? releases.find(item => item.id === selectedId) ?? releases.find(item => item.hasCover) ?? releases[0];
  const archiveFile = file && isReadableArchive(file.extension) ? file.id : undefined;
  const archive = useArchiveEvidence(rootId, archiveFile, onSignedOut, file?.modifiedAt);
  const artwork = 'h-36 w-24 max-w-full rounded-lg sm:h-48 sm:w-32 [&_img]:object-contain';
  const detectedTitle = file?.filenameEvidence?.title;
  const localTitle = details?.metadata?.title ?? detectedTitle ?? title;
  const filenameFacts = [detectedTitle !== localTitle && detectedTitle, file?.filenameEvidence?.alternateTitle,
    file?.filenameEvidence?.creator].filter(Boolean);

  return <>
    <Modal title="Compare with AniList" wide onClose={onClose} headerClassName="border-b border-line pb-3">
      <div className="grid min-w-0 gap-4">
        {releases.length > 1 && file && <Select label="Local release" value={file.id} onChange={setSelectedId}
          options={releases.map(item => ({ value: item.id, label: item.relativePath }))} />}
        {inventory.page && <PageControls label="Local release pages" offset={inventory.offset} count={releases.length}
          total={inventory.page.total} onChange={inventory.setOffset} />}
        {!localFile && !inventory.page && !inventory.error && <p role="status" className="m-0 text-sm text-muted">Loading local releases…</p>}
        {inventory.error && <div className="action-row">
          <span role="alert" className="text-sm text-danger">Could not load local releases.</span>
          <Button variant="secondary" size="compact" onClick={inventory.retry}>Retry</Button>
        </div>}
        <div className="grid grid-cols-2 gap-3 sm:gap-5">
          <section aria-label="Local file cover" className="grid min-w-0 content-start justify-items-center gap-2 rounded-lg border border-line bg-inset p-3 text-center">
            <h3 className="m-0 text-sm font-semibold">Local file</h3>
            {file?.hasCover ? <button type="button" className="block cursor-pointer rounded-lg border-0 bg-transparent p-0"
              aria-label="Enlarge local file cover" onClick={() => setEnlargedFile(file)}>
              <FileCover rootId={rootId} fileId={file.id} available seriesCoverKey={null} revision={file.modifiedAt} className={artwork} />
            </button> : <p className="m-0 text-sm text-muted">No local file cover</p>}
            <strong className="text-sm wrap-anywhere">{localTitle}</strong>
          </section>
          <section aria-label="AniList candidate cover" className="grid min-w-0 content-start justify-items-center gap-2 rounded-lg border border-line bg-inset p-3 text-center">
            <h3 className="m-0 text-sm font-semibold">AniList</h3>
            {candidate.providerDetails?.coverUrl ? <ProviderCoverPreview src={candidate.providerDetails.coverUrl}
              largeSrc={candidate.providerDetails.coverLargeUrl ?? undefined} title={candidate.title}
              className={artwork} /> : <p className="m-0 text-sm text-muted">No AniList cover</p>}
            <strong className="text-sm wrap-anywhere">{candidate.title}</strong>
          </section>
        </div>
        <div className="grid gap-4 sm:grid-cols-2 sm:gap-5">
          <section aria-label="Local release evidence" className="grid min-w-0 content-start gap-3 wrap-anywhere">
            <h3 className="m-0 font-semibold">Local release evidence</h3>
            {file && <div className="grid gap-1 text-sm">
              <code>{file.relativePath}</code>
              <p className="m-0 text-muted">{[
                file.extension.toUpperCase(), formatSize(file.size), file.volume && `Vol. ${file.volume}`, file.chapter && `Ch. ${file.chapter}`,
              ].filter(Boolean).join(' · ')}</p>
              {filenameFacts.length > 0 && <p className="m-0 text-muted">Detected from filename · {filenameFacts.join(' · ')}</p>}
            </div>}
            {file && <section aria-label="ComicInfo" className="grid gap-1.5 border-t border-line pt-3 text-sm">
              <h4 className="m-0 font-semibold">ComicInfo</h4>
              {archive?.comicInfo.length ? <dl className="m-0 grid gap-1">
                {archive.comicInfo.map(({ label, value }) => <div key={label} className="flex flex-wrap gap-x-2">
                  <dt className="font-semibold">{label}</dt><dd className="m-0 text-muted">{value}</dd>
                </div>)}
              </dl> : <p className="m-0 text-muted" role="status">
                {archiveFile && archive === undefined ? 'Loading archive details…' :
                  archive ? 'No ComicInfo fields found.' : 'ComicInfo unavailable.'}
              </p>}
            </section>}
            {error && <p role="alert" className="m-0 text-sm text-danger">{error}</p>}
            {details?.metadata && <section aria-label="Saved series metadata" className="grid gap-2 border-t border-line pt-3">
              <h4 className="m-0 font-semibold">Saved series metadata</h4>
              <PublicationCounts metadata={details.metadata} />
              <MetadataDescription metadata={details.metadata} layout="preview" />
            </section>}
          </section>
          <section aria-label="AniList metadata" className="grid min-w-0 content-start gap-3">
            <h3 className="m-0 font-semibold">AniList metadata</h3>
            <PublicationCounts metadata={candidate} />
            <MetadataDescription metadata={candidate} layout="preview" />
          </section>
        </div>
      </div>
    </Modal>
    {enlargedFile && <FileCoverDialog title={enlargedFile.relativePath.split('/').at(-1) ?? localTitle}
      rootId={rootId} fileId={enlargedFile.id} available seriesCoverKey={null} revision={enlargedFile.modifiedAt}
      onClose={() => setEnlargedFile(null)} />}
  </>;
}

function PublicationCounts({ metadata }: { metadata: Metadata }) {
  const counts = [
    metadata.totalChapters && `${metadata.totalChapters.toLocaleString()} chapters`,
    metadata.totalVolumes && `${metadata.totalVolumes.toLocaleString()} volumes`,
  ].filter(Boolean);
  return <div className="grid gap-1">
    <MetadataStatus metadata={metadata} />
    {counts.length > 0 && <p className="m-0 text-sm text-muted">{counts.join(' · ')}</p>}
  </div>;
}
