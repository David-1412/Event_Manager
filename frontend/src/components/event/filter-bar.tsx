"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Chip, ChipRow } from "@/components/ui/chip";
import { Dialog } from "@/components/ui/dialog";
import { Select } from "@/components/ui/field";
import { cn } from "@/lib/cn";
import { activeFilterCount } from "@/lib/query";
import { RADIUS_OPTIONS } from "@/lib/sports";
import type { BrowseLayoutMode, BrowseMapMode } from "@/components/event/browse-layout";
import type { EventDateFilter, EventQuery, EventSort, PopularTag } from "@/types/events";
import { ViewModeToggle } from "@/components/event/view-mode-toggle";

/**
 * Sticky, gains a shadow once the list scrolls (spec §7). Mobile: scrolling tag
 * chips + a `Filters` button carrying the active count, opening a bottom sheet
 * with Apply/Reset - chips scroll off-screen, so the count is the only proof of
 * what's applied. Desktop: the same controls inline.
 * Every change writes straight to the URL through `onPatch`.
 *
 * `popularTags` replaces the hardcoded SPORTS list. It arrives from
 * GET /api/tags/popular and is empty on a fresh database (the demo seed creates no
 * tags), so the row renders nothing rather than a stray divider - a deliberate
 * product choice, not a gap.
 */
export function FilterBar({
  query,
  onPatch,
  onReset,
  popularTags = [],
  mapMode = "none",
  onMapMode,
  mode = "list",
  onMode,
}: {
  query: EventQuery;
  onPatch: (patch: Partial<EventQuery>) => void;
  onReset: () => void;
  popularTags?: readonly PopularTag[];
  /** Desktop map mode, so the control can name the state it will move to. */
  mapMode?: BrowseMapMode;
  /** Cycles the map: none -> split -> none. When omitted the button is not
   * rendered (pages that keep the map fixed open don't offer the control). */
  onMapMode?: (mode: BrowseMapMode) => void;
  /** List vs grid. Paired with `onMode`; both optional so other FilterBar callers
   * (and their tests) render unchanged without a view-mode control. */
  mode?: BrowseLayoutMode;
  onMode?: (mode: BrowseLayoutMode) => void;
}) {
  const [scrolled, setScrolled] = useState(false);
  const [sheetOpen, setSheetOpen] = useState(false);
  const activeCount = activeFilterCount(query);

  const onPickTag = (tag: string) =>
    onPatch({ tag: query.tag === tag ? null : tag });


  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 4);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  return (
    <div
      className={cn(
        // top-14 clears the 56px site header while the window scrolls. From lg up
        // the list column is its own scroll container, where top-14 would be
        // measured from the column itself and leave a 56px gap under the header.
        "sticky top-14 z-20 border-b border-border bg-surface px-4 py-3 lg:top-0",
        scrolled && "shadow-raise",
      )}
    >
      {/* All / Following and the text search moved up: the tabs into the page's
          sub-navigation, the search into the global search bar (whose Events chip
          writes the same `q` this list reads). */}
      <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
        <div className="flex items-center gap-2">
          {/* Mobile: chips scroll horizontally and also live in the sheet. */}
          <TagChipRow
            tags={popularTags}
            selected={query.tag}
            onPick={onPickTag}
            className="lg:hidden"
          />

          <TagChipRow
            tags={popularTags}
            selected={query.tag}
            onPick={onPickTag}
            className="hidden lg:flex"
          />


          <button
            type="button"
            onClick={() => setSheetOpen(true)}
            className="press inline-flex h-[34px] shrink-0 items-center gap-2 rounded-full border border-border bg-surface-2 px-3 text-meta font-medium text-fg lg:hidden"
            aria-haspopup="dialog"
            aria-expanded={sheetOpen}
          >
            Filters
            {activeCount > 0 && (
              <span className="rounded-full bg-brand-tint px-1.5 text-micro text-brand-600">
                {activeCount}
              </span>
            )}
          </button>

          <div className="hidden items-center gap-2 lg:flex">
            <DateSelect value={query.date} onChange={(v) => onPatch({ date: v })} />
            <RadiusSelect value={query.radiusKm} onChange={(v) => onPatch({ radiusKm: v })} />
            <SortSelect value={query.sort} onChange={(v) => onPatch({ sort: v })} />
            {activeCount > 0 && (
              <button
                type="button"
                onClick={onReset}
                className="press h-9 rounded-md px-3 text-meta font-medium text-brand-600 hover:bg-brand-tint"
              >
                Reset
              </button>
            )}
            {onMode && (
              <ViewModeToggle mode={mode} onMode={onMode} />
            )}
            {onMapMode && (
              // Show Map opens the map beside the list; a second tap closes it.
              // The label names the state the tap produces, not the state the
              // control is in, so it never reads as a dead toggle.
              <button
                type="button"
                onClick={() => onMapMode(mapMode === "none" ? "split" : "none")}
                aria-pressed={mapMode !== "none"}
                className="press inline-flex h-9 shrink-0 items-center gap-2 rounded-md border border-border bg-surface px-3 text-meta font-medium text-fg hover:bg-surface-2"
              >
                <span aria-hidden>{"\u25C6"}</span>
                {mapMode === "none" ? "Show Map" : "Hide Map"}
              </button>
            )}
          </div>
        </div>
      </div>

      <FilterSheet
        open={sheetOpen}
        query={query}
        onClose={() => setSheetOpen(false)}
        onPatch={onPatch}
        onReset={onReset}
        popularTags={popularTags}
        onPickTag={onPickTag}
      />
    </div>
  );
}



function FilterSelect({
  label,
  id,
  value,
  onChange,
  options,
}: {
  label: string;
  id: string;
  value: string;
  onChange: (value: string) => void;
  options: { value: string; label: string }[];
}) {
  return (
    <div className="flex items-center gap-1">
      <label htmlFor={id} className="text-meta text-fg-muted">
        {label}
      </label>
      <Select
        id={id}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="h-9 w-auto min-w-24 text-meta"
      >
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </Select>
    </div>
  );
}

const DATE_OPTIONS = [
  { value: "today", label: "Today" },
  { value: "week", label: "This week" },
  { value: "any", label: "Any time" },
];

const SORT_OPTIONS = [
  { value: "startAt", label: "Soonest" },
  { value: "distance", label: "Closest" },
];

const radiusOptions = () => [
  { value: "", label: "Anywhere" },
  ...RADIUS_OPTIONS.map((r) => ({ value: String(r), label: `${r} km` })),
];

function DateSelect({
  value,
  onChange,
  id = "filter-when",
  label = "When",
}: {
  value: EventDateFilter;
  onChange: (value: EventDateFilter) => void;
  id?: string;
  label?: string;
}) {
  return (
    <FilterSelect
      id={id}
      label={label}
      value={value}
      options={DATE_OPTIONS}
      onChange={(v) => onChange(v as EventDateFilter)}
    />
  );
}

function RadiusSelect({
  value,
  onChange,
  id = "filter-within",
  label = "Within",
}: {
  value: number | null;
  onChange: (value: number | null) => void;
  id?: string;
  label?: string;
}) {
  return (
    <FilterSelect
      id={id}
      label={label}
      value={value === null ? "" : String(value)}
      options={radiusOptions()}
      onChange={(v) => onChange(v ? Number(v) : null)}
    />
  );
}

function SortSelect({
  value,
  onChange,
  id = "filter-sort",
  label = "Sort",
}: {
  value: EventSort;
  onChange: (value: EventSort) => void;
  id?: string;
  label?: string;
}) {
  return (
    <FilterSelect
      id={id}
      label={label}
      value={value}
      options={SORT_OPTIONS}
      onChange={(v) => onChange(v as EventSort)}
    />
  );
}


/**
 * Mobile bottom sheet. Tapping applies immediately (same single source of truth
 * as the desktop row) and `Apply` closes it - two apply models would diverge.
 */
function FilterSheet({
  open,
  query,
  onClose,
  onPatch,
  onReset,
  popularTags,
  onPickTag,
}: {
  open: boolean;
  query: EventQuery;
  onClose: () => void;
  onPatch: (patch: Partial<EventQuery>) => void;
  onReset: () => void;
  popularTags: readonly PopularTag[];
  onPickTag: (tag: string) => void;
}) {
  return (
    <Dialog open={open} onClose={onClose} labelledBy="filters-title" variant="sheet">
      <h2 id="filters-title" className="text-h3 text-fg">
        Filters
      </h2>

      <fieldset className="mt-4">
        <legend className="text-meta font-medium text-fg">Tags</legend>
        {popularTags.length === 0 ? (
          <p className="mt-2 text-meta text-fg-muted">
            No tags yet. Add one when you create an event, e.g. #tennis.
          </p>
        ) : (
          <div className="mt-2 flex flex-wrap gap-2">
            {popularTags.map((t) => (
              <Chip
                key={t.name}
                selected={query.tag === t.name}
                onClick={() => onPickTag(t.name)}
              >
                #{t.name}
                <span className="text-fg-muted">{t.count}</span>
              </Chip>
            ))}
          </div>
        )}
      </fieldset>


      <div className="mt-4 flex flex-col gap-3">
        <DateSelect
          id="sheet-when"
          value={query.date}
          onChange={(v) => onPatch({ date: v })}
        />
        <RadiusSelect
          id="sheet-within"
          value={query.radiusKm}
          onChange={(v) => onPatch({ radiusKm: v })}
        />
        <SortSelect id="sheet-sort" value={query.sort} onChange={(v) => onPatch({ sort: v })} />
      </div>

      <div className="mt-5 flex gap-2">
        <Button variant="secondary" fullWidth onClick={onReset}>
          Reset
        </Button>
        <Button fullWidth onClick={onClose}>
          Apply
        </Button>
      </div>
    </Dialog>
  );
}

/**
 * The popular-tag chips. Renders nothing when there are no tags: an empty row of
 * chips would read as a broken control rather than "nobody has tagged anything
 * yet", which is the actual state of a fresh database.
 *
 * One component because the same list renders in three places (mobile scroll row,
 * desktop row, sheet) and a hardcoded list previously let those drift.
 */
function TagChipRow({
  tags,
  selected,
  onPick,
  className,
}: {
  tags: readonly PopularTag[];
  selected: string | null;
  onPick: (tag: string) => void;
  className?: string;
}) {
  if (tags.length === 0) return null;

  return (
    <ChipRow className={className}>
      {tags.map((t) => (
        <Chip key={t.name} selected={selected === t.name} onClick={() => onPick(t.name)}>
          #{t.name}
        </Chip>
      ))}
    </ChipRow>
  );
}


