import { Image, ScanSearch } from 'lucide-react';
import { useState } from 'react';
import { getCsrfToken } from './api';
import { formatSize, scanIsActive } from './library';
import type { LibraryRoot } from './library';
import { MaintenanceResets } from './MaintenanceResets';
import { ScanStatus } from './ScanStatus';
import { Select } from './Select';
import { Button, SectionHeader } from './ui';

type ScanResult = { rootId: string; state: 'queued' | 'busy' | 'missing' };
const requestLabels = { queued: 'Scan requested', busy: 'Already scanning or in use', missing: 'Library unavailable' };

export function MaintenanceTools({ roots, onSignedOut, onChanged }: {
  roots: LibraryRoot[] | null; onSignedOut: () => void; onChanged: () => void;
}) {
  const [mode, setMode] = useState<'quick' | 'full'>('quick');
  const [selection, setSelection] = useState<string[]>([]);
  const [results, setResults] = useState<ScanResult[]>([]);
  const [pending, setPending] = useState<'scan' | 'artwork' | null>(null);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  const libraries = roots?.filter(root => root.kind !== 'import') ?? [];
  const selected = selection.filter(id => libraries.some(root => root.id === id));
  const scanReady = libraries.some(root => !scanIsActive(root) && root.state !== 'paused');
  const scanActive = roots?.some(scanIsActive);

  async function request<T>(suffix: string, body: object) {
    const response = await fetch('/api/maintenance/' + suffix, { method: 'POST', headers: {
      'Content-Type': 'application/json', 'X-CSRF-Token': await getCsrfToken(),
    }, body: JSON.stringify(body) });
    if (response.status === 401) {
      onSignedOut();
      return null;
    }
    if (!response.ok) {
      const outcome = await response.json().catch(() => null) as { message?: string } | null;
      throw new Error(outcome?.message ?? 'The maintenance action could not finish.');
    }
    return await response.json() as T;
  }

  async function scan(allLibraries: boolean) {
    setPending('scan');
    setError('');
    setNotice('');
    try {
      const outcome = await request<ScanResult[]>('scans', { allLibraries, rootIds: allLibraries ? [] : selected, full: mode === 'full' });
      if (outcome) {
        setResults(outcome);
        const count = outcome.filter(result => result.state === 'queued').length;
        setNotice(count ? `Requested ${count} ${count === 1 ? 'scan' : 'scans'}.` : 'No additional scans were queued.');
        onChanged();
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not queue scans.');
    } finally {
      setPending(null);
    }
  }

  async function clearArtwork() {
    setPending('artwork');
    setError('');
    setNotice('');
    try {
      const outcome = await request<{ images: number; imageBytes: number }>('artwork-cache/clear', {});
      if (outcome) {
        setNotice(`Cleared ${outcome.images.toLocaleString()} cached ${outcome.images === 1 ? 'image' : 'images'} (${formatSize(outcome.imageBytes)} of stored images).`);
        onChanged();
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not clear cached artwork.');
    } finally {
      setPending(null);
    }
  }

  return <>
    {error && <p role="alert" className="m-0 text-danger">{error}</p>}
    {notice && <p role="status" className="m-0 text-positive">{notice}</p>}
    <section className="surface grid gap-3 p-4" aria-labelledby="maintenance-scans">
      <SectionHeader id="maintenance-scans" title="Library scans" />
      {!roots && <p role="status" className="m-0">Loading library folders…</p>}
      {roots && libraries.length === 0 && <p className="m-0">No library folders configured.</p>}
      {libraries.length > 0 && <>
        <ul aria-label="Libraries to rescan" role="list" className="m-0 list-none divide-y divide-line p-0">
          {libraries.map(root => {
            const requestState = results.find(result => result.rootId === root.id)?.state;
            return <li key={root.id} className="grid min-w-0 gap-1 py-2">
              <label className="flex min-h-11 items-center gap-3">
                <input type="checkbox" checked={selected.includes(root.id)} onChange={event =>
                  setSelection(values => event.target.checked ? [...values, root.id] : values.filter(id => id !== root.id))} />
                <span className="min-w-0">
                  <strong className="block">{root.name || root.path}</strong>
                  {root.name && <code className="text-xs text-muted">{root.path}</code>}
                </span>
              </label>
              {requestState && <p className="m-0 text-sm text-muted">{requestLabels[requestState]}</p>}
              <ScanStatus root={root} />
            </li>;
          })}
        </ul>
        <div className="action-row items-end">
          <Select label="Scan mode" value={mode} options={[{ value: 'quick', label: 'Quick scan' },
            { value: 'full', label: 'Full verification' }]} onChange={setMode} />
          <Button icon={ScanSearch} disabled={!selected.length || pending !== null} onClick={() => void scan(false)}>Rescan selected</Button>
          <Button variant="secondary" disabled={!scanReady || pending !== null} onClick={() => void scan(true)}>Rescan all libraries</Button>
        </div>
      </>}
    </section>
    <section className="surface grid gap-3 p-4" aria-labelledby="maintenance-artwork">
      <SectionHeader id="maintenance-artwork" title="Artwork cache" />
      <p className="m-0 text-sm text-muted">Clear stored cover images. Scanning the source folders rebuilds the cache.</p>
      <Button icon={Image} variant="secondary" className="justify-self-start" disabled={!roots || pending !== null || scanActive}
        onClick={() => void clearArtwork()}>Clear artwork cache</Button>
    </section>
    <MaintenanceResets roots={roots} onSignedOut={onSignedOut} onChanged={onChanged} />
  </>;
}
