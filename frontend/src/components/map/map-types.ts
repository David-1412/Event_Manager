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

