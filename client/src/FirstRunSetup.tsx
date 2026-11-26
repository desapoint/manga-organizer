import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { FolderField } from './FolderField';
import { Button } from './ui';

export type SetupStatus = { libraryConfigured: boolean };

function setupToken() {
  return new URLSearchParams(window.location.hash.slice(1)).get('setup');
}

export function FirstRunSetup({ libraryConfigured }: SetupStatus) {
  const [token, setToken] = useState(setupToken);
  const [phase, setPhase] = useState<'editing' | 'saving' | 'starting' | 'waiting'>('editing');
  const [error, setError] = useState('');
  const [libraryPath, setLibraryPath] = useState('');

  useEffect(() => {
    const updateToken = () => setToken(setupToken());
    window.addEventListener('hashchange', updateToken);
    return () => window.removeEventListener('hashchange', updateToken);
  }, []);

  async function waitForServer() {
    setPhase('starting');
    for (let attempt = 0; attempt < 40; attempt++) {
      await new Promise(resolve => window.setTimeout(resolve, 250));
      const health = await fetch('/healthz', { cache: 'no-store' }).catch(() => null);
      if (health?.ok) {
        window.location.reload();
        return;
      }
    }
    setPhase('waiting');
  }

  async function completeSetup(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token) return;
    const form = event.currentTarget;
    const values = new FormData(form);
    const password = String(values.get('password') ?? '');
    const confirmPassword = String(values.get('confirmPassword') ?? '');
    if (password !== confirmPassword) {
      setError('The passwords must match.');
      return;
    }
    setError('');
    setPhase('saving');
    try {
      const response = await fetch('/api/setup', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Setup-Token': token },
        body: JSON.stringify({ password, confirmPassword, libraryPath: values.get('libraryPath') }),
      });
      if (response.ok || response.status === 409 || response.status === 404) {
        form.reset();
        window.history.replaceState(null, '', window.location.pathname + window.location.search);
        await waitForServer();
        return;
      }
      if (response.status === 403) {
        setError('Open the current one-time setup link shown in the server window.');
      } else if (response.status === 400) {
        const details = await response.json().catch(() => null) as { message?: string } | null;
        setError(details?.message ?? 'Check the password and library folder, then try again.');
      } else {
        setError('The server could not save setup. Try again.');
      }
    } catch {
      setError('Could not reach the server. Try again.');
    }
    setPhase('editing');
  }

  return (
    <section className="surface auth-panel" aria-labelledby="setup-heading">
      <h1 id="setup-heading">Set up your server</h1>
      {!token && <p>Open the one-time setup link shown in the Manga Organizer server window.</p>}
      {token && (phase === 'editing' || phase === 'saving') && (
        <form onSubmit={completeSetup}>
          <label htmlFor="setup-password">Owner password</label>
          <input id="setup-password" name="password" type="password" autoComplete="new-password" required minLength={12} maxLength={256} aria-describedby="setup-password-help" />
          <span className="field-help" id="setup-password-help">Use 12–256 characters. You’ll use this password to sign in.</span>
          <label htmlFor="setup-confirm">Confirm password</label>
          <input id="setup-confirm" name="confirmPassword" type="password" autoComplete="new-password" required minLength={12} maxLength={256} />
          {!libraryConfigured && <FolderField id="setup-library" label="Library folder (optional)" value={libraryPath}
            onChange={setLibraryPath} setupToken={token} />}
          {error && <p role="alert" className="error">{error}</p>}
          <Button type="submit" disabled={phase === 'saving'}>{phase === 'saving' ? 'Saving…' : 'Finish setup'}</Button>
        </form>
      )}
      {phase === 'starting' && <p role="status">Starting your server…</p>}
      {phase === 'waiting' && (
        <>
          <p role="status">Setup was saved. Check the server window if startup takes longer than expected.</p>
          <Button onClick={waitForServer}>Check again</Button>
        </>
      )}
    </section>
  );
}
