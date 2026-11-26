import { useEffect, useState } from 'react';

export type Appearance = { theme: 'ink' | 'paper'; accent: 'violet' | 'mint' | 'coral' };

const defaults: Appearance = { theme: 'ink', accent: 'violet' };
const storageKey = 'manga-organizer-appearance';

function loadAppearance(): Appearance {
  try {
    const saved = JSON.parse(localStorage.getItem(storageKey) ?? 'null') as Partial<Appearance> | null;
    return {
      theme: saved?.theme === 'paper' ? 'paper' : 'ink',
      accent: saved?.accent === 'mint' || saved?.accent === 'coral' ? saved.accent : 'violet',
    };
  } catch {
    return defaults;
  }
}

export function useAppearance() {
  const [appearance, setAppearance] = useState(loadAppearance);
  useEffect(() => {
    document.documentElement.dataset.theme = appearance.theme;
    document.documentElement.dataset.accent = appearance.accent;
    try {
      localStorage.setItem(storageKey, JSON.stringify(appearance));
    } catch {
      // The current page keeps working when storage is unavailable.
    }
  }, [appearance]);
  return { appearance, setAppearance };
}
