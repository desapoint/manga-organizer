const allowedTags = new Set(['p', 'div', 'br', 'b', 'strong', 'i', 'em', 'u', 's', 'del', 'ol', 'ul', 'li', 'blockquote']);
const discardedTags = new Set(['script', 'style', 'iframe', 'object', 'embed', 'template', 'svg', 'math']);
const paragraphTags = new Set(['P', 'DIV']);
const blockTags = new Set(['OL', 'UL', 'BLOCKQUOTE']);

function safeNodes(markup: string) {
  const template = document.createElement('template');
  template.innerHTML = markup;
  const output = document.createElement('div');

  function copy(source: Node, parent: Node) {
    if (source.nodeType === Node.TEXT_NODE) {
      parent.appendChild(document.createTextNode(source.textContent ?? ''));
      return;
    }
    if (!(source instanceof Element) || discardedTags.has(source.localName)) return;
    const element = allowedTags.has(source.localName) ? document.createElement(source.localName) : null;
    if (element) {
      if (element instanceof HTMLOListElement && source instanceof HTMLOListElement && source.start > 1) element.start = source.start;
      parent.appendChild(element);
    }
    source.childNodes.forEach(child => copy(child, element ?? parent));
  }

  template.content.childNodes.forEach(child => copy(child, output));
  return output;
}

export function cleanSummaryMarkup(markup: string) {
  return safeNodes(markup).innerHTML;
}

export function formatSummary(plain: string, markup?: string | null) {
  const input = markup ? safeNodes(markup) : document.createElement('div');
  if (!markup) input.textContent = plain;
  const result = document.createElement('div');
  let line = document.createElement('p');
  let list: HTMLOListElement | null = null;

  function flush() {
    const text = line.textContent ?? '';
    if (!text.trim()) return;
    const numbered = /^\s*(\d{1,6})\.\s+/.exec(text);
    if (numbered) {
      const number = Number(numbered[1]);
      if (!list || list.start + list.children.length !== number) {
        list = document.createElement('ol');
        if (number !== 1) list.start = number;
        result.append(list);
      }
      const range = document.createRange();
      range.selectNodeContents(line);
      const words = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
      let remaining = numbered[0].length;
      for (let node = words.nextNode(); node; node = words.nextNode()) {
        const length = node.textContent?.length ?? 0;
        if (length >= remaining) {
          range.setEnd(node, remaining);
          break;
        }
        remaining -= length;
      }
      range.deleteContents();
      const item = document.createElement('li');
      item.append(...line.childNodes);
      list.append(item);
    } else {
      list = null;
      result.append(line);
    }
    line = document.createElement('p');
  }

  function collect(nodes: NodeListOf<ChildNode>) {
    for (const node of [...nodes]) {
      if (node instanceof HTMLElement && paragraphTags.has(node.tagName)) {
        flush();
        collect(node.childNodes);
        flush();
      } else if (node instanceof HTMLElement && blockTags.has(node.tagName)) {
        flush();
        list = null;
        result.append(node);
      } else if (node instanceof HTMLBRElement) {
        flush();
      } else if (node.nodeType === Node.TEXT_NODE) {
        const parts = (node.textContent ?? '').split(/\r\n?|\n/);
        for (const [index, part] of parts.entries()) {
          if (index > 0) flush();
          line.append(part);
        }
      } else {
        line.append(node);
      }
    }
  }

  collect(input.childNodes);
  flush();
  return result.innerHTML;
}

export function summaryText(markup: string) {
  const safe = safeNodes(markup);

  function text(node: Node): string {
    if (node.nodeType === Node.TEXT_NODE) return node.textContent ?? '';
    if (node instanceof HTMLBRElement) return '\n';
    if (node instanceof HTMLOListElement) {
      return [...node.children].map((item, index) => `${node.start + index}. ${text(item).trim()}`).join('\n') + '\n';
    }
    const content = [...node.childNodes].map(text).join('');
    return node instanceof HTMLElement && (paragraphTags.has(node.tagName) || blockTags.has(node.tagName) || node.tagName === 'LI')
      ? content + '\n' : content;
  }

  return text(safe).trim();
}
