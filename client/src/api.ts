export async function getCsrfToken(): Promise<string> {
  const challenge = await fetch('/api/csrf');
  if (!challenge.ok) throw new Error('Could not reach the server. Try again.');
  const { token } = await challenge.json() as { token: string };
  return token;
}
