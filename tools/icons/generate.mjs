// Writes FSharp.Wasm.Icons/Lucide.fs: the icons named in icons.txt, as typed shapes, from the SVG files of Lucide.
//
//   npm pack lucide-static@<version>                 # in a new, empty directory
//   mkdir x && tar -xzf lucide-static-<version>.tgz -C x package/icons package/LICENSE
//   node tools/icons/generate.mjs <that directory>/x/package <version>
//
// Lucide is ISC (https://github.com/lucide-icons/lucide/blob/main/LICENSE); the icons it took over from Feather are MIT.  Both are compatible with this repository's
// Apache-2.0, and their text is in LICENSES/ISC-MIT-lucide.txt.  The generator takes only the shapes Lucide draws with (path, circle, ellipse, rect, line, polyline,
// polygon) and only the attributes that say where they are: anything else stops it, so that a new kind of icon is noticed, not dropped.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const [pkg, version] = process.argv.slice(2);
if (!pkg || !version) { console.error('usage: node generate.mjs <unpacked lucide-static/package> <version>'); process.exit(2); }
const here = (p) => fileURLToPath(new URL(p, import.meta.url));
const names = readFileSync(here('icons.txt'), 'utf8').split('\n').map((l) => l.trim()).filter((l) => l && !l.startsWith('#'));

const reserved = new Set(['abstract', 'and', 'as', 'assert', 'base', 'begin', 'class', 'default', 'delegate', 'do', 'done', 'downcast', 'downto', 'elif', 'else', 'end', 'exception', 'extern', 'false', 'finally', 'fixed', 'for', 'fun', 'function', 'global', 'if', 'in', 'inherit', 'inline', 'interface', 'internal', 'lazy', 'let', 'match', 'member', 'module', 'mutable', 'namespace', 'new', 'null', 'of', 'open', 'or', 'override', 'private', 'public', 'rec', 'return', 'sig', 'static', 'struct', 'then', 'to', 'true', 'try', 'type', 'upcast', 'use', 'val', 'void', 'when', 'while', 'with', 'yield', 'const', 'component', 'constraint', 'include', 'mixin', 'parallel', 'process', 'protected', 'pure', 'sealed', 'tailcall', 'trait', 'virtual']);
const camel = (n) => n.replace(/-([a-z0-9])/g, (_, c) => c.toUpperCase());
const num = (s) => {
  const n = Number(s);
  if (!Number.isFinite(n)) throw new Error(`not a number: ${s}`);
  return Number.isInteger(n) ? `${n}.0` : String(n);
};
const attrs = (text) => Object.fromEntries([...text.matchAll(/([a-zA-Z0-9:-]+)="([^"]*)"/g)].map((m) => [m[1], m[2]]));
const only = (name, a, allowed, optional = []) => { for (const k of Object.keys(a)) if (!allowed.includes(k) && !optional.includes(k)) throw new Error(`${name}: unexpected attribute ${k}`); for (const k of allowed) if (!(k in a)) throw new Error(`${name}: missing ${k}`); return a; };
const points = (s) => { const v = s.trim().split(/[\s,]+/).map(num); if (v.length % 2) throw new Error('odd points'); return `[ ${v.reduce((acc, x, i) => (i % 2 ? [...acc.slice(0, -1), `${acc.at(-1)}, ${x}`] : [...acc, x]), []).map((p) => `(${p})`).join('; ')} ]`; };
const shape = (tag, a) => {
  switch (tag) {
    case 'path': only(tag, a, ['d']); return `Path "${a.d}"`;
    case 'circle':
      only(tag, a, ['cx', 'cy', 'r'], ['fill']);
      // a dot of Lucide's palette: filled, with a radius that the stroke of 2 covers anyway, so the fill adds nothing and is left out; any other fill would be lost: stop
      if ('fill' in a && !(a.fill === 'currentColor' && Number(a.r) <= 0.5)) throw new Error(`circle: a fill that the stroke does not cover (${a.fill}, r ${a.r})`);
      return `Circle (${num(a.cx)}, ${num(a.cy)}, ${num(a.r)})`;
    case 'ellipse': only(tag, a, ['cx', 'cy', 'rx', 'ry']); return `Ellipse (${num(a.cx)}, ${num(a.cy)}, ${num(a.rx)}, ${num(a.ry)})`;
    case 'rect': only(tag, a, ['x', 'y', 'width', 'height'], ['rx', 'ry']); if ((a.ry ?? a.rx) !== (a.rx ?? a.ry)) throw new Error('rect with two different radii'); return `Rect (${num(a.x)}, ${num(a.y)}, ${num(a.width)}, ${num(a.height)}, ${num(a.rx ?? a.ry ?? '0')})`;
    case 'line': only(tag, a, ['x1', 'y1', 'x2', 'y2']); return `Line (${num(a.x1)}, ${num(a.y1)}, ${num(a.x2)}, ${num(a.y2)})`;
    case 'polyline': only(tag, a, ['points']); return `Polyline ${points(a.points)}`;
    case 'polygon': only(tag, a, ['points']); return `Polygon ${points(a.points)}`;
    default: throw new Error(`unknown shape <${tag}>`);
  }
};

const out = [];
const ids = [];
for (const name of names) {
  const svg = readFileSync(`${pkg}/icons/${name}.svg`, 'utf8');
  const body = svg.slice(svg.indexOf('>', svg.indexOf('<svg')) + 1, svg.lastIndexOf('</svg>'));
  const shapes = [...body.matchAll(/<(\w+)\s([^>]*?)\/>/g)].map((m) => shape(m[1], attrs(m[2])));
  if (!shapes.length || body.replace(/<(\w+)\s([^>]*?)\/>/g, '').trim() !== '') throw new Error(`${name}: not only self-closing shapes`);
  const id = camel(name);
  if (reserved.has(id)) throw new Error(`${name}: ${id} is a keyword of F#`);
  ids.push(id);
  out.push(`let ${id} : Icon = icon "${name}" [ ${shapes.join('; ')} ]`);
}
if (new Set(ids).size !== ids.length) throw new Error('two icons with one name');

const text = `// <auto-generated>  by tools/icons/generate.mjs from lucide-static ${version} (ISC; the icons Lucide took over from Feather are MIT).  Do not edit: edit tools/icons/icons.txt and run it again.
// Licence texts: LICENSES/ISC-MIT-lucide.txt.  Every icon is drawn on a 24 x 24 grid with a stroke of 2, round ends and round joins (see Icons.fs).
namespace FSharp.Wasm.Icons

/// The icons of Lucide (https://lucide.dev) that the platform uses.
module Lucide =
${out.map((l) => '    ' + l).join('\n')}

    /// All of them, in the order of tools/icons/icons.txt.
    let all : Icon list = [ ${ids.join('; ')} ]

    /// The icon with this name (as Lucide calls it: "grip-vertical").
    let tryFind (name: string) : Icon option = all |> List.tryFind (fun i -> i.Name = name)
`;
writeFileSync(here('../../FSharp.Wasm.Icons/Lucide.fs'), text);
console.log(`${names.length} icons from lucide-static ${version}`);
