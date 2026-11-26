import type { CSSProperties } from 'react';

const marks = {
  band: 'min-h-0 flex-1 motion-safe:transition-opacity',
  swatch: 'mt-1 size-2.5 shrink-0 rounded-xs',
};

export function CoverageMark({ pattern, color, fileId, variant = 'swatch', dimmed = false }: {
  pattern: 'solid' | 'striped'; color: string | undefined; fileId?: string; variant?: keyof typeof marks; dimmed?: boolean;
}) {
  return <span aria-hidden="true" data-variant={variant} data-pattern={pattern} data-file={fileId}
    style={{ '--coverage-color': color ?? 'var(--color-muted)' } as CSSProperties}
    className={`coverage-mark ${marks[variant]}${dimmed ? ' opacity-20' : ''}`} />;
}
