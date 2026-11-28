import { Plus, X } from 'lucide-react';
import { useId, useState } from 'react';
import { Button } from './ui';

export function MultiValueField({ label, itemLabel, values, onChange, limit, maxLength, disabled }: {
  label: string; itemLabel: string; values: string[]; onChange: (values: string[]) => void;
  limit: number; maxLength: number; disabled?: boolean;
}) {
  const id = useId();
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
    <div className="flex min-w-0 flex-wrap items-center gap-1 rounded-lg border border-control bg-field p-1 focus-within:border-accent">
      {values.map(value => <span key={value} className="inline-flex min-w-0 max-w-full items-center gap-1 rounded-md border border-line
        bg-panel ps-2 text-sm">
        <span className="wrap-anywhere">{value}</span>
        <button type="button" aria-label={`Remove ${itemLabel} ${value}`} disabled={disabled}
          className="grid size-7 shrink-0 place-items-center rounded-md text-muted enabled:hover:bg-nav-hover enabled:hover:text-ink"
          onClick={() => { onChange(values.filter(item => item !== value)); setError(''); }}>
          <X className="size-3.5" aria-hidden="true" />
        </button>
      </span>)}
      <input id={id} value={input} maxLength={maxLength} disabled={disabled} placeholder={`Add ${itemLabel}`}
        aria-invalid={!!error} aria-describedby={error ? id + '-error' : undefined}
        className="h-8 w-0 min-w-0 flex-[1_1_10rem] rounded-md border-0 bg-transparent px-2 py-1 text-sm
          focus-visible:outline-offset-0"
        onChange={event => { setInput(event.target.value); setError(''); }}
        onBlur={event => { if (!event.currentTarget.parentElement?.contains(event.relatedTarget)) add(input); }}
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
      <Button variant="ghost" size="compact" icon={Plus} aria-label={`Add ${itemLabel}`} title={`Add ${itemLabel}`}
        disabled={disabled || !input.trim()} onClick={() => add(input)} />
    </div>
    {error && <p id={id + '-error'} role="alert" className="m-0 text-sm text-danger">{error}</p>}
  </div>;
}
