import { useEffect, useRef, useState } from 'react';
import { getCsrfToken } from './api';
import { ROOTS_URL } from './library';
import { useToast } from './Toast';

type CoverageNumbers = { volume: string | null; chapters: string[] };
export type FileCoverageState = CoverageNumbers & {
  multipleVolumes: boolean; revision: string; suggestion?: CoverageNumbers;
};
type CoverageFile = {
  id: string; contentId: string | null; seriesKey: string; relativePath: string; coverage?: FileCoverageState;
};
type Labels = { volume: string; chapters: string };
type Draft = Labels & {
  fileId: string; contentId: string; seriesKey: string; revision: string; original: Labels; suggested?: boolean;
};

const batchLimit = 500;

function coverageLabels(numbers: CoverageNumbers): Labels {
  return { volume: numbers.volume ?? '', chapters: numbers.chapters.join(', ') };
}

export function useBulkFileCoverage(rootId: string, published: boolean, onSignedOut: () => void, onSaved: () => void) {
  const [drafts, setDrafts] = useState<Map<string, Draft> | null>(null);
  const [busy, setBusy] = useState(false);
  const [suggesting, setSuggesting] = useState(false);
  const suggestionRequest = useRef<AbortController | null>(null);
  const notify = useToast();

  useEffect(() => () => suggestionRequest.current?.abort(), []);

  function update(file: CoverageFile, field: keyof Labels, value: string) {
    const { coverage, contentId } = file;
    if (!coverage || !contentId) return;

    setDrafts(current => {
      if (!current) return current;
      const entries = new Map(current);
      const original = coverageLabels(coverage);
      const existing = current.get(contentId) ?? {
        fileId: file.id, contentId, seriesKey: file.seriesKey, revision: coverage.revision, original, ...original,
      };
      const next = { ...existing, [field]: value, suggested: false };
      if (next.volume === existing.original.volume && next.chapters === existing.original.chapters) entries.delete(contentId);
      else entries.set(contentId, next);
      return entries;
    });
  }

  async function suggest(query: URLSearchParams) {
    if (!drafts || busy || suggestionRequest.current) return;
    const available = batchLimit - drafts.size;
    if (available <= 0) {
      notify('error', 'Apply or cancel the current batch before adding more suggestions.');
      return;
    }
    const request = new AbortController();
    suggestionRequest.current = request;
    setSuggesting(true);
    const proposed = new Map<string, Draft>();
    const evidence = new Map<string, string>();
    const ambiguous = new Set<string>();
    const params = new URLSearchParams(query);
    params.set('suggestCoverage', 'true');
    const limit = 100;
    params.set('limit', String(limit));
    try {
      for (let offset = 0; ; offset += limit) {
        params.set('offset', String(offset));
        const response = await fetch(`${ROOTS_URL}/${rootId}/files?${params}`, { signal: request.signal });
        if (response.status === 401) return onSignedOut();
        if (!response.ok) throw new Error('Could not load coverage suggestions.');
        const { items, total } = await response.json() as { items: CoverageFile[]; total: number };
        for (const { id, contentId, seriesKey, coverage } of items) {
          if (!contentId || !coverage || drafts.has(contentId) || coverage.multipleVolumes) continue;
          const labels = coverageLabels(coverage.suggestion ?? coverage);
          const comparable = labels.volume || labels.chapters ? JSON.stringify(labels) : null;
          if (!comparable) continue;
          const previous = evidence.get(contentId);
          if (previous && previous !== comparable) ambiguous.add(contentId);
          evidence.set(contentId, comparable);
          if (coverage.suggestion && !proposed.has(contentId)) {
            proposed.set(contentId, { fileId: id, contentId, seriesKey, revision: coverage.revision,
              original: coverageLabels(coverage), ...labels, suggested: true });
          }
        }
        if (offset + limit >= total || items.length === 0) break;
      }
      if (request.signal.aborted) return;
      const candidates = [...proposed].filter(([id]) => !ambiguous.has(id));
      const added = candidates.slice(0, available);
      if (added.length === 0) {
        notify('success', 'No missing assignments could be suggested from these filenames.');
        return;
      }
      setDrafts(new Map([...drafts, ...added]));
      notify('success', added.length < candidates.length
        ? `Added ${added.length} suggestions to this batch. Run suggestions again after applying these changes.`
        : `Added ${added.length} ${added.length === 1 ? 'suggestion' : 'suggestions'}. Review the draft before applying changes.`);
    } catch (reason) {
      if (!request.signal.aborted) notify('error', reason instanceof Error ? reason.message : 'Could not load coverage suggestions.');
    } finally {
      if (suggestionRequest.current === request) {
        suggestionRequest.current = null;
        setSuggesting(false);
      }
    }
  }

  async function apply() {
    if (!drafts?.size || busy || suggesting) return;
    if (drafts.size > batchLimit) {
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

  function cancel() {
    suggestionRequest.current?.abort();
    suggestionRequest.current = null;
    setSuggesting(false);
    setDrafts(null);
  }

  return { drafts, busy, suggesting, update, apply, suggest, start: () => setDrafts(new Map()), cancel };
}

export function BulkCoverageRow({ file, editor }: { file: CoverageFile; editor: ReturnType<typeof useBulkFileCoverage> }) {
  const { coverage, contentId, relativePath } = file;
  const draft = contentId ? editor.drafts?.get(contentId) : undefined;
  const editable = coverage && !coverage.multipleVolumes ? coverage : null;
  const disabled = editor.busy || editor.suggesting;
  return <li className="grid min-w-0 items-end gap-2.5 border-b border-line px-4 py-3 last:border-b-0
    @[40rem]:grid-cols-[minmax(0,1fr)_7rem_minmax(10rem,1fr)]">
    <div className="grid min-w-0 gap-0.5 self-center">
      <code className="text-sm wrap-anywhere">{relativePath}</code>
      {draft && <span className="text-xs text-accent-readable">{draft.suggested ? 'Suggested from filename' : 'Edited'}</span>}
    </div>
    {editable ? <>
      <label className="grid min-w-0 gap-1 text-sm">Volume
        <input aria-label={`Volume for ${relativePath}`} maxLength={13} inputMode="decimal" value={draft?.volume ?? editable.volume ?? ''}
          disabled={disabled} onChange={event => editor.update(file, 'volume', event.target.value)} />
      </label>
      <label className="grid min-w-0 gap-1 text-sm">Chapters
        <input aria-label={`Chapters for ${relativePath}`} maxLength={7500} placeholder="1, 1.5, 2"
          value={draft?.chapters ?? editable.chapters.join(', ')} disabled={disabled}
          onChange={event => editor.update(file, 'chapters', event.target.value)} />
      </label>
    </> : <p className="m-0 text-sm text-muted @[40rem]:col-span-2">
      {coverage?.multipleVolumes ? 'This file has chapters assigned across multiple volumes.' : 'Content identity unavailable.'}
    </p>}
  </li>;
}
