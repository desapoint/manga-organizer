import { ClipboardCheck } from 'lucide-react';
import { useEffect, useState } from 'react';
import { getCsrfToken } from './api';
import type { SeriesLocation } from './library';
import { Button, Modal } from './ui';

export const findingNames = {
  bytes: 'Same indexed bytes',
  chapter: 'Shared chapter numbers',
  volume: 'Same numbered volume',
} as const;
export const duplicateReviewStates = { pending: 'Needs review', kept: 'Kept', deferred: 'Deferred' } as const;
const decisionNames = { keep: 'Kept files', defer: 'Deferred review', reopen: 'Reopened review' } as const;

type Kind = keyof typeof findingNames;
type Decision = keyof typeof decisionNames;
type Finding = {
  kind: Kind; files: number; status: keyof typeof duplicateReviewStates; fingerprint: string; latest: number; evidenceChanged: boolean;
};
type DecisionSummary = {
  groups: Finding[];
  recent: { id: number; kind: Kind; decision: Decision; note: string | null; decidedAt: string }[];
};
type Props = SeriesLocation & { version: string; onSignedOut: () => void; onChanged: () => void };

export function DuplicateDecisions(props: Props) {
  const [open, setOpen] = useState(false);
  return <>
    <Button variant="secondary" icon={ClipboardCheck} onClick={() => setOpen(true)}>Review decisions</Button>
    {open && <DecisionDialog {...props} onClose={() => setOpen(false)} />}
  </>;
}

function DecisionDialog({ rootId, seriesKey, version, onSignedOut, onChanged, onClose }: Props & { onClose: () => void }) {
  const url = `/api/file-operations/duplicates/${rootId}/series/${seriesKey}/decisions`;
  const [summary, setSummary] = useState<DecisionSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [retry, setRetry] = useState(0);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  useEffect(() => {
    const request = new AbortController();
    setLoading(true);
    async function load() {
      const response = await fetch(url, { signal: request.signal });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) throw new Error();
      const current = await response.json() as DecisionSummary;
      if (!request.signal.aborted) setSummary(current);
    }
    void load().catch(() => {
      if (!request.signal.aborted) {
        setSummary(null);
        setError('Could not load duplicate review decisions.');
      }
    }).finally(() => { if (!request.signal.aborted) setLoading(false); });
    return () => request.abort();
  }, [url, version, retry, onSignedOut]);

  async function decide(finding: Finding, decision: Decision, note: string) {
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const response = await fetch(`${url}/${finding.kind}`, { method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken() },
        body: JSON.stringify({ fingerprint: finding.fingerprint, latest: finding.latest, decision, note }),
      });
      if (response.status === 401) return onSignedOut();
      if (!response.ok) {
        if (response.status === 409) setRetry(value => value + 1);
        const details = await response.json().catch(() => null) as { message?: string } | null;
        throw new Error(details?.message ?? 'Could not save the review decision.');
      }
      setLoading(true);
      setNotice(`${decisionNames[decision]} for ${findingNames[finding.kind].toLowerCase()}.`);
      onChanged();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save the review decision.');
    } finally {
      setBusy(false);
    }
  }

  return <Modal wide title="Series review decisions" onClose={() => { if (!busy) onClose(); }}>
    <p className="m-0 text-sm text-muted">Each decision applies to all current candidates of that type in this series. Use the file preview to move any files to quarantine.</p>
    {notice && <p role="status" className="m-0 text-sm text-positive">{notice}</p>}
    {error && <div className="action-row"><span role="alert" className="text-danger">{error}</span>
      {!summary && <Button variant="secondary" onClick={() => { setError(''); setRetry(value => value + 1); }}>Retry</Button>}
    </div>}
    {loading && !summary && <p role="status">Loading review decisions…</p>}
    {summary?.groups.length === 0 && <p>No indexed duplicate findings remain in this series.</p>}
    {summary?.groups.map(finding => <FindingRow key={finding.kind + finding.latest} finding={finding}
      busy={busy || loading} onDecide={decide} />)}
    {!!summary?.recent.length && <details className="surface p-3">
      <summary className="cursor-pointer font-semibold">Recent decisions</summary>
      <ul className="m-0 mt-2 list-none divide-y divide-line p-0" aria-label="Recent review decisions" role="list">
        {summary.recent.map(item => <li key={item.id} className="grid gap-0.5 py-2 text-sm wrap-anywhere">
          <strong>{decisionNames[item.decision]} · {findingNames[item.kind]}</strong>
          {item.note && <span>{item.note}</span>}
          <time className="text-muted" dateTime={item.decidedAt}>{new Date(item.decidedAt).toLocaleString()}</time>
        </li>)}
      </ul>
    </details>}
  </Modal>;
}

function FindingRow({ finding, busy, onDecide }: {
  finding: Finding; busy: boolean; onDecide: (finding: Finding, decision: Decision, note: string) => void;
}) {
  const [note, setNote] = useState('');
  return <section className="surface grid gap-2 p-3" aria-label={findingNames[finding.kind]}>
    <div>
      <h3 className="m-0 text-base font-semibold">{findingNames[finding.kind]}</h3>
      <p className="m-0 text-sm text-muted">{finding.files.toLocaleString()} {finding.files === 1 ? 'file' : 'files'}
        {' · '}{duplicateReviewStates[finding.status]}</p>
      {finding.evidenceChanged && <p className="m-0 text-sm text-warning">The indexed evidence changed since the last decision.</p>}
    </div>
    <label className="grid gap-1 text-sm">Optional review note
      <input value={note} onChange={event => setNote(event.target.value)} maxLength={400} disabled={busy} />
    </label>
    <div className="action-row">
      {finding.status === 'pending' ? <>
        <Button variant="secondary" disabled={busy} onClick={() => onDecide(finding, 'keep', note)}>Keep files</Button>
        <Button variant="ghost" disabled={busy} onClick={() => onDecide(finding, 'defer', note)}>Defer review</Button>
      </> : <Button variant="secondary" disabled={busy} onClick={() => onDecide(finding, 'reopen', note)}>Reopen review</Button>}
    </div>
  </section>;
}
