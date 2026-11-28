import { Check, CircleAlert, X } from 'lucide-react';
import { createContext, useCallback, useContext, useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { createPortal } from 'react-dom';

type Kind = 'success' | 'error';
type Notice = { id: number; kind: Kind; message: string };
type Notify = (kind: Kind, message: string) => void;
const ToastContext = createContext<{ notify: Notify; attach: (element: HTMLElement) => () => void } | null>(null);

export function useToast(): Notify {
  return useContext(ToastContext)!.notify;
}

export function ToastAnchor() {
  const target = useRef<HTMLDivElement>(null);
  const attach = useContext(ToastContext)?.attach;
  useLayoutEffect(() => {
    if (target.current) return attach?.(target.current);
  }, [attach]);
  return <div ref={target} />;
}

function ToastItem({ notice, dismiss }: { notice: Notice; dismiss: (id: number) => void }) {
  const { id, kind, message } = notice;
  const [hold, setHold] = useState(false);
  useEffect(() => {
    if (hold) return;
    const timer = window.setTimeout(() => dismiss(id), kind === 'error' ? 10000 : 5500);
    return () => window.clearTimeout(timer);
  }, [dismiss, hold, id, kind]);
  const Icon = kind === 'error' ? CircleAlert : Check;
  return <li onMouseEnter={() => setHold(true)} onMouseLeave={() => setHold(false)}
    onFocus={() => setHold(true)} onBlur={() => setHold(false)}
    className="pointer-events-auto flex items-center gap-3 rounded-xl border border-control bg-elevated p-3 text-ink shadow-lg">
    <Icon aria-hidden="true" className={`size-4 shrink-0 ${kind === 'error' ? 'text-danger' : 'text-positive'}`} />
    <p role={kind === 'error' ? 'alert' : 'status'} className="m-0 flex-1 text-sm wrap-anywhere">{message}</p>
    <button type="button" aria-label="Dismiss notification" title="Dismiss notification"
      className="grid size-8 shrink-0 cursor-pointer place-items-center rounded-md border-0 bg-transparent text-muted hover:bg-nav-hover hover:text-ink"
      onClick={() => dismiss(id)}><X className="size-4" aria-hidden="true" /></button>
  </li>;
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [notices, setNotices] = useState<Notice[]>([]);
  const [anchors, setAnchors] = useState<HTMLElement[]>([]);
  const nextId = useRef(0);
  const notify = useCallback<Notify>((kind, message) => {
    const id = ++nextId.current;
    setNotices(current => [...current.slice(-3), { id, kind, message }]);
  }, []);
  const dismiss = useCallback((id: number) => setNotices(current => current.filter(notice => notice.id !== id)), []);
  const attach = useCallback((element: HTMLElement) => {
    setAnchors(current => [...current, element]);
    return () => setAnchors(current => current.filter(anchor => anchor !== element));
  }, []);
  return <ToastContext.Provider value={{ notify, attach }}>
    {children}
    {notices.length > 0 && createPortal(
      <ul aria-label="Notifications" className="pointer-events-none fixed right-4 bottom-[calc(1rem+env(safe-area-inset-bottom))]
        z-50 m-0 grid w-[min(24rem,calc(100%-2rem))] list-none gap-2 p-0 max-[50rem]:bottom-[calc(5.5rem+env(safe-area-inset-bottom))]">
        {notices.map(notice => <ToastItem key={notice.id} notice={notice} dismiss={dismiss} />)}
      </ul>, anchors.at(-1) ?? document.body)}
  </ToastContext.Provider>;
}
