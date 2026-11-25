import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { createRoot } from 'react-dom/client';
import { getCsrfToken } from './api';
import { Catalog } from './Catalog';
import './styles.css';

type Server = { id: string };
type Connection = 'checking' | 'signed-out' | 'unavailable' | Server;

async function loadServer(signal?: AbortSignal): Promise<Connection> {
  const response = await fetch('/api/server', { signal });
  if (response.status === 401) return 'signed-out';
  if (!response.ok) return 'unavailable';
  return response.json() as Promise<Server>;
}

async function changeSession(method: 'POST' | 'DELETE', password?: string) {
  const headers = new Headers({ 'X-CSRF-Token': await getCsrfToken() });
  if (method === 'POST') headers.set('Content-Type', 'application/json');

  return fetch('/api/session', {
    method,
    headers,
    body: method === 'POST' ? JSON.stringify({ password }) : undefined,
  });
}

function App() {
  const [connection, setConnection] = useState<Connection>('checking');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const returnToSignIn = useCallback(() => setConnection('signed-out'), []);

  useEffect(() => {
    const request = new AbortController();
    loadServer(request.signal).then(setConnection, () => {
      if (!request.signal.aborted) setConnection('unavailable');
    });
    return () => request.abort();
  }, []);

  async function signIn(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const password = String(new FormData(form).get('password') ?? '');
    setBusy(true);
    setError('');
    try {
      const response = await changeSession('POST', password);
      if (response.status === 401) {
        setError('That password was not accepted.');
      } else if (response.status === 429) {
        setError('Too many attempts. Wait a minute and try again.');
      } else if (!response.ok) {
        setError('The server could not sign you in. Try again.');
      } else {
        form.reset();
        setConnection(await loadServer());
      }
    } catch {
      setError('Could not reach the server. Try again.');
    } finally {
      setBusy(false);
    }
  }

  async function signOut() {
    setBusy(true);
    setError('');
    try {
      const response = await changeSession('DELETE');
      if (response.ok || response.status === 401) {
        setConnection('signed-out');
      } else {
        setError('The server could not sign you out. Try again.');
      }
    } catch {
      setError('Could not reach the server. Try again.');
    } finally {
      setBusy(false);
    }
  }

  if (typeof connection === 'object') {
    return (
      <main className="catalog-layout">
        <header className="catalog-header">
          <div className="brand">Manga Organizer</div>
          <button type="button" className="secondary" onClick={signOut} disabled={busy}>
            {busy ? 'Signing out…' : 'Sign out'}
          </button>
        </header>
        {error && <p role="alert" className="error">{error}</p>}
        <Catalog onSignedOut={returnToSignIn} />
        <dl className="server-details">
          <dt>Server ID</dt>
          <dd><code>{connection.id}</code></dd>
        </dl>
      </main>
    );
  }

  return (
    <main>
      <header className="brand">Manga Organizer</header>
      <section className="panel" aria-labelledby="server-heading">
        <h1 id="server-heading">Primary server</h1>
        {connection === 'checking' && <p role="status">Checking connection…</p>}
        {connection === 'unavailable' && (
          <>
            <p role="alert">The server is unavailable.</p>
            <button type="button" onClick={() => window.location.reload()}>Try again</button>
          </>
        )}
        {connection === 'signed-out' && (
          <form onSubmit={signIn}>
            <label htmlFor="password">Owner password</label>
            <input id="password" name="password" type="password" autoComplete="current-password" required maxLength={256} />
            <button disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
          </form>
        )}
        {error && <p role="alert" className="error">{error}</p>}
      </section>
    </main>
  );
}

createRoot(document.getElementById('root')!).render(<App />);
