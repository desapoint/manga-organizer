import { ArrowRight, ListChecks } from 'lucide-react';
import { useState } from 'react';
import { PageControls, useInventoryPage } from './CatalogInventory';
import { postFileOperation } from './FileOperations';
import type { Operation } from './FileOperations';
import { canTransferTo, intakeTargetFor, ROOTS_URL } from './library';
import type { LibraryRoot } from './library';
import { Select } from './Select';
import { FileCover } from './SeriesCover';
import { ActionLink, Button, Modal, SearchField } from './ui';

type BatchFile = { id: string; contentId: string | null; relativePath: string; seriesTitle: string | null; hasSeriesMetadata: boolean;
  volume: string | null; chapter: string | null; hasCover: boolean; seriesCoverKey: string | null; associationConfirmed: boolean };
type Choice = { file: BatchFile; target: string };
type Preview = { index: number; plan: Operation | null; error: string | null };
type Result = { id: string; result: Operation | null; error: string | null };

function suggestedPath(file: BatchFile) {
  if (!file.seriesTitle) return file.relativePath;
  const folder = file.seriesTitle.replace(/[<>:"/\\|?*\u0000-\u001f]/g, ' ').replace(/\s+/g, ' ').slice(0, 90).trim().replace(/\.+$/, '');
  const name = file.relativePath.split('/').at(-1);
  return folder && name && folder !== '.' && folder !== '..' ? folder + '/' + name : file.relativePath;
}

type BatchContext = {
  source: LibraryRoot; version: string; onSignedOut: () => void; onChanged: () => void; seriesKey?: string;
};

export function IntakeTransfer({ folders, ...context }: BatchContext & { folders: LibraryRoot[] }) {
  const target = intakeTargetFor(context.source, folders);
  if (!target) return <div className="action-row">
    <span className="text-sm text-muted">Choose a target library for reviewed transfers.</span>
    <ActionLink variant="ghost" href="#settings">Folder settings</ActionLink>
  </div>;
  if (target.id === context.source.id)
    return <p className="m-0 text-sm text-muted">Target library · This folder. Confirmed files appear in Library in place.</p>;
  return <div className="action-row">
    <span className="text-sm text-muted">Target library · {target.name || target.path}</span>
    {canTransferTo(context.source, target) ? <BatchOrganizer {...context} targets={[target]} intakeTransfer /> :
      <span className="text-sm text-muted">Transfers require two available folders with managed access.</span>}
  </div>;
}

export function BatchOrganizer({ source, targets, version, onSignedOut, onChanged, seriesKey, intakeTransfer = false }:
  BatchContext & { targets: LibraryRoot[]; intakeTransfer?: boolean }) {
  const [opened, setOpened] = useState(false);
  const [dirty, setDirty] = useState(false);

  return <>
    <Button icon={ListChecks} variant="secondary" className="justify-self-start" onClick={() => { setOpened(true); setDirty(false); }}>
      {intakeTransfer ? 'Review intake transfer' : 'Organize files'}</Button>
    {opened && <BatchDialog source={source} targets={targets} version={version} onSignedOut={onSignedOut} seriesKey={seriesKey}
      intakeTransfer={intakeTransfer}
      onChanged={() => setDirty(true)} onClose={() => { setOpened(false); if (dirty) onChanged(); }} />}
  </>;
}

function BatchDialog({ source, targets, version, onSignedOut, onChanged, onClose, seriesKey, intakeTransfer }:
  BatchContext & { targets: LibraryRoot[]; onClose: () => void; intakeTransfer: boolean }) {
  const [search, setSearch] = useState('');
  const [targetRoot, setTargetRoot] = useState(targets[0]?.id ?? '');
  const [keepBoth, setKeepBoth] = useState(false);
  const [choices, setChoices] = useState<Choice[]>([]);
  const [plans, setPlans] = useState<Preview[] | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [results, setResults] = useState<Result[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const files = useInventoryPage<BatchFile>(ROOTS_URL + '/' + source.id + '/files', version, onSignedOut,
    seriesKey ? { search, seriesKey } : { search });

  async function preview() {
    setBusy(true);
    setError('');
    try {
      const plans = await postFileOperation<Preview[]>('/batch-preview', onSignedOut, { items: choices.map(item => ({
        sourceRoot: source.id, fileId: item.file.id, targetRoot, targetPath: item.target, keepBoth, intakeTransfer,
      })) });
      if (!plans) return;
      setPlans(plans);
      setSelected(plans.flatMap(item => item.plan ? [item.plan.id] : []));
      setResults(null);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not preview the batch.');
    } finally {
      setBusy(false);
    }
  }

  async function apply() {
    setBusy(true);
    setError('');
    try {
      const items = await postFileOperation<Result[]>('/batch-apply', onSignedOut, { ids: selected });
      if (!items) return;
      setResults(items);
      onChanged();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Check file operation history before retrying.');
      onChanged();
    } finally {
      setBusy(false);
    }
  }

  return <Modal title={intakeTransfer ? 'Review intake transfer' : 'Organize selected files'} wide onClose={() => { if (!busy) onClose(); }}>
    <div className="grid gap-4">
      {error && <p role="alert" className="m-0 text-danger">{error}</p>}
      {plans ? <>
        <p className="m-0 text-sm text-muted">Review each original and destination. Applied moves create a recovery copy and appear in File operation history.</p>
        <ul role="list" className="m-0 grid list-none gap-2 p-0">
          {plans.map(item => {
            const choice = choices[item.index];
            if (!choice) return null;
            const plan = item.plan;
            const result = results?.find(entry => entry.id === plan?.id);
            return <li key={choice.file.id} className="grid gap-2 rounded-lg border border-line bg-inset p-3">
              <div className="grid items-center gap-2 sm:grid-cols-[1fr_auto_1fr]">
                <code>{plan?.source ?? choice.file.relativePath}</code>
                <ArrowRight className="size-4 text-muted" aria-hidden="true" />
                <code>{plan?.target ?? choice.target}</code>
              </div>
              <p className="m-0 text-sm text-muted">{[choice.file.seriesTitle, choice.file.volume && 'Volume ' + choice.file.volume,
                choice.file.chapter && 'Chapter ' + choice.file.chapter].filter(Boolean).join(' · ') || 'Association needs review'}</p>
              {plan && !results && <label className="flex items-center gap-2"><input type="checkbox" disabled={busy} checked={selected.includes(plan.id)}
                onChange={event => setSelected(current => event.target.checked ? [...current, plan.id] : current.filter(id => id !== plan.id))} /> Apply this move</label>}
              {(item.error || result?.error) && <p role="alert" className="m-0 text-sm text-danger">{item.error || result?.error}</p>}
              {result?.result && <p role="status" className="m-0 text-sm text-positive">Moved · Recovery copy retained</p>}
              {results && plan && !result && <p className="m-0 text-sm text-muted">Skipped</p>}
            </li>;
          })}
        </ul>
        <div className="action-row">
          {!results && <Button disabled={!selected.length || busy} onClick={() => void apply()}>{busy ? 'Applying changes…' : `Apply ${selected.length} selected`}</Button>}
          <Button variant="secondary" disabled={busy} onClick={results ? onClose : () => setPlans(null)}>{results ? 'Done' : 'Edit destinations'}</Button>
        </div>
      </> : <>
        <div className="grid gap-3 sm:grid-cols-2 sm:items-end">
          <SearchField aria-label="Find batch files" placeholder="Find batch files" maxLength={160} value={search}
            onChange={event => { setSearch(event.target.value); files.setOffset(0); }} />
          {intakeTransfer ? <p className="m-0 text-sm">Target library · {targets[0]?.name || targets[0]?.path}</p> :
            <Select label="Destination library" value={targetRoot} onChange={setTargetRoot}
              options={targets.map(root => ({ value: root.id, label: root.name || root.path }))} />}
        </div>
        <label className="flex items-center gap-2"><input type="checkbox" checked={keepBoth} onChange={event => setKeepBoth(event.target.checked)} />
          Keep both on a destination conflict</label>
        {files.error && <div className="action-row"><p role="alert">Could not load files.</p><Button variant="secondary" onClick={files.retry}>Retry</Button></div>}
        {!files.page && !files.error && <p role="status">Loading files…</p>}
        <ul role="list" className="m-0 grid list-none divide-y divide-line p-0" aria-label="Batch files to organize">
          {files.page?.items.map(file => <li key={file.id} className="flex items-center gap-2 py-1">
            <FileCover rootId={source.id} fileId={file.id} available={file.hasCover} seriesCoverKey={file.seriesCoverKey}
              revision={version} className="h-12 w-8 shrink-0 rounded" />
            <label className="flex min-h-11 items-center gap-2 wrap-anywhere">
              <input type="checkbox" checked={choices.some(choice => choice.file.id === file.id)}
                disabled={!file.contentId || intakeTransfer && !file.associationConfirmed ||
                  choices.length >= 50 && !choices.some(choice => choice.file.id === file.id)}
                onChange={event => setChoices(current => event.target.checked ? [...current, { file, target: suggestedPath(file) }] : current.filter(choice => choice.file.id !== file.id))} />
              <span><code>{file.relativePath}</code>
                {intakeTransfer && !file.associationConfirmed && <small className="block text-muted">Confirm series and metadata first</small>}</span>
            </label>
          </li>)}
        </ul>
        {files.page && <PageControls label="Batch file pages" offset={files.offset} count={files.page.items.length}
          total={files.page.total} onChange={files.setOffset} />}
        {choices.length > 0 && <section aria-label="Selected destinations" className="grid gap-3">
          {choices.map((item, index) => <label className="grid gap-1" key={item.file.id}>
            <span>Destination for <code>{item.file.relativePath}</code></span>
            <input value={item.target} maxLength={4096}
              onChange={event => setChoices(current => current.map((choice, row) => row === index ? { ...choice, target: event.target.value } : choice))} />
            <span className="text-sm text-muted">{item.file.seriesTitle ? `${item.file.hasSeriesMetadata ? 'Matched series' : 'Suggested series'} · ${item.file.seriesTitle}` : 'Association needs review'}</span>
          </label>)}
        </section>}
        <Button className="justify-self-start" disabled={busy || !targetRoot || !choices.length || choices.some(item => !item.target.trim())}
          onClick={() => void preview()}>{busy ? 'Checking paths…' : `Preview ${choices.length} selected`}</Button>
      </>}
    </div>
  </Modal>;
}
