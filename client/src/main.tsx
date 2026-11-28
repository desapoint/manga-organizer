import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { createRoot } from 'react-dom/client';
import { useAppearance } from './appearance';
import { getCsrfToken } from './api';
import { Workspace } from './Workspace';
import { FirstRunSetup } from './FirstRunSetup';
import type { SetupStatus } from './FirstRunSetup';
import { ToastProvider } from './Toast';
import { AuthLayout, Button, PageHeader } from './ui';
import './styles.css';

type Server = { id: string };
type Connection = 'checking' | 'signed-out' | 'unavailable' | Server | SetupStatus;

async function loadServer(signal?: AbortSignal): Promise<Connection> {
  const response = await fetch('/api/server', { signal });
  if (response.status === 401) {
    const setup = await fetch('/api/setup', { signal });
    if (setup.status === 404) return 'signed-out';
    return setup.ok ? setup.json() as Promise<SetupStatus> : 'unavailable';
  }
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
  const { appearance, setAppearance } = useAppearance();
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

  if (typeof connection === 'object' && 'libraryConfigured' in connection) {
    return (
      <AuthLayout>
        <FirstRunSetup libraryConfigured={connection.libraryConfigured} />
      </AuthLayout>
    );
  }

  if (typeof connection === 'object') {
    return <Workspace serverId={connection.id} signingOut={busy} error={error}
      onSignOut={signOut} onSignedOut={returnToSignIn} appearance={appearance} onAppearanceChange={setAppearance} />;
  }

  return (
    <AuthLayout>
      <section className="surface auth-panel" aria-labelledby="server-heading">
        <PageHeader id="server-heading" section="Server access" title="Primary server" />
        {connection === 'checking' && <p role="status">Checking connection…</p>}
        {connection === 'unavailable' && (
          <>
            <p role="alert">The server is unavailable.</p>
            <Button onClick={() => window.location.reload()}>Try again</Button>
          </>
        )}
        {connection === 'signed-out' && (
          <form className="mt-4" onSubmit={signIn}>
            <label htmlFor="password">Owner password</label>
            <input id="password" name="password" type="password" autoComplete="current-password" required maxLength={256} />
            <Button type="submit" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</Button>
          </form>
        )}
        {error && <p role="alert" className="error">{error}</p>}
      </section>
    </AuthLayout>
  );
}

createRoot(document.getElementById('root')!).render(<ToastProvider><App /></ToastProvider>);
