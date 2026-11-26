import { BookOpen, Search, X } from 'lucide-react';
import { createContext, useId, useLayoutEffect, useRef, useState } from 'react';
import type { LucideIcon } from 'lucide-react';
import type { ComponentProps, ReactNode } from 'react';
import type { ScanState } from './library';

const buttonVariants = {
  primary: 'border-transparent bg-accent text-accent-ink not-disabled:hover:bg-accent-hover',
  secondary: 'border-control bg-transparent text-secondary enabled:hover:bg-nav-hover',
  ghost: 'border-transparent bg-transparent text-muted enabled:hover:bg-nav-hover enabled:hover:text-ink',
};

type ActionStyle = { variant?: keyof typeof buttonVariants; icon?: LucideIcon };

const actionClass = 'inline-flex items-center gap-2 rounded-lg border text-sm leading-tight font-semibold no-underline transition-colors motion-reduce:transition-none';
const buttonSizes = {
  regular: 'min-h-11 px-3.5 py-2.5',
  compact: 'min-h-9 px-1.5 py-1',
};

export function Button({ type = 'button', variant = 'primary', size = 'regular', align = 'center', icon: Icon,
  className = '', children, ...props }:
  ComponentProps<'button'> & ActionStyle & { size?: keyof typeof buttonSizes; align?: 'center' | 'start' }) {
  const alignment = align === 'start' ? 'justify-start' : 'justify-center';
  return (
    <button {...props} type={type}
      className={`${actionClass} ${alignment} ${buttonSizes[size]} ${buttonVariants[variant]} cursor-pointer disabled:cursor-default disabled:opacity-65 ${className}`}>
      {Icon && <Icon className="size-4 shrink-0" aria-hidden="true" />}
      {children}
    </button>
  );
}

export function ActionLink({ variant = 'primary', icon: Icon, className = '', children, ...props }:
  ComponentProps<'a'> & ActionStyle) {
  return (
    <a {...props} className={`${actionClass} justify-center ${buttonSizes.regular} ${buttonVariants[variant]} ${className}`}>
      {Icon && <Icon className="size-4 shrink-0" aria-hidden="true" />}
      {children}
    </a>
  );
}

export function Brand({ compact = false }: { compact?: boolean }) {
  return (
    <span className="inline-flex min-w-0 items-center gap-2.5 text-sm leading-tight font-bold tracking-tight text-ink desktop:text-base">
      <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-linear-to-br from-accent-hover to-accent text-accent-ink">
        <BookOpen className="size-5" aria-hidden="true" />
      </span>
      <span className={compact ? 'sr-only' : undefined}>Manga Organizer</span>
    </span>
  );
}

export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <main className="grid min-h-svh place-items-center px-4 py-[clamp(2rem,8vw,5rem)]">
      <div className="grid w-full max-w-xl gap-6">
        <header className="flex justify-center"><Brand /></header>
        {children}
      </div>
    </main>
  );
}

export function PageHeader({ id, section, title, children }: { id: string; section: string; title: string; children?: ReactNode }) {
  return (
    <header className="grid gap-2 pb-2">
      <span className="text-xs font-semibold tracking-[.16em] text-accent-readable uppercase">{section}</span>
      <h1 id={id} className="m-0 text-[clamp(2rem,4vw,3.65rem)] leading-[1.05] font-bold tracking-[-.045em] wrap-anywhere">{title}</h1>
      {children}
    </header>
  );
}

export function SectionHeader({ id, title }: { id?: string; title: string }) {
  return <h2 id={id} className="m-0 text-lg font-semibold tracking-tight">{title}</h2>;
}

export const ModalPortalContext = createContext<HTMLElement | null>(null);

export function Modal({ title, onClose, children, className = '', wide = false }: {
  title: string; onClose: () => void; children: ReactNode; className?: string; wide?: boolean;
}) {
  const id = useId();
  const dialog = useRef<HTMLDialogElement>(null);
  const [portal, setPortal] = useState<HTMLElement | null>(null);

  useLayoutEffect(() => {
    const element = dialog.current;
    const previous = document.activeElement;
    element?.showModal();
    return () => {
      element?.close();
      if (previous instanceof HTMLElement && previous.isConnected) previous.focus({ preventScroll: true });
    };
  }, []);

  return <dialog ref={dialog} aria-labelledby={id} onCancel={event => { event.preventDefault(); onClose(); }}
    className={`m-auto ${wide ? 'w-[min(64rem,calc(100%-2rem))]' : 'w-[min(36rem,calc(100%-2rem))]'} max-h-[calc(100dvh-2rem)] overflow-auto rounded-xl border border-line bg-panel p-4 text-ink backdrop:bg-black/70 sm:p-5 ${className}`}>
    <div className="mb-4 flex items-center justify-between gap-3">
      <SectionHeader id={id} title={title} />
      <Button variant="ghost" icon={X} aria-label="Close dialog" onClick={onClose} autoFocus />
    </div>
    <ModalPortalContext.Provider value={portal}>{children}</ModalPortalContext.Provider>
    <div ref={setPortal} />
  </dialog>;
}

export function SegmentedControl<Value extends string>({ label, value, options, onChange }: {
  label: string;
  value: Value;
  options: readonly { value: Value; label: string }[];
  onChange: (value: Value) => void;
}) {
  return (
    <div role="group" aria-label={label} className="inline-flex w-fit flex-wrap gap-1 rounded-xl border border-line bg-field p-1">
      {options.map(option => (
        <button key={option.value} type="button" aria-pressed={value === option.value}
          className="min-h-11 cursor-pointer rounded-lg border border-transparent bg-transparent px-3
            text-sm font-semibold text-muted hover:bg-nav-hover aria-pressed:border-nav-line aria-pressed:bg-nav-active aria-pressed:text-ink"
          onClick={() => onChange(option.value)}>{option.label}</button>
      ))}
    </div>
  );
}

const statusStyles = {
  idle: { label: 'Ready', style: 'text-muted' },
  queued: { label: 'Queued', style: 'text-accent-readable' },
  scanning: { label: 'Scanning', style: 'text-accent-readable' },
  complete: { label: 'Indexed', style: 'text-positive' },
  unavailable: { label: 'Unavailable', style: 'text-warning' },
  incomplete: { label: 'Incomplete', style: 'text-warning' },
  emptyReview: { label: 'Needs review', style: 'text-warning' },
  interrupted: { label: 'Interrupted', style: 'text-warning' },
} satisfies Record<ScanState, { label: string; style: string }>;

export function ScanBadge({ state }: { state: ScanState }) {
  const { label, style } = statusStyles[state];
  return <span className={`inline-flex items-center gap-1.5 text-sm font-medium ${style}`}>
    <span className="size-1.5 shrink-0 rounded-full bg-current" aria-hidden="true" />{label}
  </span>;
}

export function SearchField({ className = '', ...props }: Omit<ComponentProps<'input'>, 'type'>) {
  return (
    <div className={`relative min-w-0 ${className}`}>
      <Search className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted" aria-hidden="true" />
      <input {...props} type="search" className="w-full ps-9" />
    </div>
  );
}
