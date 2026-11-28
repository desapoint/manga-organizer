import { useState } from 'react';
import { getCsrfToken } from './api';
import { ROOTS_URL } from './library';
import { useToast } from './Toast';

export type FileCoverageState = { volume: string | null; chapters: string[]; multipleVolumes: boolean; revision: string };
type CoverageFile = {
  id: string; contentId: string | null; seriesKey: string; relativePath: string; coverage?: FileCoverageState;
};
type Labels = { volume: string; chapters: string };
type Draft = Labels & {
  fileId: string; contentId: string; seriesKey: string; revision: string; original: Labels;
};

export function useBulkFileCoverage(rootId: string, published: boolean, onSignedOut: () => void, onSaved: () => void) {
  const [drafts, setDrafts] = useState<Map<string, Draft> | null>(null);
  const [busy, setBusy] = useState(false);
  const notify = useToast();

  function update(file: CoverageFile, field: keyof Labels, value: string) {
    const { coverage, contentId } = file;
    if (!coverage || !contentId) return;

    setDrafts(current => {
      if (!current) return current;
      const entries = new Map(current);
      const original = { volume: coverage.volume ?? '', chapters: coverage.chapters.join(', ') };
      const existing = current.get(contentId) ?? {
        fileId: file.id, contentId, seriesKey: file.seriesKey, revision: coverage.revision, original, ...original,
      };
      const next = { ...existing, [field]: value };
      if (next.volume === existing.original.volume && next.chapters === existing.original.chapters) entries.delete(contentId);
      else entries.set(contentId, next);
      return entries;
    });
  }

  async function apply() {
    if (!drafts?.size || busy) return;
    if (drafts.size > 500) {
      notify('error', 'Apply changes to up to 500 releases at a time.');
      return;
    }
    setBusy(true);
    try {
      const changes = [...drafts.values()].map(({ fileId, contentId, seriesKey, revision, volume, chapters }) => ({
        fileId, contentId, seriesKey, revision, volume, chapters: chapters.split(',').map(value => value.trim()).filter(Boolean),
      }));
      const response = await fetch(`${ROOTS_URL}/${rootId}/file-coverage?published=${published}`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: JSON.stringify({ changes }),
      });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        const failure = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(failure?.message ?? 'Could not apply coverage changes.');
      }
      setDrafts(null);
      notify('success', `Updated coverage for ${changes.length} ${changes.length === 1 ? 'release' : 'releases'}.`);
      onSaved();
    } catch (reason) {
      notify('error', reason instanceof Error ? reason.message : 'Could not apply coverage changes.');
    } finally {
      setBusy(false);
    }
  }

  return { drafts, busy, update, apply, start: () => setDrafts(new Map()), cancel: () => setDrafts(null) };
}

export function BulkCoverageRow({ file, editor }: { file: CoverageFile; editor: ReturnType<typeof useBulkFileCoverage> }) {
  const { coverage, contentId, relativePath } = file;
  const draft = contentId ? editor.drafts?.get(contentId) : undefined;
  const editable = coverage && !coverage.multipleVolumes ? coverage : null;
  return <li className="grid min-w-0 items-end gap-2.5 border-b border-line px-4 py-3 last:border-b-0
    @[40rem]:grid-cols-[minmax(0,1fr)_7rem_minmax(10rem,1fr)]">
    <div className="grid min-w-0 gap-0.5 self-center">
      <code className="text-sm wrap-anywhere">{relativePath}</code>
      {draft && <span className="text-xs text-accent-readable">Edited</span>}
    </div>
    {editable ? <>
      <label className="grid min-w-0 gap-1 text-sm">Volume
        <input aria-label={`Volume for ${relativePath}`} maxLength={13} inputMode="decimal" value={draft?.volume ?? editable.volume ?? ''}
          disabled={editor.busy} onChange={event => editor.update(file, 'volume', event.target.value)} />
      </label>
      <label className="grid min-w-0 gap-1 text-sm">Chapters
        <input aria-label={`Chapters for ${relativePath}`} maxLength={7500} placeholder="1, 1.5, 2"
          value={draft?.chapters ?? editable.chapters.join(', ')} disabled={editor.busy}
          onChange={event => editor.update(file, 'chapters', event.target.value)} />
      </label>
    </> : <p className="m-0 text-sm text-muted @[40rem]:col-span-2">
      {coverage?.multipleVolumes ? 'This file has chapters assigned across multiple volumes.' : 'Content identity unavailable.'}
    </p>}
  </li>;
}
