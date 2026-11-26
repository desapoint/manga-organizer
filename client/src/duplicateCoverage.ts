export type Chapter = { volume: string | null; chapter: string; source?: string };
export type CoverageFile = {
  rootId: string; fileId: string; relativePath: string; fullPath: string; format: string; size: number;
  sha256: string | null; volume: string | null; volumeArchive: boolean; hasCover: boolean; seriesCoverKey: string | null; chapters: Chapter[];
};
export type CoverageState = 'matching' | 'multiple' | 'single' | 'missing';
export type CoverageChapter = Chapter & { key: string; files: CoverageFile[]; state: CoverageState };

export const coverageLabels: Record<CoverageState, string> = {
  matching: 'Same indexed bytes', multiple: 'Different releases', single: 'One indexed file', missing: 'No indexed file',
};

const chapterNumber = new Intl.Collator(undefined, { numeric: true });
export const chapterKey = (chapter: Chapter) => `${chapter.volume ?? ''}:${chapter.chapter}`;
export const fileName = (file: CoverageFile) => file.relativePath.split('/').at(-1) ?? file.relativePath;
export const isBundle = (file: CoverageFile) => file.volumeArchive || file.chapters.length > 1;
export const fileKind = (file: CoverageFile) => isBundle(file) ? 'Volume or bundle' : file.chapters.length ? 'Single chapter' : 'Coverage unknown';

export function byteGroups(files: CoverageFile[]) {
  const byHash = new Map<string, CoverageFile[]>();
  for (const file of files) {
    if (!file.sha256) continue;
    const key = `${file.sha256}:${file.size}`;
    const matches = byHash.get(key);
    if (matches) matches.push(file);
    else byHash.set(key, [file]);
  }
  return [...byHash.values()].filter((matches): matches is [CoverageFile, CoverageFile, ...CoverageFile[]] => matches.length > 1);
}

export function chapterCoverage(files: CoverageFile[], expected: Chapter[]): CoverageChapter[] {
  const map = new Map<string, { chapter: Chapter; files: CoverageFile[] }>();
  for (const chapter of expected) map.set(chapterKey(chapter), { chapter, files: [] });
  for (const file of files) {
    for (const chapter of file.chapters) {
      const key = chapterKey(chapter);
      const entry = map.get(key);
      if (entry) {
        if (entry.files.at(-1)?.fileId !== file.fileId) entry.files.push(file);
      } else map.set(key, { chapter, files: [file] });
    }
  }
  return [...map].map(([key, entry]): CoverageChapter => ({ ...entry.chapter, key, files: entry.files,
    state: byteGroups(entry.files).length ? 'matching' : entry.files.length > 1 ? 'multiple' : entry.files.length ? 'single' : 'missing',
  })).sort((a, b) => Number(a.chapter) - Number(b.chapter) || chapterNumber.compare(a.chapter, b.chapter) ||
    Number(a.volume) - Number(b.volume) || chapterNumber.compare(a.volume ?? '', b.volume ?? ''));
}

export function fileColors(files: CoverageFile[]) {
  const colors = new Map<string, string>();
  let index = 0;
  for (const file of files) {
    if (!isBundle(file)) { colors.set(file.fileId, 'var(--color-mint)'); continue; }
    const hue = (295 + index++ * 137.508) % 300;
    colors.set(file.fileId, `oklch(78% 0.14 ${hue >= 135 ? hue + 60 : hue})`);
  }
  return colors;
}

export function matchingCoverage(a: CoverageFile, b: CoverageFile) {
  if (a.rootId !== b.rootId || a.format !== b.format || a.volume !== b.volume || a.volumeArchive !== b.volumeArchive ||
    a.chapters.length !== b.chapters.length) return false;
  const chapters = new Set(a.chapters.map(chapterKey));
  return b.chapters.every(chapter => chapters.has(chapterKey(chapter)));
}
