"use client";

import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type CSSProperties,
} from "react";
import { useSyncExternalStore } from "react";
import { AdvancedMarker, useMap } from "@vis.gl/react-google-maps";
import {
  DEFAULT_CENTER,
  MapSurface,
  useAdvancedMarkerSupport,
} from "@/components/map/map-surface";
import { geocodeSearch, reverseGeocode, type GeocodeResult } from "@/components/map/geocode";
import {
  MAPS_API_KEY,
  lastMapsFailureReason,
  subscribeMapsFailure,
  type LatLng,
} from "@/components/map/map-types";
import { MELBOURNE_CBD } from "@/lib/sports";
import { haversineKm } from "@/lib/fixtures";
import { cn } from "@/lib/cn";
import { Button } from "@/components/ui/button";

export interface VenueSelection {
  venueName: string;
  address: string;
  latitude: number;
  longitude: number;
}

/**
 * `/create` "Where" control: pick a spot on Google Maps.
 *
 * Two-step, three ways in, one output (`VenueSelection`):
 *  - type a place name - debounced (250 ms) Geocoder suggestions, up to 8,
 *    arrow keys + Enter pick the highlighted row;
 *  - click anywhere on the map - the red pin drops there;
 *  - drag the red pin - the pin follows on release (dragging keeps working
 *    even when Google refuses to serve tiles or geocodes).
 * A drop or drag only *stages* the pin: nothing reaches the form until the
 * user presses **Select**, so the pin can be repositioned as many times as
 * they like first. The staged spot is reverse-geocoded (so the preview reads
 * like a place, not a bare coordinate) and, once selected, `Change` returns to
 * the map with the pin back on it.
 *
 * When Google rejects the key (the "This page can't load Google Maps
 * correctly" state), the map surface stays usable and a banner explains the
 * reason - it never masquerades as "no place matched".
 *
 * The map is an enhancement: with no API key (or a failed script) the search
 * box degrades to the built-in Melbourne venue list so the flow stays usable.
 */

const PICKER_HEIGHT = "h-[260px]";

export function VenuePicker({
  value,
  onChange,
  error,
}: {
  value: VenueSelection | null;
  onChange: (venue: VenueSelection | null) => void;
  error?: string;
}) {
  return value ? (
    <VenueChips venue={value} onChange={onChange} error={error} />
  ) : (
    <VenuePickerOpen onChange={onChange} error={error} />
  );
}

function VenuePickerOpen({
  onChange,
  error,
}: {
  onChange: (venue: VenueSelection | null) => void;
  error?: string;
}) {
  const [term, setTerm] = useState("");
  const [suggestions, setSuggestions] = useState<GeocodeResult[]>([]);
  const [open, setOpen] = useState(false);
  const [searching, setSearching] = useState(false);
  const [highlight, setHighlight] = useState(-1);
  const [mapCentre, setMapCentre] = useState<LatLng>(DEFAULT_CENTER);
  // The staged pin, held locally until the user presses Select. Null until the
  // first drop/drag/search pick; the pin renders here (falling back to
  // mapCentre, which starts as the city view with no pin).
  const [pending, setPending] = useState<VenueSelection | null>(null);
  const [mapsIssue, setMapsIssue] = useState<string | null>(lastMapsFailureReason);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const searchSeqRef = useRef(0);
  const commitSeqRef = useRef(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listId = "venue-suggestions";
  // SSR-safe "is the browser here" flag, so the Enter-to-enter-address handler
  // is a no-op during prerender (no window/Geocoder access on the server).
  const hydrated = useSyncExternalStore(
    () => () => {},
    () => true,
    () => false,
  );

  // Google refused something (key, tiles, or a geocode call) - explain it once
  // under the map instead of letting it look like an empty search result.
  useEffect(() => subscribeMapsFailure((failure) => setMapsIssue(failure.reason)), []);

  // Drop any pending debounce when the panel unmounts (Change pressed).
  useEffect(
    () => () => {
      searchSeqRef.current += 1;
      commitSeqRef.current += 1;
      if (debounceRef.current) clearTimeout(debounceRef.current);
    },
    [],
  );

  // Hand a finished selection to the form. Called only by Select (or a
  // suggestion pick), never by a bare drop - that just stages the pin.
  const commitSelection = useCallback(
    (venue: VenueSelection) => {
      commitSeqRef.current += 1; // drop any in-flight reverse-geocode
      setMapCentre({ lat: venue.latitude, lng: venue.longitude });
      setPending(null);
      onChange(venue);
    },
    [onChange],
  );

  // Stage a pin at a spot (map click, drag release, or a search pick). Moves
  // the pin and reverse-geocodes it so the preview shows a real place. Nothing
  // reaches the form until Select; the sequence number drops a late answer if
  // the pin moved again.
  const stage = useCallback(
    async (position: LatLng, known?: { name: string; address: string }) => {
      const point = { lat: position.lat, lng: position.lng };
      setMapCentre(point);
      if (known) {
        setPending({
          venueName: known.name,
          address: known.address,
          latitude: point.lat,
          longitude: point.lng,
        });
        return;
      }
      // Bare coordinate from a click or drag: reverse-geocode so the preview
      // reads like a place. On failure keep the raw point with coordinate
      // labels rather than blocking a selection.
      const seq = ++commitSeqRef.current;
      const match = await reverseGeocode(point);
      if (seq !== commitSeqRef.current) return;
      setPending({
        venueName: match?.name ?? `${point.lat.toFixed(5)}, ${point.lng.toFixed(5)}`,
        address: match?.address ?? `Pinned at ${point.lat.toFixed(5)}, ${point.lng.toFixed(5)}`,
        latitude: point.lat,
        longitude: point.lng,
      });
    },
    [],
  );

  // Confirm the staged pin and hand it to the form.
  function select() {
    if (pending) commitSelection(pending);
  }

  // 250 ms debounce so a burst of typing costs one geocode; the sequence
  // number drops a response for a term the user already replaced. A blank or
  // one-character term never reaches the Geocoder - that was the "No place
  // matched \"\"" report.
  function search(next: string) {
    setTerm(next);
    setHighlight(-1);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    const query = next.trim();
    if (query.length < 2) {
      setSuggestions([]);
      setOpen(false);
      setSearching(false);
      return;
    }
    setSearching(true);
    debounceRef.current = setTimeout(async () => {
      const seq = ++searchSeqRef.current;
      const results = MAPS_API_KEY
        ? await geocodeSearch(query, mapCentre)
        : offlineSearch(query);
      if (seq !== searchSeqRef.current) return;
      setSuggestions(results);
      setOpen(true);
      setHighlight(-1);
      setSearching(false);
    }, 250);
  }

  function pick(result: GeocodeResult) {
    setOpen(false);
    setTerm("");
    setSuggestions([]);
    setHighlight(-1);
    void stage(
      { lat: result.lat, lng: result.lng },
      { name: result.name, address: result.address },
    );
  }

  // Pressing Enter with a typed address and no suggestion highlighted still
  // sets a location: geocode the text and, if nothing matches, drop the pin at
  // the current map centre labelled with the typed address. `stage` is a stable
  // callback, so this reads the current mapCentre/onChange without a ref.
  async function enterTypedAddress(raw: string) {
    const query = raw.trim();
    if (!query) return;
    const results = MAPS_API_KEY ? await geocodeSearch(query, mapCentre) : offlineSearch(query);
    const match = results[0];
    if (match) {
      setOpen(false);
      setTerm("");
      setSuggestions([]);
      setHighlight(-1);
      void stage(
        { lat: match.lat, lng: match.lng },
        { name: match.name, address: match.address },
      );
      return;
    }
    void stage(mapCentre, { name: query, address: query });
  }

  // Combobox keyboard model: Down/Up walk the list, Enter (or Tab) accepts the
  // highlighted row, Escape closes. Without this, pressing Enter next to an
  // open dropdown did nothing and the click looked "not picked up".
  function onKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (!open || suggestions.length === 0) {
      // No list to walk: Enter commits whatever was typed as the location, even
      // when no place matched (it drops the pin at the typed address).
      if (event.key === "Enter") {
        const query = term.trim();
        if (!query) return;
        event.preventDefault();
        if (hydrated) void enterTypedAddress(query);
        return;
      }
      if (event.key === "Escape") setOpen(false);
      return;
    }
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setHighlight((h) => (h + 1) % suggestions.length);
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setHighlight((h) => (h <= 0 ? suggestions.length - 1 : h - 1));
    } else if (event.key === "Enter" || event.key === "Tab") {
      // With results open, Enter accepts the highlighted row - or the first
      // when nothing is highlighted, so Enter always sets a location.
      const index = highlight >= 0 ? highlight : 0;
      event.preventDefault();
      pick(suggestions[index]);
    } else if (event.key === "Escape") {
      setOpen(false);
      setHighlight(-1);
    }
  }

  useEffect(() => {
    if (highlight < 0) return;
    document
      .getElementById(`${listId}-${highlight}`)
      ?.scrollIntoView({ block: "nearest" });
  }, [highlight, listId]);

  const showEmptyHint =
    !searching && term.trim().length >= 2 && suggestions.length === 0;

  return (
    <div className="flex flex-col gap-3">
      <MapSurface
        center={mapCentre}
        zoom={13}
        heightClass={PICKER_HEIGHT}
        ariaLabel="Pick a venue location"
        keepMapOnFailure
        onMapClick={(position) => void stage(position)}
      >
        <div className="contents">
          {pending && (
            <PinMarker
              position={{ lat: pending.latitude, lng: pending.longitude }}
              onDrag={(position) => setMapCentre(position)}
              onDragEnd={(position) => void stage(position)}
            />
          )}
        </div>
      </MapSurface>

      {pending ? (
        // Staged: show where the pin sits and let the user confirm or keep
        // moving it. Nothing has reached the form yet.
        <div className="flex flex-col gap-2">
          <div className="flex items-start justify-between gap-3 rounded-md border border-border bg-surface-2 p-3 text-meta">
            <div className="min-w-0">
              <p className="truncate font-medium text-fg">{pending.venueName}</p>
              <p className="truncate text-fg-muted">{pending.address}</p>
            </div>
            <Button size="sm" onClick={select} className="shrink-0">
              Select
            </Button>
          </div>
          <p className="text-meta text-fg-muted">
            Tap the map or drag the red pin to move it, then Select to confirm.
          </p>
        </div>
      ) : (
        <p className="text-meta text-fg-muted">
          Tap the map, drag the red pin, or search above - then Select to confirm the location.
        </p>
      )}

      <div className="relative">
        <input
          ref={inputRef}
          type="search"
          role="combobox"
          value={term}
          onChange={(e) => search(e.target.value)}
          onKeyDown={onKeyDown}
          onFocus={() => suggestions.length > 0 && setOpen(true)}
          onBlur={() => setOpen(false)}
          placeholder="Enter address"
          aria-label="Venue"
          aria-expanded={open}
          aria-autocomplete="list"
          aria-controls={listId}
          aria-activedescendant={
            open && highlight >= 0 ? `${listId}-${highlight}` : undefined
          }
          aria-invalid={Boolean(error) || undefined}
          autoComplete="off"
          className="h-11 w-full rounded-md border border-border bg-surface px-3 text-body text-fg placeholder:text-fg-muted aria-[invalid=true]:border-danger"
        />
        {searching && (
          <span className="absolute right-3 top-3 text-meta text-fg-muted" role="status">
            Searching
          </span>
        )}
        {/* Mounted (invisible) whenever options exist so screen readers and
            E2E drivers see a stable listbox while results stream in. */}
        {(open || suggestions.length > 0) && (
          <ul
            id={listId}
            role="listbox"
            aria-label="Venue results"
            hidden={!open}
            // The list is a sibling overlay: keep focus on the combobox so a
            // mousedown here never fires the input's blur-close first.
            onMouseDown={(e) => e.preventDefault()}
            className="absolute z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-md border border-border bg-surface shadow-float"
          >
            {showEmptyHint && (
              <li className="px-3 py-2 text-meta text-fg-muted">
                No place matched &quot;{term.trim()}&quot; - press Enter to use it as the address,
                or tap the map or drag the red pin.
              </li>
            )}
            {suggestions.map((row, i) => (
              <li
                key={`${row.lat},${row.lng},${i}`}
                id={`${listId}-${i}`}
                role="option"
                aria-selected={i === highlight}
              >
                <button
                  type="button"
                  onClick={() => pick(row)}
                  className={
                    "press flex w-full flex-col items-start gap-0.5 px-3 py-2 text-left hover:bg-surface-2 data-[highlighted=true]:bg-surface-2"
                  }
                  data-highlighted={i === highlight || undefined}
                >
                  <span className="text-meta font-medium text-fg">{row.name}</span>
                  <span className="text-meta text-fg-muted">
                    {row.address}
                    {MAPS_API_KEY
                      ? ` \u00B7 ${haversineKm(
                          MELBOURNE_CBD.latitude,
                          MELBOURNE_CBD.longitude,
                          row.lat,
                          row.lng,
                        ).toFixed(1)} km`
                      : ""}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {mapsIssue && (
        <p className="text-meta text-danger" role="alert">
          {mapsIssue}
        </p>
      )}

      {error && (
        <p className="text-meta text-danger" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

/** The red teardrop, classic Google style, with a draggable-handle affordance. */
function PinVisual() {
  return (
    <span
      data-pin
      tabIndex={0}
      aria-label="Selected venue pin - drag to move"
      className="group relative block h-9 w-6 cursor-grab active:cursor-grabbing"
    >
      {/* pulse so the pin reads as draggable at a glance */}
      <span
        aria-hidden
        className="absolute -bottom-0.5 left-1/2 h-3 w-3 -translate-x-1/2 animate-ping rounded-full bg-danger/50"
      />
      <svg viewBox="0 0 27 41" className="relative block h-full w-full drop-shadow-md" aria-hidden>
        <path
          fill="#EA4335"
          stroke="#B31412"
          strokeWidth="1"
          d="M13.5 1C6.6 1 .5 7 0.5 14.1c0 4.6 2.2 8.5 5.6 11.7L13.5 40l7.4-14.2c3.4-3.2 5.6-7.1 5.6-11.7C26.5 7 20.4 1 13.5 1z"
        />
        <circle cx="13.5" cy="14" r="4.8" fill="#7A1E17" />
      </svg>
    </span>
  );
}

/**
 * The picker's red pin. Draggable in both render modes:
 *  - Advanced marker (map has a Map ID): the SDK owns the drag;
 *  - legacy raster map (no Map ID - AdvancedMarker would render nothing):
 *    the pin floats above the canvas and is dragged with pointer events,
 *    converting screen pixels to lat/lng through the map's projection.
 * `key` remounts it when the selection changes from outside (search pick /
 * map click) so it re-opens at the right coordinates.
 */
function PinMarker({
  position,
  onDrag,
  onDragEnd,
}: {
  position: LatLng;
  onDrag: (position: LatLng) => void;
  onDragEnd: (position: LatLng) => void;
}) {
  const supportsAdvanced = useAdvancedMarkerSupport();
  return supportsAdvanced ? (
    <AdvancedPinMarker position={position} onDrag={onDrag} onDragEnd={onDragEnd} />
  ) : (
    <OverlayPinMarker position={position} onDrag={onDrag} onDragEnd={onDragEnd} />
  );
}

function AdvancedPinMarker({
  position,
  onDrag,
  onDragEnd,
}: {
  position: LatLng;
  onDrag: (position: LatLng) => void;
  onDragEnd: (position: LatLng) => void;
}) {
  const [dragged, setDragged] = useState<LatLng | null>(null);
  const shown = dragged ?? position;

  return (
    <AdvancedMarker
      key={`${position.lat},${position.lng}`}
      position={shown}
      draggable
      onDragStart={(event) => {
        const point = event.latLng;
        if (point) {
          const next = { lat: point.lat(), lng: point.lng() };
          setDragged(next);
          onDrag(next);
        }
      }}
      onDragEnd={(event) => {
        const point = event.latLng;
        const next = point ? { lat: point.lat(), lng: point.lng() } : shown;
        setDragged(null);
        onDragEnd(next);
      }}
      title={"Venue - drag to move"}
    >
      <PinVisual />
    </AdvancedMarker>
  );
}

/** Read-only chips for the picked venue; `Change` clears back to the map. */
export function VenueChips({
  venue,
  onChange,
  error,
}: {
  venue: VenueSelection;
  onChange: (venue: VenueSelection | null) => void;
  error?: string;
}) {
  return (
    <div className="flex flex-col gap-1">
      <dl
        className={cn(
          "flex flex-col gap-1 rounded-md border border-border bg-surface-2 p-3 text-meta",
          error && "border-danger",
        )}
      >
        <div>
          <dt className="sr-only">Venue</dt>
          <dd className="font-medium text-fg">{venue.venueName}</dd>
        </div>
        <div>
          <dt className="sr-only">Address</dt>
          <dd className="text-fg-muted">{venue.address}</dd>
        </div>
      </dl>
      {error && <p className="text-meta text-danger">{error}</p>}
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


/**
 * No-API-key fallback for the search box: the known-Melbourne venues the old
 * picker used, so the typeahead flow stays exercisable offline.
 */
const OFFLINE_VENUES: { name: string; address: string; lat: number; lng: number }[] = [
  { name: "Glen Waverley Badminton Centre", address: "100 Kingsway, Glen Waverley VIC 3150", lat: -37.8708, lng: 145.1615 },
  { name: "Bob Jane Running Track", address: "Acland St, Melbourne VIC 3000", lat: -37.8247, lng: 144.9786 },
  { name: "Lakeside Stadium", address: "Storramadrup Rd, Melbourne VIC 3009", lat: -37.8136, lng: 144.9331 },
  { name: "Melbourne Sports and Aquatic Centre", address: "Olympian Way, West Melbourne VIC 3003", lat: -37.8024, lng: 144.9437 },
  { name: "Coode Island Tennis Centre", address: "Kings Way, Melbourne VIC 3008", lat: -37.8055, lng: 144.9525 },
  { name: "Olympic Park", address: "747 Lewar St, Melbourne VIC 3000", lat: -37.81, lng: 144.9803 },
  { name: "Footscray Sports Centre", address: "1 Hopkins St, Footscray VIC 3011", lat: -37.7996, lng: 144.8993 },
  { name: "Princes Park", address: "261 Princes Park Ave, Carlton VIC 3053", lat: -37.7873, lng: 144.9681 },
];

function offlineSearch(query: string): GeocodeResult[] {
  const needle = query.toLowerCase();
  return OFFLINE_VENUES.filter(
    (v) =>
      v.name.toLowerCase().includes(needle) || v.address.toLowerCase().includes(needle),
  )
    .slice(0, 8)
    .map((v) => ({ address: v.address, name: v.name, lat: v.lat, lng: v.lng }));
}

/**
 * No-Map-ID fallback pin. Advanced markers render nothing on a classic raster
 * map, so this keeps the red teardrop as an overlay above the canvas. It lives
 * inside the SDK's own overlay pane via a minimal OverlayView (created in a
 * ref callback - React 19 ref cleanup runs StrictMode-safe), so panning and
 * zooming move it for free; dragging converts screen pixels back to lat/lng
 * through the map projection.
 */
function OverlayPinMarker({
  position,
  onDrag,
  onDragEnd,
}: {
  position: LatLng;
  onDrag: (position: LatLng) => void;
  onDragEnd: (position: LatLng) => void;
}) {
  const map = useMap();
  const [overlay, setOverlay] = useState<google.maps.OverlayView | null>(null);
  const [dragging, setDragging] = useState(false);
  const positionRef = useRef(position);
  useEffect(() => {
    positionRef.current = position;
  }, [position]);

  // One OverlayView per map: its pane hosts the pin, so camera moves come free.
  const overlayRef = useCallback(
    (node: HTMLDivElement | null) => {
      if (!map) return;
      if (!node) return;
      const view = new google.maps.OverlayView();
      view.onAdd = () => {
        const panes = view.getPanes();
        if (panes?.overlayMouseTarget) panes.overlayMouseTarget.appendChild(node);
      };
      view.draw = () => {
        const projection = view.getProjection();
        if (!projection) return;
        const p = projection.fromLatLngToDivPixel(
          new google.maps.LatLng(positionRef.current.lat, positionRef.current.lng),
        );
        if (p) {
          node.style.left = `${p.x}px`;
          node.style.top = `${p.y}px`;
        }
      };
      view.onRemove = () => node.remove();
      view.setMap(map);
      setOverlay(view);
      return () => {
        view.setMap(null);
        setOverlay(null);
      };
    },
    [map],
  );

  // Redraw whenever the logical position changes outside a drag.
  useEffect(() => {
    if (!overlay) return;
    google.maps.event.trigger(overlay, "draw");
  }, [overlay, position.lat, position.lng]);

  const pointFrom = useCallback(
    (clientX: number, clientY: number): LatLng | null => {
      if (!map) return null;
      const rect = map.getDiv().getBoundingClientRect();
      // The OverlayView's MapCanvasProjection agrees with client pixels once
      // the div rect is subtracted; cast because @types files it under
      // OverlayView only, not the plain map Projection.
      const projection = overlay?.getProjection();
      if (!projection) return null;
      const ll = projection.fromContainerPixelToLatLng(
        new google.maps.Point(clientX - rect.left, clientY - rect.top),
        true,
      );
      return ll ? { lat: ll.lat(), lng: ll.lng() } : null;
    },
    [map, overlay],
  );

  const handlePointerDown = useCallback(
    (event: React.PointerEvent<HTMLDivElement>) => {
      if (!map) return;
      event.preventDefault();
      setDragging(true);
      const move = (e: PointerEvent) => {
        const next = pointFrom(e.clientX, e.clientY);
        if (next) onDrag(next);
      };
      const up = (e: PointerEvent) => {
        window.removeEventListener("pointermove", move);
        window.removeEventListener("pointerup", up);
        setDragging(false);
        onDragEnd(pointFrom(e.clientX, e.clientY) ?? position);
      };
      window.addEventListener("pointermove", move);
      window.addEventListener("pointerup", up);
    },
    [map, pointFrom, onDrag, onDragEnd, position],
  );

  if (!map) return null;
  const style: CSSProperties = {
    position: "absolute",
    transform: "translate(-50%, -100%)",
    cursor: dragging ? "grabbing" : "grab",
  };
  return (
    <div ref={overlayRef} style={style} onPointerDown={handlePointerDown} className="touch-none">
      <PinVisual />
    </div>
  );
}



