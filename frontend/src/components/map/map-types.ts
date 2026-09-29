import type { EventListItem } from "@/types/events";

export interface MapProps {
  events: EventListItem[];
  selectedEventId: string | null;
  onSelect: (id: string | null) => void;
  /** Fired when the centre moves > 0.5 km so the parent can re-query. */
  onSearchThisArea?: (center: { lat: number; lng: number }) => void;
  /** `detail` = single marker, 200px, no `Search this area` pill (spec §8). */
  variant?: "browse" | "detail";
  className?: string;
}

/** Read at render time so a missing key degrades to a labelled placeholder. */
export const MAPS_API_KEY = process.env.NEXT_PUBLIC_GOOGLE_MAPS_API_KEY ?? "";
export const MAPS_MAP_ID = process.env.NEXT_PUBLIC_GOOGLE_MAPS_MAP_ID ?? "";

export type LatLng = { lat: number; lng: number };

/**
 * Shared "Google Maps is not working" channel. The shield in MapSurface owns
 * the real detection (gm_error / gm_authFailure); geocode helpers report here
 * too so a refused Geocoding call explains itself in the venue picker instead
 * of surfacing as "no place matched".
 */
export type MapsFailure = { reason: string };

const mapsFailureListeners = new Set<(failure: MapsFailure) => void>();
let lastMapsFailure: MapsFailure | null = null;

export function reportMapsFailure(failure: MapsFailure) {
  lastMapsFailure = failure;
  mapsFailureListeners.forEach((listener) => listener(failure));
}

export function subscribeMapsFailure(listener: (failure: MapsFailure) => void): () => void {
  mapsFailureListeners.add(listener);
  return () => mapsFailureListeners.delete(listener);
}

export function lastMapsFailureReason(): string | null {
  return lastMapsFailure?.reason ?? null;
}

/** Human sentence for a Geocoder status; null for statuses that are fine. */
export function describeGeocoderStatus(status: string): string | null {
  switch (status) {
    case "ZERO_RESULTS":
    case "OK":
      // Not a failure at all - the term simply has no match (or the call
      // worked). The picker shows its own "No place matched" hint for this,
      // so it must never surface as an error banner.
      return null;
    case "REQUEST_DENIED":
    case "INVALID_REQUEST":
      return "Google refused the venue search for this API key - check the Geocoding API, its referrer restrictions and billing in Google Cloud Console.";
    case "OVER_QUERY_LIMIT":
      return "Google's free geocoding quota for this key is used up - venue search is paused; tap the map or drag the pin instead.";
    case "UNKNOWN_ERROR":
      return "Venue search failed due to a network problem - check the connection and try again.";
    default:
      return null;
  }
}

