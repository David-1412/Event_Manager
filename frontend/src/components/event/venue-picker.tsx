"use client";

import { useEffect, useRef, useState } from "react";
import { ApiError, API_BASE_URL, usingFixtures } from "@/lib/api";
import { haversineKm } from "@/lib/fixtures";
import { MELBOURNE_CBD } from "@/lib/sports";

export interface VenueSelection {
  venueName: string;
  address: string;
  latitude: number;
  longitude: number;
}

export interface VenueRow extends VenueSelection {
  distanceKm: number;
}

/**
 * VenuePicker (spec §8): input -> debounced `GET /api/geo/search` -> rows with
 * address + distance. Picking fills read-only `venueName/address/lat/lng` chips
 * with a `Change` link, so coordinates can never be hand-edited into a location
 * that does not match the typed address.
 *
 * Offline we fall back to a known-Melbourne-venue list so the flow - including
 * the "no results" path - stays exercisable before the API exists.
 */
export const OFFLINE_VENUES: VenueRow[] = [
  ["Glen Waverley Badminton Centre", "100 Kingsway, Glen Waverley VIC 3150", -37.8708, 145.1615],
  ["Bob Jane Running Track", "Acland St, Melbourne VIC 3000", -37.8247, 144.9786],
  ["Lakeside Stadium", "Storramadrup Rd, Melbourne VIC 3009", -37.8136, 144.9331],
  ["Melbourne Sports and Aquatic Centre", "Olympian Way, West Melbourne VIC 3003", -37.8024, 144.9437],
  ["Coode Island Tennis Centre", "Kings Way, Melbourne VIC 3008", -37.8055, 144.9525],
  ["Olympic Park", "747 Lewar St, Melbourne VIC 3000", -37.81, 144.9803],
  ["Footscray Sports Centre", "1 Hopkins St, Footscray VIC 3011", -37.7996, 144.8993],
  ["Princes Park", "261 Princes Park Ave, Carlton VIC 3053", -37.7873, 144.9681],
].map(([venueName, address, lat, lng]) => ({
  venueName: venueName as string,
  address: address as string,
  latitude: lat as number,
  longitude: lng as number,
  distanceKm: haversineKm(
    MELBOURNE_CBD.latitude,
    MELBOURNE_CBD.longitude,
    lat as number,
    lng as number,
  ),
}));

export function offlineSearch(query: string): VenueRow[] {
  const needle = query.toLowerCase();
  return OFFLINE_VENUES.filter(
    (v) =>
      v.venueName.toLowerCase().includes(needle) || v.address.toLowerCase().includes(needle),
  ).slice(0, 6);
}

export async function searchVenues(query: string, signal: AbortSignal): Promise<VenueRow[]> {
  const response = await fetch(
    `${API_BASE_URL}/api/geo/search?q=${encodeURIComponent(query)}`,
    { headers: { Accept: "application/json" }, signal },
  );
  const text = await response.text();
  if (!response.ok) {
    throw new ApiError({ status: response.status, title: response.statusText || "Search failed" });
  }
  const payload = text ? JSON.parse(text) : [];
  return (payload as Record<string, unknown>[]).map((row) => ({
    venueName: String(row.venueName ?? row.name ?? ""),
    address: String(row.address ?? ""),
    latitude: Number(row.latitude),
    longitude: Number(row.longitude),
    distanceKm: Number(row.distanceKm ?? 0),
  }));
}

export function VenuePicker({
  value,
  onChange,
  error,
}: {
  value: VenueSelection | null;
  onChange: (venue: VenueSelection | null) => void;
  error?: string;
}) {
  const [term, setTerm] = useState("");
  const [rows, setRows] = useState<VenueRow[]>([]);
  const [open, setOpen] = useState(false);
  const [searching, setSearching] = useState(false);
  const [failed, setFailed] = useState(false);
  const controllerRef = useRef<AbortController | null>(null);
  const listId = "venue-results";

  // 300ms debounce + AbortController so an in-flight query for a term the user
  // already replaced cannot land out of order (spec §11). Every state write is
  // deferred into the timer or a promise callback, never synchronous in the body.
  useEffect(() => {
    const query = term.trim();
    const timer = setTimeout(() => {
      if (query.length < 3) {
        setRows([]);
        setSearching(false);
        return;
      }
      controllerRef.current?.abort();
      const controller = new AbortController();
      controllerRef.current = controller;
      const done = (results: VenueRow[]) => {
        if (controller.signal.aborted) return;
        setRows(results);
        setSearching(false);
        setOpen(true);
      };
      const failedSearch = () => {
        if (controller.signal.aborted) return;
        setFailed(true);
        setRows([]);
        setSearching(false);
      };
      if (usingFixtures) {
        done(offlineSearch(query));
        return;
      }
      searchVenues(query, controller.signal).then(done, failedSearch);
    }, 300);
    return () => clearTimeout(timer);
  }, [term]);

  if (value) return <VenueChips venue={value} onChange={onChange} />;

  return (
    <div className="relative">
      <input
        type="search"
        role="combobox"
        value={term}
        onChange={(e) => {
          setTerm(e.target.value);
          if (e.target.value.trim().length >= 3) setSearching(true);
        }}
        onFocus={() => rows.length > 0 && setOpen(true)}
        placeholder="Search a venue, park or court"
        aria-label="Venue"
        aria-expanded={open}
        aria-autocomplete="list"
        aria-controls={listId}
        aria-invalid={Boolean(error) || undefined}
        autoComplete="off"
        className="h-11 w-full rounded-md border border-border bg-surface px-3 text-body text-fg placeholder:text-fg-muted aria-[invalid=true]:border-danger"
      />
      {searching && (
        <span className="absolute right-3 top-3 text-meta text-fg-muted" role="status">
          Searching
        </span>
      )}
      {open && (
        <ul
          id={listId}
          role="listbox"
          aria-label="Venue results"
          className="absolute z-10 mt-1 max-h-64 w-full overflow-y-auto rounded-md border border-border bg-surface shadow-float"
        >
          {failed && (
            <li className="px-3 py-2 text-meta text-danger">
              Venue search is unavailable right now - check the name and try again.
            </li>
          )}
          {!failed && rows.length === 0 && !searching && term.trim().length >= 3 && (
            <li className="px-3 py-2 text-meta text-fg-muted">
              No venue matched &quot;{term.trim()}&quot;.
            </li>
          )}
          {rows.map((row) => (
            <li key={`${row.latitude},${row.longitude}`} role="option" aria-selected={false}>
              <button
                type="button"
                onClick={() => {
                  onChange({
                    venueName: row.venueName,
                    address: row.address,
                    latitude: row.latitude,
                    longitude: row.longitude,
                  });
                  setOpen(false);
                }}
                className="press flex w-full flex-col items-start gap-0.5 px-3 py-2 text-left hover:bg-surface-2"
              >
                <span className="text-meta font-medium text-fg">{row.venueName}</span>
                <span className="text-meta text-fg-muted">
                  {row.address} {"\u00B7"} {row.distanceKm.toFixed(1)} km
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Read-only chips for the picked venue; `Change` clears back to search. */
export function VenueChips({
  venue,
  onChange,
}: {
  venue: VenueSelection;
  onChange: (venue: VenueSelection | null) => void;
}) {
  return (
    <div className="flex flex-col gap-1">
      <dl className="flex flex-col gap-1 rounded-md border border-border bg-surface-2 p-3 text-meta">
        <div>
          <dt className="sr-only">Venue</dt>
          <dd className="font-medium text-fg">{venue.venueName}</dd>
        </div>
        <div>
          <dt className="sr-only">Address</dt>
          <dd className="text-fg-muted">{venue.address}</dd>
        </div>
      </dl>
      <button
        type="button"
        onClick={() => onChange(null)}
        className="press self-start text-meta font-medium text-brand-600 hover:underline"
      >
        Change
      </button>
    </div>
  );
}

