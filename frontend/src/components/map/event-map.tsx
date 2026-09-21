"use client";

import { useMemo, useRef, useState } from "react";
import {
  AdvancedMarker,
  APIProvider,
  InfoWindow,
  Map as GoogleMap,
  type MapCameraChangedEvent,
} from "@vis.gl/react-google-maps";
import { MapFallback, MapInfoCard } from "@/components/map/map-fallback";
import { MAPS_API_KEY, MAPS_MAP_ID, type MapProps } from "@/components/map/map-types";
import { MapSkeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/cn";
import { iconFor, MELBOURNE_CBD } from "@/lib/sports";

/**
 * Maps is an enhancement, never a dependency: the list beside it is the
 * accessible equivalent (spec §7 - "the list is the accessible alternative,
 * never the reverse"). Baked in here:
 *  - the script is only requested when a key exists; otherwise a same-height
 *    placeholder holds the layout (no CLS when keys are added later);
 *  - `role="region"` + `aria-label="Map of events"` so it can be skipped;
 *  - `Search this area` only after the centre moves > 0.5 km;
 *  - the selected pin scales 1.15 - translation/scale on a pin never reflows
 *    the page because the map owns its own layer.
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
  const [loaded, setLoaded] = useState(false);
  const [failed, setFailed] = useState(false);
  const [pendingCentre, setPendingCentre] = useState<{ lat: number; lng: number } | null>(null);
  const anchorRef = useRef<{ lat: number; lng: number } | null>(null);

  const centre = useMemo(() => {
    const selected = events.find((e) => e.id === selectedEventId);
    if (selected) return { lat: selected.latitude, lng: selected.longitude };
    const first = events[0];
    if (first) return { lat: first.latitude, lng: first.longitude };
    return { lat: MELBOURNE_CBD.latitude, lng: MELBOURNE_CBD.longitude };
  }, [events, selectedEventId]);

  if (!MAPS_API_KEY) {
    return (
      <MapFallback
        className={cn(variant === "detail" ? "h-[200px]" : "h-full min-h-48", className)}
        height={variant === "detail" ? 200 : undefined}
      />
    );
  }

  return (
    <div
      className={cn(
        "relative w-full overflow-hidden bg-surface-2",
        variant === "detail" ? "h-[200px]" : "h-full min-h-48",
        className,
      )}
      role="region"
      aria-label="Map of events"
    >
      {!loaded && !failed && <MapSkeleton />}
      {failed && <MapFallback className="absolute inset-0" height={undefined} />}

      <APIProvider
        apiKey={MAPS_API_KEY}
        onLoad={() => setLoaded(true)}
        onError={() => setFailed(true)}
      >
        <GoogleMap
          mapId={MAPS_MAP_ID || undefined}
          defaultCenter={centre}
          defaultZoom={variant === "detail" ? 15 : 12}
          center={centre}
          isFractionalZoomEnabled
          gestureHandling="greedy"
          disableDefaultUI
          zoomControl
          streetViewControl={false}
          fullscreenControl={false}
          clickableIcons={false}
          onClick={() => onSelect(null)}
          onCameraChanged={(event: MapCameraChangedEvent) => {
            if (!onSearchThisArea || variant !== "browse") return;
            const next = { lat: event.detail.center.lat, lng: event.detail.center.lng };
            const anchor = anchorRef.current ?? centre;
            anchorRef.current = anchor;
            if (distanceKm(anchor.lat, anchor.lng, next.lat, next.lng) > SEARCH_THIS_AREA_THRESHOLD_KM) {
              setPendingCentre(next);
            }
          }}
        >
          {events.map((event) => (
            <AdvancedMarker
              key={event.id}
              position={{ lat: event.latitude, lng: event.longitude }}
              title={`${event.title} ${event.participantCount}/${event.maxParticipants}`}
              onClick={() => onSelect(event.id)}
            >
              <Pin
                icon={iconFor(event.sport, event.sportIcon)}
                count={`${event.participantCount}/${event.maxParticipants}`}
                selected={event.id === selectedEventId}
                full={event.participantCount >= event.maxParticipants}
              />
            </AdvancedMarker>
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
        </GoogleMap>
      </APIProvider>

      {variant === "browse" && pendingCentre && (
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
      )}
    </div>
  );
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

