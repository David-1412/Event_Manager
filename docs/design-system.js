/* Preview logic for design-system.html.
   Mirrors the real JoinButton state machine described in UIUX_DESIGN_SPEC.md §7,
   so the behaviour is reviewed here before it is written in React. */

// theme toggle
const root = document.documentElement;
document.getElementById("theme").onclick = () => root.classList.toggle("dark");

// swatches read straight from tokens.css - no hard-coded colours in this file
const tokens = [
  ["--page", "bg"],
  ["--surface", "surface"],
  ["--surface-2", "surface-2"],
  ["--fg", "fg"],
  ["--fg-muted", "fg-muted"],
  ["--border", "border"],
  ["--brand", "brand-600"],
  ["--brand-tint", "brand-100"],
  ["--warn", "warn"],
  ["--danger", "danger"],
  ["--info", "info"],
];
document.getElementById("swatches").innerHTML = tokens
  .map(
    ([v, n]) =>
      `<div class="swatch"><i style="background:var(${v})"></i><span>${n}<br><code>${v}</code></span></div>`,
  )
  .join("");

// EventCard variants: [variant, title, sportIcon, sport, skill, cur, max, meta, cta, kind]
const variants = [
  ["open", "Badminton Monday", "🏸", "Badminton", "Intermediate", 2, 4, "Tonight 6PM · 3 km away", "Join", "primary"],
  ["one-spot", "Thursday Run at the Track", "🏃", "Running", "Beginner", 7, 8, "Thu 7AM · 1.2 km away", "Join", "primary"],
  ["full", "Social Soccer", "⚽", "Soccer", "Intermediate", 8, 8, "Sat 4PM · 5 km away", "Full", "primary"],
  ["joined", "Basketball Thursday", "🏀", "Basketball", "Advanced", 5, 6, "Thu 8PM · 2 km away", "Joined ✓", "secondary"],
  ["mine", "Badminton Wednesday", "🏸", "Badminton", "Intermediate", 3, 4, "Wed 6PM · Glen Waverley BC", "Leave event", "ghost"],
  ["cancelled", "Sunday Cricket", "🏏", "Cricket", "Beginner", 0, 6, "Cancelled by host", "", ""],
];
document.getElementById("cards").innerHTML =
  variants
    .map(
      ([v, t, icon, sport, skill, cur, max, meta, cta, kind]) => `
  <article class="card surface-card press" data-variant="${v}" tabindex="0">
    <div class="sport"><span aria-hidden="true">${icon}</span><span>${sport}</span>
      <span class="badge" style="margin-left:auto;background:var(--surface-2);color:var(--fg-muted)">${skill}</span>
    </div>
    <span class="title">${t}</span>
    <span class="meta">${meta}</span>
    <div class="foot">
      <span class="count" data-count>${cur}/${max}</span>
      ${v === "one-spot" ? '<span class="badge tint-warn">1 spot left</span>' : ""}
      ${v === "full" ? '<span class="badge tint-danger">Full</span>' : ""}
      ${v === "mine" ? '<span class="badge tint-brand">You&#39;re hosting</span>' : ""}
      ${v === "cancelled" ? '<span class="badge tint-info">Cancelled</span>' : ""}
      ${cta ? `<button class="btn btn-${kind} btn-sm" style="margin-left:auto">${cta}</button>` : ""}
    </div>
  </article>`,
    )
    // skeleton has identical geometry, which is what keeps CLS under 0.05
    .join("") +
  `<article class="card surface-card" aria-hidden="true">
     <div class="skel" style="height:16px;width:120px"></div>
     <div class="skel" style="height:20px;width:70%"></div>
     <div class="skel" style="height:14px;width:55%"></div>
     <div class="foot"><div class="skel" style="height:24px;width:56px"></div>
       <div class="skel" style="height:34px;width:88px;margin-left:auto;border-radius:var(--radius-md)"></div>
     </div>
   </article>`;

document.getElementById("states").innerHTML = `
  <div class="card surface-card"><span class="badge tint-info" style="width:fit-content">Empty</span>
    <span class="title">No badminton within 10 km</span>
    <span class="meta">Nothing scheduled this week inside your filters.</span>
    <button class="btn btn-primary btn-sm" style="width:fit-content">Create the first one</button></div>
  <div class="card surface-card"><span class="badge tint-danger" style="width:fit-content">Error</span>
    <span class="title">Couldn&#39;t load events</span>
    <span class="meta">Your filters are kept - just try again.</span>
    <button class="btn btn-secondary btn-sm" style="width:fit-content">Try again</button>
    <details><summary class="hint">Details</summary><code>GET /api/events?sport=badminton 503</code></details></div>
  <div class="card surface-card"><span class="badge tint-warn" style="width:fit-content">Join rejected 409</span>
    <span class="title">This event just filled up</span>
    <span class="meta">The API rolled the optimistic count back 3/4 &rarr; 2/4.</span></div>`;

const count = document.getElementById("liveCount");
const toast = document.getElementById("toast");
function showToast(msg) {
  toast.textContent = msg;
  toast.dataset.show = "1";
  clearTimeout(showToast.t);
  showToast.t = setTimeout(() => delete toast.dataset.show, 2200);
}
function nudge(node) {
  node.dataset.changed = "up"; // .count-shift lifts it 4px, then we clear
  setTimeout(() => delete node.dataset.changed, 200);
}

// idle -> joining -> joined, or -> full on 409. Width is held for the spinner.
function runJoin() {
  const btn = document.getElementById("joinSmall");
  const [cur, max] = count.textContent.split("/").map(Number);
  btn.dataset.loading = "";
  setTimeout(() => {
    delete btn.dataset.loading;
    if (cur >= max) {
      btn.textContent = "Full";
      btn.disabled = true;
      count.style.color = "var(--danger)";
      showToast("This event just filled up");
      return;
    }
    count.textContent = `${cur + 1}/${max}`;
    nudge(count);
    btn.textContent = "Joined ✓";
    btn.className = "btn btn-secondary btn-sm";
    showToast(cur + 1 === max ? "That was the last spot" : `You&#39;re in - ${max - cur - 1} spot(s) left`);
  }, 650);
}
document.getElementById("joinSmall").onclick = runJoin;
document.getElementById("demoJoin").onclick = () => {
  const b = document.getElementById("joinSmall");
  count.textContent = "2/4";
  count.style.color = "";
  b.disabled = false;
  b.className = "btn btn-primary btn-sm";
  b.textContent = "Join";
  runJoin();
};
