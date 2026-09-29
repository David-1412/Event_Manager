"use client";

import {
  APIProvider,
  APILoadingStatus,
  Map as GoogleMap,
  useApiIsLoaded,
  useApiLoadingStatus,
  useMap,
  type MapCameraChangedEvent,
} from "@vis.gl/react-google-maps";
import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";
import { toast } from "@/components/ui/toast";
import { MapFallback } from "@/components/map/map-fallback";
import {
  MAPS_API_KEY,
  MAPS_MAP_ID,
  reportMapsFailure,
  type LatLng,
} from "@/components/map/map-types";
import { MapSkeleton } from "@/components/ui/skeleton";
import { MELBOURNE_CBD } from "@/lib/sports";
import { cn } from "@/lib/cn";

/**
 * The shared Google Maps surface every map in the app renders through (browse,
 * detail, venue picker). MapSurface owns the APIProvider, the key-absent
 * fallback, the loading skeleton and the failed-load placeholder; children
 * (markers, InfoWindows) only mount once `google.maps` exists.
 */
export interface MapSurfaceProps {
  center: LatLng;
  zoom: number;
  className?: string;
  /** `detail` renders 200px tall (spec §8); browse fills its column. */
  heightClass?: string;
  ariaLabel: string;
  onMapClick?: (position: LatLng) => void;
  /** browse "Search this area" pill; fired whenever the camera centre moves. */
  onCameraChanged?: (center: LatLng) => void;
  /**
   * Make the camera fully user-controlled: pan/zoom freely, and the map only
   * flies back when the *value* of `center` changes — not on every re-render.
   * Without this, the library's controlled `center` prop snaps a dragged map
   * back to the prop on the next render (SWR revalidation, selection, …).
   */
  flyToCenterOnChange?: boolean;
  /**
   * Keep the interactive map even when Google reports an auth failure
   * (venue picker: the pin stays draggable so the spot can still be picked).
   * The explanatory strip renders on top instead of replacing the map.
   */
  keepMapOnFailure?: boolean;
  children?: React.ReactNode;
  /** Pill overlays; rendered above the map. */
  overlay?: React.ReactNode;
}

/** Melbourne CBD — the opening centre whenever there is nothing better. */
export const DEFAULT_CENTER: LatLng = {
  lat: MELBOURNE_CBD.latitude,
  lng: MELBOURNE_CBD.longitude,
};

/**
 * Advanced (HTML-content) markers only render on Maps >= 3.57 when the map
 * was created with a valid Map ID - without one, AdvancedMarkerElement
 * creates zero DOM and logs "The map is initialized without a valid Map ID".
 * MapSurface detects the condition once per map instance and shares it via
 * this context, so markers can fall back to the legacy raster `Marker` and
 * stay visible with or without a Map ID.
 */
const AdvancedMarkerSupportContext = createContext(true);
export function useAdvancedMarkerSupport(): boolean {
  return useContext(AdvancedMarkerSupportContext);
}

/** True when the live map accepts vector-styled Advanced Markers. */
function mapSupportsAdvancedMarkers(map: google.maps.Map | null): boolean {
  if (!map) return false;
  try {
    const id = (map as { getMapId?: () => string | undefined }).getMapId?.();
    return typeof id === "string" && id.length > 0;
  } catch {
    return false;
  }
}

/** Google's constant for "classic raster map, no Map ID" (client-only, lazy). */
function defaultWithoutId(): string | undefined {
  if (typeof google === "undefined") return undefined;
  return (
    google.maps as unknown as { MapIdRenderer?: { DEFAULT_WITHOUT_ID?: string } }
  ).MapIdRenderer?.DEFAULT_WITHOUT_ID;
}

export function MapSurface({
  center,
  zoom,
  className,
  heightClass = "h-full min-h-48",
  ariaLabel,
  onMapClick,
  onCameraChanged,
  flyToCenterOnChange,
  keepMapOnFailure,
  children,
  overlay,
}: MapSurfaceProps) {
  if (!MAPS_API_KEY) {
    return (
      <MapFallback
        className={cn(className, heightClass)}
        message={"Map unavailable - add NEXT_PUBLIC_GOOGLE_MAPS_API_KEY to enable the map."}
      />
    );
  }

  return (
    <div
      className={cn("relative w-full overflow-hidden bg-surface-2", heightClass, className)}
      role="region"
      aria-label={ariaLabel}
    >
      <APIProvider apiKey={MAPS_API_KEY}>
        <MapSurfaceBody
          center={center}
          zoom={zoom}
          onMapClick={onMapClick}
          onCameraChanged={onCameraChanged}
          flyToCenterOnChange={flyToCenterOnChange}
          keepMapOnFailure={keepMapOnFailure}
          overlay={overlay}
        >
          {children}
        </MapSurfaceBody>
      </APIProvider>
    </div>
  );
}

/**
 * Translate the raw `gm_error` detail the Maps SDK fires into a sentence that
 * names the fix. `notAuthorized` covers both "billing not enabled" and "HTTP
 * referrer blocked" - the two reasons the dialog appears with a working key.
 */
function describeGmError(detail?: {
  event?: string;
  referer?: string;
  error_message?: string;
  status?: number;
}): string {
  const reason = detail?.error_message?.toLowerCase() ?? "";
  if (reason.includes("billing") || reason.includes("enabled in the console")) {
    return "Google Maps is disabled or has no billing account - enable Maps JavaScript API (plus Geocoding) and attach billing in Google Cloud Console.";
  }
  if (reason.includes("referrer") || reason.includes("referer") || detail?.event === "invisible-referer") {
    return "This Google Maps API key does not allow localhost - in Google Cloud Console set its application restriction to HTTP referrers and add http://localhost:3000/*.";
  }
  if (detail?.event === "auth_failure" || reason.includes("api key")) {
    return "Google Maps rejected the API key - double-check NEXT_PUBLIC_GOOGLE_MAPS_API_KEY in .env.local.";
  }
  return `Google Maps refused to load${detail?.error_message ? `: ${detail.error_message}` : " - check the key's restrictions, enabled APIs and billing."}`;
}

/**
 * Kill Google's "This page can't load Google Maps correctly / Do you own this
 * website?" alert. The SDK paints it as an in-map `role=alertdialog` overlay
 * (class `*-degraded-map-dialog-view`) whenever a usage call is refused, and
 * exposes the failure through the window-level `gm_authFailure` hook - which
 * @vis.gl/react-google-maps does not wire up. We intercept the hook, rip the
 * dialog out as soon as it appears (childList subtree observer + capture-phase
 * click blocker so it can never flash interactively), and hand the reason to
 * `onFail` so MapSurface can explain what to fix instead.
 */
function installGmErrorShield(onFail: (detail?: { event?: string; error_message?: string }) => void): () => void {
  const window_ = window as unknown as { gm_authFailure?: () => void };
  const previous = window_.gm_authFailure;
  let disposed = false;

  const fail = (detail?: { event?: string; error_message?: string }) => {
    if (!disposed) onFail(detail);
    killDialogs();
  };

  window_.gm_authFailure = () => {
    previous?.();
    fail({ event: "auth_failure", error_message: "Google rejected this API key for this page." });
  };

  const onGmError = (event: Event) => {
    fail((event as CustomEvent).detail);
  };
  window.addEventListener("gm_error", onGmError);

  const isDialog = (node: Node | null): node is HTMLElement =>
    node instanceof HTMLElement &&
    (node.getAttribute("role") === "alertdialog" || node.className.toString().includes("degraded-map-dialog"));

  const killDialogs = () => {
    document
      .querySelectorAll<HTMLElement>('[role="alertdialog"], [class*="degraded-map-dialog"]')
      .forEach((el) => {
        // Remove the whole overlay wrapper when the dialog is centred in one,
        // so the blurred backdrop disappears too.
        const overlay = el.parentElement?.className.toString().includes("modal-overlay")
          ? el.parentElement
          : el;
        overlay.remove();
      });
  };

  // Blocker: any click landing on a dialog (e.g. fired in the same tick the
  // dialog appears) is swallowed instead of reaching Google's alert.
  const block = (event: MouseEvent) => {
    const target = event.target as HTMLElement | null;
    if (target?.closest?.('[role="alertdialog"], [class*="degraded-map-dialog"]')) {
      event.stopPropagation();
      event.preventDefault();
      killDialogs();
    }
  };
  document.addEventListener("click", block, { capture: true });

  const observer = new MutationObserver((records) => {
    if (records.some((r) => Array.from(r.addedNodes).some(isDialog))) killDialogs();
  });
  observer.observe(document.body, { childList: true, subtree: true });
  killDialogs();

  return () => {
    disposed = true;
    window_.gm_authFailure = previous;
    window.removeEventListener("gm_error", onGmError);
    document.removeEventListener("click", block, { capture: true });
    observer.disconnect();
  };
}

function MapSurfaceBody({
  center,
  zoom,
  onMapClick,
  onCameraChanged,
  flyToCenterOnChange,
  keepMapOnFailure,
  overlay,
  children,
}: Omit<MapSurfaceProps, "ariaLabel" | "heightClass" | "className">) {
  const status = useApiLoadingStatus();
  const apiLoaded = useApiIsLoaded();
  const [loadError, setLoadError] = useState<string | null>(null);
  const failed =
    status === APILoadingStatus.FAILED || status === APILoadingStatus.AUTH_FAILURE;
  // Google occasionally rejects the *initial* map session (quota/referrer
  // blip) while later calls work. One automatic retry with a fresh key prop
  // makes the APIProvider reload the SDK instead of showing the fallback.
  const [retryNonce, setRetryNonce] = useState(0);
  const autoRetried = useRef(false);

  // Shield stays mounted for the whole life of the surface: auth failures can
  // arrive at any time (initial load *and* later geocode calls) and the dialog
  // would otherwise cover the map mid-session.
  useEffect(() => {
    let lastToast = 0;
    return installGmErrorShield((detail) => {
      const reason = describeGmError(detail);
      setLoadError(reason);
      reportMapsFailure({ reason });
      if (!autoRetried.current && retryNonce === 0) {
        autoRetried.current = true;
        setRetryNonce(1);
        return;
      }
      // One toast per minute at most - the SDK repeats gm_error per call.
      if (Date.now() - lastToast > 60_000) {
        lastToast = Date.now();
        toast(reason, "info");
      }
    });
  }, [retryNonce]);

  const showMessage = failed || Boolean(loadError);
  const showMap = apiLoaded && (!showMessage || keepMapOnFailure);
  const rootRef = useRef<HTMLDivElement>(null);
  const lastFocusable = useRef<Element | null>(null);

  // When Google refuses to serve (tiles never arrive on an auth failure), the
  // empty map canvas is what sits under the cursor - point it at the pin so
  // the venue spot stays discoverable, keyboard-included.
  useEffect(() => {
    const root = rootRef.current;
    if (!root) return;
    const canvas = root.querySelector<HTMLElement>("[data-testid='map'] [role='region'][aria-label='Map']");
    if (!canvas) return;
    if (showMessage && keepMapOnFailure) {
      lastFocusable.current = document.activeElement;
      canvas.setAttribute("tabindex", "-1");
      canvas.setAttribute("aria-hidden", "true");
      canvas.style.pointerEvents = "none";
      root.querySelector<HTMLElement>("[data-pin]")?.focus({ preventScroll: true });
    } else {
      canvas.removeAttribute("aria-hidden");
      canvas.style.pointerEvents = "";
    }
    return () => {
      canvas.removeAttribute("aria-hidden");
      canvas.style.pointerEvents = "";
    };
  }, [showMessage, keepMapOnFailure]);

  // While the fallback covers the map, keyboard focus must not wander into the
  // dead canvas underneath.
  useEffect(() => {
    const root = rootRef.current;
    if (!showMessage || !root) return;
    const trap = (event: KeyboardEvent) => {
      if (event.key !== "Tab") return;
      const fallback = root.querySelector<HTMLElement>("[data-map-fallback]");
      const active = document.activeElement;
      if (fallback && !fallback.contains(active)) fallback.focus();
    };
    root.addEventListener("keydown", trap);
    return () => {
      root.removeEventListener("keydown", trap);
      if (lastFocusable.current instanceof HTMLElement) lastFocusable.current.focus();
    };
  }, [showMessage]);

  // `flyToCenterOnChange`: keep the library's controlled-`center` behaviour but
  // freeze the handed-down value to the last *value-change* of `center`. The
  // library's camera sync runs on every render and compares against the map's
  // live centre, so a plain controlled prop snaps a dragged map back the moment
  // anything else re-renders (SWR revalidation, selection, toast…). With the
  // value frozen, a drag is final until the app genuinely asks for a new
  // centre (a different selected venue), which then flies once.
  const centerKey = `${center.lat},${center.lng}`;
  const [flyTarget, setFlyTarget] = useState<LatLng>(center);
  const flyKeyRef = useRef(centerKey);
  useEffect(() => {
    if (flyKeyRef.current !== centerKey) {
      flyKeyRef.current = centerKey;
      setFlyTarget(center);
    }
  }, [center, centerKey]);

  return (
    <div ref={rootRef} className="contents">
      {!apiLoaded && !failed && <MapSkeleton />}
      {showMessage &&
        (keepMapOnFailure ? (
          <div
            data-map-fallback
            tabIndex={-1}
            role="alert"
            className="pointer-events-none absolute inset-x-0 top-0 z-10 bg-surface-2/95 px-3 py-2 text-meta text-fg-muted backdrop-blur-sm outline-none"
          >
            {loadError ??
              "Map tiles failed to load - check the Maps JavaScript API key, its referrer restrictions and billing."}
          </div>
        ) : (
          <MapFallback
            className="absolute inset-0"
            message={
              loadError ??
              "Map failed to load - check the Maps JavaScript API key, its referrer restrictions and billing."
            }
          />
        ))}
      {showMap && (
        <GoogleMap
          mapId={MAPS_MAP_ID || defaultWithoutId()}
          defaultCenter={center}
          defaultZoom={zoom}
          center={flyToCenterOnChange ? flyTarget : undefined}
          isFractionalZoomEnabled
          gestureHandling="greedy"
          disableDefaultUI
          zoomControl
          streetViewControl={false}
          fullscreenControl={false}
          clickableIcons={false}
          onClick={(event) => {
            if (!onMapClick) return;
            const point = event.detail.latLng;
            if (point) onMapClick({ lat: point.lat, lng: point.lng });
          }}
          onCameraChanged={(event: MapCameraChangedEvent) => {
            if (!onCameraChanged) return;
            const next = event.detail.center;
            onCameraChanged({ lat: next.lat, lng: next.lng });
          }}
        >
          <AdvancedMarkerCapability>{children}</AdvancedMarkerCapability>
        </GoogleMap>
      )}
      {overlay}
    </div>
  );
}

/**
 * Publishes whether the mounted map accepts Advanced (HTML-content) markers,
 * so descendant markers can pick AdvancedMarker vs the legacy raster Marker.
 * Must be rendered inside <GoogleMap> (it reads the map from context).
 */
function AdvancedMarkerCapability({ children }: { children: React.ReactNode }) {
  const map = useMap();
  const supports = mapSupportsAdvancedMarkers(map);
  return (
    <AdvancedMarkerSupportContext.Provider value={supports}>
      {children}
    </AdvancedMarkerSupportContext.Provider>
  );
}
