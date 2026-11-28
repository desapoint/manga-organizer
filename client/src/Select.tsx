import * as SelectPrimitive from '@radix-ui/react-select';
import { Check, ChevronDown } from 'lucide-react';
import { useContext, useId } from 'react';
import { ModalPortalContext } from './ui';

export type SelectOption<Value extends string> = {
  value: Value;
  label: string;
  description?: string;
};

export function Select<Value extends string>({ label, value, options, onChange, disabled, hideLabel = false, className = '' }: {
  label: string;
  value: Value;
  options: readonly SelectOption<Value>[];
  onChange: (value: Value) => void;
  disabled?: boolean;
  hideLabel?: boolean;
  className?: string;
}) {
  const id = useId();
  const portal = useContext(ModalPortalContext);

  return (
    <div className={`grid min-w-0 gap-1.5 ${className}`}>
      <label htmlFor={id} className={hideLabel ? 'sr-only' : undefined}>{label}</label>
      <SelectPrimitive.Root value={value} disabled={disabled} onValueChange={value => {
        const option = options.find(option => option.value === value);
        if (option) onChange(option.value);
      }}>
        <SelectPrimitive.Trigger id={id}
          className="select-trigger group flex w-full items-center justify-between gap-2 text-start enabled:cursor-pointer
            enabled:hover:border-accent disabled:opacity-65 [&>span:first-child]:truncate">
          <SelectPrimitive.Value />
          <SelectPrimitive.Icon asChild>
            <ChevronDown className="size-4 shrink-0 text-muted group-data-[state=open]:rotate-180" aria-hidden="true" />
          </SelectPrimitive.Icon>
        </SelectPrimitive.Trigger>
        <SelectPrimitive.Portal container={portal ?? undefined}>
          <SelectPrimitive.Content position="popper" sideOffset={7} align="start" collisionPadding={12}
            className="select-list z-30 w-[var(--radix-select-trigger-width)] max-w-[var(--radix-select-content-available-width)]
              overflow-hidden rounded-xl border border-control bg-elevated text-ink shadow-lg">
            <SelectPrimitive.Viewport className="select-viewport p-1.5">
              {options.map(option => (
                <SelectPrimitive.Item key={option.value} value={option.value}
                  className="relative grid min-h-9 cursor-pointer content-center rounded-md py-2 pe-2 ps-8 text-sm leading-tight wrap-anywhere
                    outline-none data-[highlighted]:bg-nav-hover data-[state=checked]:bg-nav-active data-[state=checked]:text-accent-readable">
                  <SelectPrimitive.ItemIndicator className="absolute start-2.5 top-1/2 -translate-y-1/2">
                    <Check className="size-4 text-accent-readable" aria-hidden="true" />
                  </SelectPrimitive.ItemIndicator>
                  <SelectPrimitive.ItemText>{option.label}</SelectPrimitive.ItemText>
                  {option.description && <span className="text-xs text-muted">{option.description}</span>}
                </SelectPrimitive.Item>
              ))}
            </SelectPrimitive.Viewport>
          </SelectPrimitive.Content>
        </SelectPrimitive.Portal>
      </SelectPrimitive.Root>
    </div>
  );
}
