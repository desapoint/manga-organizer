import { Plus, X } from 'lucide-react';
import { useId, useRef, useState } from 'react';
import { Button } from './ui';

export function MultiValueField({ label, itemLabel, values, onChange, limit, maxLength, disabled }: {
  label: string; itemLabel: string; values: string[]; onChange: (values: string[]) => void;
  limit: number; maxLength: number; disabled?: boolean;
}) {
  const id = useId();
  const entry = useRef<HTMLInputElement>(null);
  const [input, setInput] = useState('');
  const [error, setError] = useState('');

  function add(raw: string) {
    const entries = raw.split(/\r\n?|\n/).map(value => value.trim()).filter(Boolean);
    if (!entries.length) return;

    const seen = new Set(values.map(value => value.toLowerCase()));
    const added = entries.filter(value => {
      const key = value.toLowerCase();
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    });
    if (added.some(value => value.length > maxLength) || values.length + added.length > limit) {
      setError(`Use up to ${limit} values here, with up to ${maxLength} characters each.`);
      return;
    }
    if (added.length) onChange([...values, ...added]);
    setInput('');
    setError('');
  }

  return <div className="grid min-w-0 gap-1">
    <label htmlFor={id}>{label}</label>
    <div className="flex min-w-0 flex-wrap items-center gap-1.5 rounded-lg border border-control bg-field px-1.5 py-1
      focus-within:border-accent-readable focus-within:ring-1 focus-within:ring-accent-readable"
      onBlur={event => { if (!disabled && !event.currentTarget.contains(event.relatedTarget)) add(input); }}>
      {values.map(value => <span key={value} className="inline-flex min-w-0 max-w-full items-center gap-1 rounded-full border border-nav-line
        bg-nav-active py-0.5 ps-2.5 pe-0.5 text-sm leading-tight text-ink">
        <span className="min-w-0 wrap-anywhere">{value}</span>
        <button type="button" aria-label={`Remove ${itemLabel} ${value}`} disabled={disabled}
          className="grid size-6 shrink-0 appearance-none place-items-center rounded-full border-0 bg-transparent p-0 text-muted
            enabled:cursor-pointer enabled:hover:bg-nav-hover enabled:hover:text-ink focus-visible:outline-offset-0 disabled:opacity-65"
          onClick={() => {
            entry.current?.focus();
            onChange(values.filter(item => item !== value));
            setError('');
          }}>
          <X className="size-3.5" strokeWidth={2.25} aria-hidden="true" />
        </button>
      </span>)}
      <div className="flex min-w-0 flex-[1_1_9rem] items-center">
        <input ref={entry} id={id} value={input} maxLength={maxLength} disabled={disabled} placeholder={`Add ${itemLabel}`}
          aria-invalid={!!error} aria-describedby={error ? id + '-error' : undefined}
          className="h-8 w-0 min-w-0 flex-1 self-center rounded-md border-0 bg-transparent px-2 text-sm
            placeholder:text-muted focus-visible:outline-none"
          onChange={event => { setInput(event.target.value); setError(''); }}
          onKeyDown={event => {
            if (event.key !== 'Enter' || event.nativeEvent.isComposing) return;
            event.preventDefault();
            add(input);
          }}
          onPaste={event => {
            const text = event.clipboardData.getData('text');
            if (!/[\r\n]/.test(text)) return;
            event.preventDefault();
            add(input + text);
          }} />
        <Button variant="ghost" size="compact" icon={Plus} className="w-9 shrink-0 focus-visible:outline-offset-0"
          aria-label={`Add ${itemLabel}`} title={`Add ${itemLabel}`} disabled={disabled || !input.trim()}
          onClick={() => { entry.current?.focus(); add(input); }} />
      </div>
    </div>
    {error && <p id={id + '-error'} role="alert" className="m-0 text-sm text-danger">{error}</p>}
  </div>;
}
