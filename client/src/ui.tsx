import { BookOpen } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import type { ComponentProps, ReactNode } from 'react';

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

export function ActionLink({ icon: Icon, className = '', children, ...props }:
  ComponentProps<'a'> & Pick<ActionStyle, 'icon'>) {
  return (
    <a {...props} className={`${actionClass} justify-center ${buttonSizes.regular} ${buttonVariants.primary} ${className}`}>
      {Icon && <Icon className="size-4 shrink-0" aria-hidden="true" />}
      {children}
    </a>
  );
}

export function Brand() {
  return (
    <span className="inline-flex min-w-0 items-center gap-2.5 text-sm leading-tight font-bold tracking-tight text-ink desktop:text-base">
      <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-linear-to-br from-accent-hover to-accent text-accent-ink">
        <BookOpen className="size-5" aria-hidden="true" />
      </span>
      <span>Manga Organizer</span>
    </span>
  );
}

export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <main className="mx-auto grid min-h-svh max-w-[35rem] content-start gap-8 p-[clamp(1rem,6vw,4rem)]">
      <header><Brand /></header>
      {children}
    </main>
  );
}
