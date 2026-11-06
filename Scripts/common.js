// Lists every "scope" in the current browsing context: the document plus every (nested) open shadow root.
// Iframes are handled from C# because each one is a separate browsing context.
function allScopes() {
  const out = [];
  (function walk(root, label, depth) {
    out.push({ root, label, depth });
    root.querySelectorAll('*').forEach(e => {
      if (e.shadowRoot) {
        const host = e.tagName.toLowerCase() + (e.id ? '#' + e.id : '');
        walk(e.shadowRoot, (depth === 0 ? '' : label + ' > ') + 'shadow(' + host + ')', depth + 1);
      }
    });
  })(document, 'document', 0);
  return out;
}
