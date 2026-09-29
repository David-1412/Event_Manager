/**
 * Reports every non-ASCII run in the tree as escape sequences, so the output
 * cannot be mangled by the terminal's code page: PowerShell 5.1 renders a
 * correct UTF-8 section sign and a damaged stand-in for it identically on
 * screen, so only code points can be trusted. Used to prove that the files
 * written through the editor are clean UTF-8.
 *
 * Any run that is not an approved dash, quote, ellipsis, section sign, bullet,
 * diagram glyph or emoji is reported.
 *
 *   node scripts/inspect-encoding.mjs        -> summary (fails on suspects)
 */
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

const ROOTS = ["src", "docs", "scripts"].filter((r) => {
  try {
    return statSync(r).isDirectory();
  } catch {
    return false;
  }
});

const files = [];
function walk(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) walk(path);
    else if (/\.(ts|tsx|css|md|mjs|json)$/.test(entry.name)) files.push(path);
  }
}
ROOTS.forEach(walk);

const escapes = (text) =>
  [...text]
    .map((c) => {
      const cp = c.codePointAt(0);
      return cp < 0x20 || cp > 0x7e ? "\\u" + cp.toString(16).padStart(4, "0") : c;
    })
    .join("");

// Characters we actually intend to ship, by code point so this file needs no
// non-ASCII bytes of its own: dashes, curly quotes, ellipsis, section sign,
// middle dot, nbsp, the box/arrow/check glyphs used in the JoinButton state
// diagram, the sun and diamond UI glyphs, the Command key symbol, and emoji.
const ALLOWED_SINGLE = [
  0x2013, 0x2014, // en dash, em dash
  0x2018, 0x2019, 0x201c, 0x201d, // curly quotes
  0x2026, // ellipsis
  0x00a7, // section sign (spec references)
  0x00b7, // middle dot (metadata separators)
  0x00a0, // nbsp
  0x2500, 0x2502, 0x251c, 0x2514, 0x25b6, 0x2713, // state diagram + check
  0x25c6, // header diamond
  0x2600, // sun (theme toggle)
  0x2318, // command key hint
  0x26bd, // soccer ball (a sport icon outside the emoji plane range below)
  0x2605, 0x2606, // filled/star outline (Interested toggle label)
  0x21c4, // north-east-south-west arrow (Interest/Join state-transition diagrams)
  0xfe0f, // variation selector accompanying emoji
];
const ALLOWED = new Set(ALLOWED_SINGLE);
const isEmoji = (cp) => cp >= 0x1f000 && cp <= 0x1ffff;
const isAllowed = (cp) => ALLOWED.has(cp) || isEmoji(cp);

const suspect = [];
let runs = 0;
for (const file of files) {
  const text = readFileSync(file, "utf8");
  if (text.includes("\uFFFD")) suspect.push(`${file}: REPLACEMENT CHARACTER`);
  const lines = text.split("\n");
  lines.forEach((line, index) => {
    for (const match of line.matchAll(/[^\x00-\x7F]+/g)) {
      runs += 1;
      const bad = [...match[0]].some((c) => !isAllowed(c.codePointAt(0)));
      if (bad) {
        suspect.push(`${file}:${index + 1} ${escapes(match[0])}`);
      }
    }
  });
}

console.log(`inspect-encoding: ${files.length} files, ${runs} non-ASCII runs.`);
if (suspect.length === 0) {
  console.log("OK - every non-ASCII run is an approved dash, quote, section sign or emoji.");
} else {
  console.log("SUSPECT RUNS:");
  suspect.forEach((s) => console.log("  " + escapes(s)));
}
process.exit(suspect.length === 0 ? 0 : 1);
