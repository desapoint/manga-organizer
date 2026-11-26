import type { CSSProperties } from 'react';
import { isBundle } from './duplicateCoverage';
import type { CoverageFile } from './duplicateCoverage';

const marks = {
  band: 'min-h-0 flex-1 motion-safe:transition-opacity',
  swatch: 'mt-1 size-2.5 shrink-0 rounded-xs',
};

export function CoverageMark({ file, color, variant = 'swatch', dimmed = false }: {
  file: CoverageFile; color: string | undefined; variant?: keyof typeof marks; dimmed?: boolean;
}) {
  const striped = !isBundle(file) && file.chapters.length === 1;
  return <span aria-hidden="true" data-variant={variant} data-pattern={striped ? 'striped' : 'solid'}
    data-file={variant === 'band' ? file.fileId : undefined}
    style={{ '--coverage-color': color ?? 'var(--color-muted)' } as CSSProperties}
    className={`coverage-mark ${marks[variant]}${dimmed ? ' opacity-20' : ''}`} />;
}
