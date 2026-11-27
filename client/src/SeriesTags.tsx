import { useId } from 'react';

export type SeriesTag = { name: string; spoiler: boolean };
export type TagDraft = { regular: string; spoilers: string };

export function draftForTags(tags: SeriesTag[]): TagDraft {
  return {
    regular: tags.filter(tag => !tag.spoiler).map(tag => tag.name).join('\n'),
    spoilers: tags.filter(tag => tag.spoiler).map(tag => tag.name).join('\n'),
  };
}

export function tagsFromDraft({ regular, spoilers }: TagDraft): SeriesTag[] {
  const toTags = (value: string, spoiler: boolean) => value.split(/\r?\n/).map(name => name.trim())
    .filter(Boolean).map(name => ({ name, spoiler }));
  return [...toTags(regular, false), ...toTags(spoilers, true)];
}

export function SeriesTagFields({ draft, onChange, disabled }: {
  draft: TagDraft;
  onChange: (draft: TagDraft) => void;
  disabled?: boolean;
}) {
  const hint = useId();
  const className = 'min-w-0 rounded-lg border border-control bg-field p-3 text-base text-ink';
  return <div className="grid gap-2">
    <div className="grid gap-3 sm:grid-cols-2">
      <div className="grid gap-1">
        <label htmlFor={hint + '-regular'}>Tags</label>
        <textarea id={hint + '-regular'} value={draft.regular} onChange={event => onChange({ ...draft, regular: event.target.value })}
          className={className} rows={3} maxLength={10100} aria-describedby={hint} disabled={disabled} />
      </div>
      <div className="grid gap-1">
        <label htmlFor={hint + '-spoilers'}>Spoiler tags</label>
        <textarea id={hint + '-spoilers'} value={draft.spoilers} onChange={event => onChange({ ...draft, spoilers: event.target.value })}
          className={className} rows={3} maxLength={10100} aria-describedby={hint} disabled={disabled} />
      </div>
    </div>
    <p className="m-0 text-sm text-muted" id={hint}>Enter one tag per line. Readers can reveal spoiler tags in the series details.</p>
  </div>;
}
