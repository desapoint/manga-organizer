import { MultiValueField } from './MultiValueField';

export type SeriesTag = { name: string; spoiler: boolean };
export type TagDraft = { regular: string[]; spoilers: string[] };
const tagLimit = 100;

export function draftForTags(tags: SeriesTag[]): TagDraft {
  return {
    regular: tags.filter(tag => !tag.spoiler).map(tag => tag.name),
    spoilers: tags.filter(tag => tag.spoiler).map(tag => tag.name),
  };
}

export function tagsFromDraft({ regular, spoilers }: TagDraft): SeriesTag[] {
  return [...regular.map(name => ({ name, spoiler: false })), ...spoilers.map(name => ({ name, spoiler: true }))];
}

export function SeriesTagFields({ draft, onChange, disabled }: {
  draft: TagDraft;
  onChange: (draft: TagDraft) => void;
  disabled?: boolean;
}) {
  return <div className="grid gap-3">
    <MultiValueField label="Tags" itemLabel="tag" values={draft.regular} limit={tagLimit - draft.spoilers.length} maxLength={100}
      onChange={regular => onChange({ ...draft, regular })} disabled={disabled} />
    <MultiValueField label="Spoiler tags" itemLabel="spoiler tag" values={draft.spoilers} limit={tagLimit - draft.regular.length} maxLength={100}
      onChange={spoilers => onChange({ ...draft, spoilers })} disabled={disabled} />
  </div>;
}
