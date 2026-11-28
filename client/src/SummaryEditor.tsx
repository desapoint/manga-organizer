import { Bold, Italic, List, ListOrdered } from 'lucide-react';
import { useId, useLayoutEffect, useRef } from 'react';
import { cleanSummaryMarkup, formatSummary, summaryText } from './summaryMarkup';
import { Button } from './ui';

export const summaryStyle = 'wrap-anywhere [&_blockquote]:border-s-2 [&_blockquote]:border-control [&_blockquote]:ps-3 [&_b]:font-bold [&_strong]:font-bold [&_ol]:my-2 [&_ol]:list-decimal [&_ol]:ps-6 [&_p]:my-2 [&_ul]:my-2 [&_ul]:list-disc [&_ul]:ps-6';

const commands = [
  { label: 'Bold', command: 'bold', icon: Bold },
  { label: 'Italic', command: 'italic', icon: Italic },
  { label: 'Bulleted list', command: 'insertUnorderedList', icon: List },
  { label: 'Numbered list', command: 'insertOrderedList', icon: ListOrdered },
];

export function SummaryEditor({ summary, markup, onChange, disabled }: {
  summary: string; markup: string; onChange: (plain: string, markup: string) => void; disabled?: boolean;
}) {
  const id = useId();
  const entry = useRef<HTMLDivElement>(null);
  const selection = useRef<Range | null>(null);
  const edited = useRef<{ summary: string; markup: string } | null>(null);

  useLayoutEffect(() => {
    if (!entry.current || edited.current?.summary === summary && edited.current.markup === markup) return;
    entry.current.innerHTML = formatSummary(summary, markup);
    selection.current = null;
  }, [summary, markup]);

  function rememberSelection() {
    const current = window.getSelection();
    if (!current?.rangeCount) return;
    const range = current.getRangeAt(0);
    if (entry.current?.contains(range.commonAncestorContainer)) selection.current = range.cloneRange();
  }

  function changed() {
    const html = cleanSummaryMarkup(entry.current?.innerHTML ?? '');
    const plain = summaryText(html);
    edited.current = { summary: plain, markup: plain ? html : '' };
    onChange(plain, plain ? html : '');
    rememberSelection();
  }

  function format(command: string) {
    entry.current?.focus();
    const range = selection.current;
    if (range && entry.current?.contains(range.commonAncestorContainer)) {
      const current = window.getSelection();
      current?.removeAllRanges();
      current?.addRange(range);
    }
    document.execCommand(command);
    changed();
  }

  return <div className="grid min-w-0 gap-1">
    <span id={id} className="text-label">Summary</span>
    <div className="rounded-lg border border-control bg-field focus-within:border-accent-readable">
      <div role="group" aria-label="Summary formatting" className="flex flex-wrap gap-1 border-b border-line px-2 py-1">
        {commands.map(({ command, label, icon }) => <Button key={command} variant="ghost" size="compact" icon={icon}
          aria-label={label} title={label} disabled={disabled} onMouseDown={event => event.preventDefault()} onClick={() => format(command)} />)}
      </div>
      <div ref={entry} role="textbox" aria-labelledby={id} aria-multiline="true" aria-disabled={disabled || undefined}
        contentEditable={!disabled} tabIndex={disabled ? -1 : 0}
        className={`min-h-28 max-h-80 overflow-y-auto rounded-b-lg px-3 py-1 text-sm leading-relaxed focus-visible:outline-offset-0 ${summaryStyle}`}
        onInput={changed} onKeyUp={rememberSelection} onMouseUp={rememberSelection} onBlur={rememberSelection}
        onDrop={event => event.preventDefault()} onPaste={event => {
          event.preventDefault();
          const html = event.clipboardData.getData('text/html');
          if (html) document.execCommand('insertHTML', false, cleanSummaryMarkup(html));
          else document.execCommand('insertText', false, event.clipboardData.getData('text/plain'));
          changed();
        }} />
    </div>
  </div>;
}
