"use client";

import { useEffect, useState } from "react";
import { MELBOURNE_CBD } from "@/lib/sports";

export type GeoStatus = "idle" | "prompting" | "granted" | "denied" | "unsupported";

export interface GeoState {
  status: GeoStatus;
  coords: { lat: number; lng: number };
}

/**
 * Geolocation is asked for lazily and never blocks content: `/` renders on
 * Melbourne CBD (spec §8) and silently upgrades once permission resolves.
 * Denied leaves the map on CBD and surfaces a one-line hint - a permission
 * prompt before the user has seen anything reads as a wall.
 *
 * Only the two terminal states are set from the callbacks; the initial value
 * covers "unsupported" too, so nothing is written to state during mount.
 */
export function useGeolocation(): GeoState {
  const [settled, setSettled] = useState<GeoState | null>(null);

  useEffect(() => {
    if (!("geolocation" in navigator)) return;
    let cancelled = false;
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        if (cancelled) return;
        setSettled({
          status: "granted",
          coords: { lat: pos.coords.latitude, lng: pos.coords.longitude },
        });
      },
      () => {
        if (cancelled) return;
        setSettled({
          status: "denied",
          coords: { lat: MELBOURNE_CBD.latitude, lng: MELBOURNE_CBD.longitude },
        });
      },
      { enableHighAccuracy: false, timeout: 8000, maximumAge: 600_000 },
    );
    return () => {
      cancelled = true;
    };
  }, []);

  if (settled) return settled;
  const supported = typeof navigator !== "undefined" && "geolocation" in navigator;
  return {
    status: supported ? "prompting" : "unsupported",
    coords: { lat: MELBOURNE_CBD.latitude, lng: MELBOURNE_CBD.longitude },
  };
}
