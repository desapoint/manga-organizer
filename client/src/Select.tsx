import * as SelectPrimitive from '@radix-ui/react-select';
import { Check, ChevronDown, ChevronUp } from 'lucide-react';
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
          className="group flex min-h-11 w-full min-w-0 items-center justify-between gap-2 rounded-lg border border-control bg-field
            px-3.5 py-2.5 text-start text-base leading-tight text-ink enabled:cursor-pointer enabled:hover:border-accent
            disabled:opacity-65 data-[state=open]:border-accent [&>span:first-child]:truncate">
          <SelectPrimitive.Value />
          <SelectPrimitive.Icon asChild>
            <ChevronDown className="size-4 shrink-0 text-muted group-data-[state=open]:rotate-180" aria-hidden="true" />
          </SelectPrimitive.Icon>
        </SelectPrimitive.Trigger>
        <SelectPrimitive.Portal container={portal ?? undefined}>
          <SelectPrimitive.Content position="popper" sideOffset={5} align="start" collisionPadding={12}
            className="z-30 max-h-[min(20rem,var(--radix-select-content-available-height))] w-[var(--radix-select-trigger-width)]
              max-w-[var(--radix-select-content-available-width)] overflow-hidden rounded-xl border border-control bg-elevated text-ink shadow-lg">
            <SelectPrimitive.ScrollUpButton className="grid place-items-center text-muted">
              <ChevronUp className="size-4" aria-hidden="true" />
            </SelectPrimitive.ScrollUpButton>
            <SelectPrimitive.Viewport className="p-1">
              {options.map(option => (
                <SelectPrimitive.Item key={option.value} value={option.value}
                  className="relative grid min-h-10 cursor-default content-center rounded-lg py-2 pe-3 ps-9 text-sm wrap-anywhere
                    outline-none data-[highlighted]:bg-nav-hover data-[state=checked]:text-secondary">
                  <SelectPrimitive.ItemIndicator className="absolute start-3 top-2.5">
                    <Check className="size-4 text-accent-readable" aria-hidden="true" />
                  </SelectPrimitive.ItemIndicator>
                  <SelectPrimitive.ItemText>{option.label}</SelectPrimitive.ItemText>
                  {option.description && <span className="text-xs text-muted">{option.description}</span>}
                </SelectPrimitive.Item>
              ))}
            </SelectPrimitive.Viewport>
            <SelectPrimitive.ScrollDownButton className="grid place-items-center text-muted">
              <ChevronDown className="size-4" aria-hidden="true" />
            </SelectPrimitive.ScrollDownButton>
          </SelectPrimitive.Content>
        </SelectPrimitive.Portal>
      </SelectPrimitive.Root>
    </div>
  );
}
