// arguments[0] = scope index, arguments[1] = JSON config { text:{phrases}, radio:{phrases,option}, select:{phrases,option} }
// Finds the best First-Name text box, Gender radio and State dropdown INSIDE ONE SCOPE by scoring candidates on
// what a human would read (label, aria-label, placeholder, nearby text, group heading, options), not on fixed selectors.
const idx = arguments[0];
const cfg = JSON.parse(arguments[1]);
const scopes = allScopes();
if (idx >= scopes.length) return null;
const root = scopes[idx].root;

const norm = s => (s || '').replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
const words = s => norm(s).split(' ').filter(Boolean);
const visible = el => { const r = el.getBoundingClientRect(), cs = getComputedStyle(el);
  return r.width > 0 && r.height > 0 && cs.visibility !== 'hidden' && cs.display !== 'none'; };
const isControl = e => e.matches('input:not([type=hidden]),select,textarea');
const textOf = n => (n.innerText || n.textContent || '');

// 1 = exact phrase, lower = extra words around it
function match(text, phrase, joined) {
  const w = words(text); if (!w.length) return 0;
  if (phrase.every(p => w.includes(p))) return 1 / (1 + Math.max(0, w.length - phrase.length) * 0.2);
  if (joined && w.join('').includes(phrase.join(''))) return 0.6;
  return 0;
}
function score(signals, spec) {
  let total = 0;
  for (const [text, weight, joined] of signals)
    for (const ph of spec.phrases) total += weight * match(text, ph, joined);
  return total;
}
function precedingText(el) {
  let n = el;
  for (let i = 0; i < 4 && n && n.parentElement; i++) {
    let s = n.previousElementSibling;
    while (s) { if (!isControl(s) && !s.querySelector('input,select,textarea') && textOf(s).trim()) return textOf(s); s = s.previousElementSibling; }
    n = n.parentElement;
    if (n.querySelectorAll('input:not([type=hidden]),select,textarea').length > 1) break;
  }
  return '';
}
function signals(el) {
  const s = [];
  if (el.labels) for (const l of el.labels) s.push([textOf(l), 10, false]);
  const lb = el.getAttribute('aria-labelledby');
  if (lb) lb.split(/\s+/).forEach(id => { const e = root.getElementById && root.getElementById(id); if (e) s.push([textOf(e), 10, false]); });
  s.push([el.getAttribute('aria-label'), 8, false], [el.placeholder, 8, false], [el.title, 6, false]);
  s.push([precedingText(el), 6, false]);
  s.push([el.getAttribute('name'), 3, true], [el.id, 3, true]);       // weak hints only
  return s;
}

function run(kind, spec) {
  let best = null, bestScore = 0, dbg = '';
  const consider = (el, sc, d) => { if (sc > bestScore) { best = el; bestScore = sc; dbg = d; } };

  if (kind === 'text') {
    root.querySelectorAll('input, textarea').forEach(el => {
      const t = (el.getAttribute('type') || 'text').toLowerCase();
      if (!['text', 'search', ''].includes(t) && el.tagName !== 'TEXTAREA') return;
      if (!visible(el) || el.disabled || el.readOnly) return;
      consider(el, score(signals(el), spec), 'text input');
    });
  } else if (kind === 'select') {
    root.querySelectorAll('select').forEach(el => {
      if (!visible(el) || el.disabled) return;
      let sc = score(signals(el), spec);
      const opts = [...el.options].map(o => norm(o.text));
      if (spec.option && opts.includes(norm(spec.option))) sc += 6;     // requested value exists here
      consider(el, sc, 'select with ' + el.options.length + ' options');
    });
  } else if (kind === 'radio') {
    const forms = [...root.querySelectorAll('form')];
    const groups = new Map();
    root.querySelectorAll('input[type=radio]').forEach((el, i) => {
      if (el.disabled) return;
      const key = el.name ? 'n:' + el.name + '|' + forms.indexOf(el.form) : 'u:' + i;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(el);
    });
    const optText = r => ((r.labels && r.labels[0]) ? textOf(r.labels[0]) : (r.nextSibling && r.nextSibling.textContent) || r.getAttribute('aria-label') || r.value || '');
    const want = norm(spec.option);
    groups.forEach(radios => {
      const opt = radios.find(r => norm(optText(r)) === want)
               || radios.find(r => words(optText(r)).includes(want))
               || radios.find(r => norm(r.value) === want);
      if (!opt) return;
      if (!(visible(opt) || (opt.labels && [...opt.labels].some(visible)))) return;
      let a = radios[0].parentNode;
      while (a && !radios.every(r => a.contains(r))) a = a.parentNode;
      const fs = radios[0].closest('fieldset'); const legend = fs && fs.querySelector('legend');
      const sg = [];
      if (legend) sg.push([textOf(legend), 10, false]);
      if (a) { if (a.nodeType === 1) sg.push([precedingText(a), 8, false]); sg.push([textOf(a), 4, false]); }
      sg.push([radios[0].name, 3, true], [radios[0].id, 2, true]);
      let sc = score(sg, spec);
      const labels = radios.map(r => norm(optText(r)));
      if (labels.includes('male') && labels.includes('female')) sc += 5;   // looks like a gender group
      consider(opt, sc, 'radio group of ' + radios.length);
    });
  }
  return best ? [best, JSON.stringify({ score: bestScore.toFixed(1), info: dbg })] : null;
}

const t = run('text', cfg.text), r = run('radio', cfg.radio), s = run('select', cfg.select);
return [t ? t[0] : null, r ? r[0] : null, s ? s[0] : null,
        JSON.stringify({ text: t && t[1], radio: r && r[1], select: s && s[1] })];
