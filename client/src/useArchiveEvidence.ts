import { useEffect, useState } from 'react';

type ArchiveEvidence = { imagePages: number; comicInfo: { label: string; value: string }[] };

export function useArchiveEvidence(rootId: string, fileId: string | undefined, onSignedOut: () => void, version = '') {
  const url = fileId ? `/api/file-operations/duplicates/${rootId}/${fileId}/archive` : undefined;
  const [evidence, setEvidence] = useState<{ url: string; archive: ArchiveEvidence | null }>();

  useEffect(() => {
    if (!url) return;
    const controller = new AbortController();
    void fetch(url, { signal: controller.signal }).then(async response => {
      if (response.status === 401) return onSignedOut();
      const archive = response.ok ? await response.json() as ArchiveEvidence : null;
      if (!controller.signal.aborted) setEvidence({ url, archive });
    }).catch(() => { if (!controller.signal.aborted) setEvidence({ url, archive: null }); });
    return () => controller.abort();
  }, [url, onSignedOut, version]);

  return evidence?.url === url ? evidence?.archive : undefined;
}
