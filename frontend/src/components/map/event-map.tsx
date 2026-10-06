"use client";

import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { AdvancedMarker, InfoWindow, useMap } from "@vis.gl/react-google-maps";
import { MapInfoCard } from "@/components/map/map-fallback";
import {
  DEFAULT_CENTER,
  MapSurface,
  useAdvancedMarkerSupport,
} from "@/components/map/map-surface";
import type { MapProps } from "@/components/map/map-types";
import { cn } from "@/lib/cn";

/** Sport is optional decoration now, but a map pin must show *something*; a
 * calendar reads as "an event" without implying a sport the host never chose. */
const PIN_FALLBACK_ICON = "\u{1F4C5}";


/**
 * Maps is an enhancement, never a dependency: the list beside it is the
 * accessible equivalent (spec §7 - "the list is the accessible alternative,
 * never the reverse"). The provider, key-absent fallback, skeleton and failure
 * placeholder live in MapSurface; this component is only markers + selection.
 *  - `role="region"` + `aria-label="Map of events"` so it can be skipped;
 *  - `Search this area` only after the centre moves > 0.5 km;
 *  - the selected pin scales 1.15 - translation/scale on a pin never reflows
 *    the map, so it stays cheap under INP.
 */
const SEARCH_THIS_AREA_THRESHOLD_KM = 0.5;

export function EventMap({
  events,
  selectedEventId,
  onSelect,
  onSearchThisArea,
  variant = "browse",
  className,
}: MapProps) {
  const [pendingCentre, setPendingCentre] = useState<{ lat: number; lng: number } | null>(null);
  const anchorRef = useRef<{ lat: number; lng: number } | null>(null);

  const centre = useMemo(() => {
    const selected = events.find((e) => e.id === selectedEventId);
    if (selected) return { lat: selected.latitude, lng: selected.longitude };
    const first = events[0];
    if (first) return { lat: first.latitude, lng: first.longitude };
    return DEFAULT_CENTER;
  }, [events, selectedEventId]);

  return (
    <MapSurface
      center={centre}
      zoom={variant === "detail" ? 15 : 12}
      className={className}
      heightClass={variant === "detail" ? "h-[200px]" : "h-full min-h-48"}
      ariaLabel={variant === "detail" ? "Map of venue" : "Map of events"}
      flyToCenterOnChange
      onMapClick={() => onSelect(null)}
      onCameraChanged={(next) => {
        if (!onSearchThisArea || variant !== "browse") return;
        const anchor = anchorRef.current ?? centre;
        anchorRef.current = anchor;
        if (distanceKm(anchor.lat, anchor.lng, next.lat, next.lng) > SEARCH_THIS_AREA_THRESHOLD_KM) {
          setPendingCentre(next);
        }
      }}
      overlay={
        variant === "browse" && pendingCentre ? (
          <button
            type="button"
            onClick={() => {
              onSearchThisArea?.(pendingCentre);
              anchorRef.current = pendingCentre;
              setPendingCentre(null);
            }}
            className="press absolute bottom-4 left-1/2 z-10 h-9 -translate-x-1/2 rounded-full border border-border bg-surface px-4 text-meta font-medium text-fg shadow-float"
          >
            Search this area
          </button>
        ) : null
      }
    >
      {events.map((event) => (
        <EventPinMarker
          key={event.id}
          event={event}
          selected={event.id === selectedEventId}
          onSelect={onSelect}
        />
      ))}

      {variant === "browse" &&
        events
          .filter((e) => e.id === selectedEventId)
          .map((event) => (
            <InfoWindow
              key={event.id}
              position={{ lat: event.latitude, lng: event.longitude }}
              onCloseClick={() => onSelect(null)}
              ariaLabel={`Details for ${event.title}`}
            >
              <MapInfoCard event={event} />
            </InfoWindow>
          ))}
    </MapSurface>
  );
}

/**
 * One event's pin. Advanced markers carry the HTML `Pin`; a map created without
 * a Map ID cannot render them at all, so it takes the OverlayView route instead.
 */
function EventPinMarker({
  event,
  selected,
  onSelect,
}: {
  event: MapProps["events"][number];
  selected: boolean;
  onSelect: (id: string) => void;
}) {
  const supportsAdvanced = useAdvancedMarkerSupport();
  const visual = (
    <Pin
      icon={event.sportIcon ?? PIN_FALLBACK_ICON}
      count={`${event.joinedCount}/${event.maxParticipants}`}
      selected={selected}
      full={event.joinedCount >= event.maxParticipants}
    />
  );

  if (supportsAdvanced) {
    return (
      <AdvancedMarker
        position={{ lat: event.latitude, lng: event.longitude }}
        title={`${event.title} ${event.joinedCount}/${event.maxParticipants}`}
        onClick={() => onSelect(event.id)}
      >
        {visual}
      </AdvancedMarker>
    );
  }
  return (
    <OverlayPin
      position={{ lat: event.latitude, lng: event.longitude }}
      title={`${event.title} ${event.joinedCount}/${event.maxParticipants}`}
      icon={event.sportIcon ?? PIN_FALLBACK_ICON}
      count={`${event.joinedCount}/${event.maxParticipants}`}
      selected={selected}
      full={event.joinedCount >= event.maxParticipants}
      onClick={() => onSelect(event.id)}
    />
  );
}

/**
 * Minimal OverlayView-hosted HTML pin, for maps created without a Map ID where
 * Advanced markers render nothing at all. It lives in the SDK's overlayMouseTarget
 * pane, so camera moves reposition it for free and clicks arrive normally.
 *
 * The host is a node this component creates, not one React rendered. The overlay
 * has to re-parent it into that pane, and a node React believes it owns cannot be
 * moved: when a React-rendered host was used, filtering the list down unmounted
 * pins whose node had already left the tree, and the commit threw
 * `NotFoundError: ... removeChild`, blanking the page. The markup below mirrors
 * `Pin`, whose Tailwind classes Tailwind still emits because `Pin` itself is
 * rendered on the Advanced-marker path; colours go through the same custom
 * properties, so light/dark theming still applies.
 */
function OverlayPin({
  position,
  onClick,
  title,
  icon,
  count,
  selected,
  full,
}: {
  position: { lat: number; lng: number };
  onClick: () => void;
  title: string;
  icon: string;
  count: string;
  selected: boolean;
  full: boolean;
}) {
  const map = useMap();
  const positionRef = useRef(position);
  useEffect(() => {
    positionRef.current = position;
  }, [position]);
  const clickRef = useRef(onClick);
  useEffect(() => {
    clickRef.current = onClick;
  }, [onClick]);

  const overlayRef = useCallback(
    (anchor: HTMLSpanElement | null) => {
      if (!map || !anchor) return;

      // Raw node, appended nowhere until the overlay pane exists, so React never
      // claims it (see the note above).
      const node = document.createElement("div");
      node.style.cssText = pinShellCss(selected);
      node.append(pinGlyph(icon), pinText(count, selected, full));
      node.title = title;
      node.addEventListener("click", (event) => {
        event.stopPropagation();
        clickRef.current();
      });

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
      return () => view.setMap(null);
    },
    // Rebuilt whenever the pin's appearance changes; selection and participant
    // counts move a handful of nodes, which is not hot-path work.
    [map, title, icon, count, selected, full],
  );

  if (!map) return null;
  // Zero-size anchor: its only purpose is to give React a ref callback to build
  // the overlay from, exactly once per mount.
  return <span ref={overlayRef} style={{ position: "absolute", width: 0, height: 0 }} />;
}

/**
 * `Pin`'s computed style (`flex items-center gap-1 rounded-full border bg-surface
 * px-2 py-1 text-meta font-medium shadow-raise`, plus the selected/full variants),
 * written against the same design tokens so light/dark theming still applies.
 */
function pinShellCss(selected: boolean): string {
  const brand = "var(--color-brand-600, oklch(0.55 0.17 155))";
  return [
    "position:absolute",
    "display:flex",
    "align-items:center",
    "gap:0.25rem",
    "border-radius:calc(infinity * 1px)",
    "border-width:1px",
    "border-style:solid",
    `border-color:${selected ? brand : "var(--color-border, oklch(0.9 0.006 250))"}`,
    "background-color:var(--color-surface, #fff)",
    `color:${selected ? brand : "var(--color-fg, oklch(0.24 0.02 255))"}`,
    "padding:0.25rem 0.5rem",
    "font-size:0.8125rem",
    "line-height:1.125rem",
    "font-weight:500",
    selected
      ? "box-shadow:0 8px 24px oklch(0% 0 0 / 0.12)"
      : "box-shadow:0 1px 2px oklch(0% 0 0 / 0.06)",
    selected ? "transform:scale(1.15)" : "",
    "translate:-50% -100%",
    "white-space:nowrap",
    "cursor:pointer",
  ]
    .filter(Boolean)
    .join(";");
}

function pinGlyph(icon: string): HTMLElement {
  const glyph = document.createElement("span");
  glyph.setAttribute("aria-hidden", "true");
  glyph.textContent = icon;
  return glyph;
}

function pinText(count: string, selected: boolean, full: boolean): HTMLElement {
  const text = document.createElement("span");
  text.dataset.count = "";
  text.textContent = count;
  // `full && !selected && text-danger` from `Pin`.
  if (full && !selected) text.style.color = "var(--color-danger, oklch(0.58 0.2 25))";
  return text;
}

/** Sport emoji + live count. Selected pin scales 1.15 with a lifted shadow. */
function Pin({
  icon,
  count,
  selected,
  full,
}: {
  icon: string;
  count: string;
  selected: boolean;
  full: boolean;
}) {
  return (
    <span
      className={cn(
        "flex items-center gap-1 rounded-full border bg-surface px-2 py-1 text-meta font-medium shadow-raise",
        selected ? "border-brand-600 text-brand-600 shadow-float" : "border-border text-fg",
        full && !selected && "text-danger",
      )}
      style={selected ? { transform: "scale(1.15)" } : undefined}
    >
      <span aria-hidden>{icon}</span>
      <span data-count>{count}</span>
    </span>
  );
}

function distanceKm(aLat: number, aLng: number, bLat: number, bLng: number): number {
  const toRad = (v: number) => (v * Math.PI) / 180;
  const dLat = toRad(bLat - aLat);
  const dLng = toRad(bLng - aLng);
  const h =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(aLat)) * Math.cos(toRad(bLat)) * Math.sin(dLng / 2) ** 2;
  return 12_742 * Math.asin(Math.sqrt(h));
}

