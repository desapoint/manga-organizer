import { Files, FolderCog, Layers, NotebookPen, Settings2 } from 'lucide-react';

export const toolsNavigation = [
  { module: 'duplicates', href: '#tools/duplicates', label: 'Duplicates', icon: Files },
  { module: 'versions', href: '#tools/versions', label: 'Versions and alternatives', icon: Layers },
  { module: 'metadata', href: '#tools/metadata', label: 'Associations and metadata', icon: NotebookPen },
  { module: 'organize', href: '#tools/organize', label: 'Organize', icon: FolderCog },
  { module: 'maintenance', href: '#tools/maintenance', label: 'Maintenance', icon: Settings2 },
] as const;
