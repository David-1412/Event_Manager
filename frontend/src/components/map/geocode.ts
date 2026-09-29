/**
 * Geocoding helpers over `google.maps.Geocoder`.
 *
 * Geocoder rather than Places Autocomplete on purpose: it needs only the Maps
 * JavaScript API enabled (no Places billing) and returns full address results.
 * Geocode calls are billed, so callers debounce aggressively (250 ms in the
 * venue picker) and cap results.
 *
 * Failure policy: a non-OK status (REQUEST_DENIED, OVER_QUERY_LIMIT, ...) is
 * reported to the shared maps-failure channel (map-types.ts) so the venue
 * picker can explain the real reason, and the promise still resolves empty -
 * callers guard staleness with a sequence number.
 */

import { describeGeocoderStatus, reportMapsFailure } from "@/components/map/map-types";

export interface GeocodeResult {
  /** Google's formatted address line, e.g. "Acland St, Melbourne VIC 3000". */
  address: string;
  /** Best short label: street number + route, falling back to the address. */
  name: string;
  lat: number;
  lng: number;
}

/**
 * The Geocoder promise API hides the status inside the rejection value, so
 * normalise whatever shape arrives (GeocoderStatus string, MapsRequestError,
 * or a bare Error) into our shared failure channel. A search that simply has
 * no match is not a failure and reports nothing - the picker's "No place
 * matched" hint owns that case.
 */
function reportFailure(error: unknown) {
  const status =
    typeof error === "string"
      ? error
      : error && typeof error === "object" && "status" in error
        ? String((error as { status?: unknown }).status ?? "")
        : "";
  // The promise API rejects no-match with a MapsRequestError whose message is
  // "GEOCODER_GEOCODE: ZERO_RESULTS: ..." and has no `status` property, so
  // sniff the message too before treating it as a real failure.
  const message = error instanceof Error ? error.message : "";
  if (status === "ZERO_RESULTS" || message.includes("ZERO_RESULTS")) return;
  const reason =
    describeGeocoderStatus(status) ??
    (message
      ? `Venue search failed: ${message}`
      : "Venue search failed - Google Maps refused this request.");
  reportMapsFailure({ reason });
}



/** Short label for the picked venue chip: "2-16 Acland St" style. */
function nameFromResult(result: google.maps.GeocoderResult): string {
  const street = result.address_components.find(
    (c) =>
      c.types.includes("route") ||
      c.types.includes("establishment") ||
      c.types.includes("point_of_interest") ||
      c.types.includes("premise"),
  );
  if (street?.long_name) {
    const house = result.address_components.find((c) => c.types.includes("street_number"));
    return house ? `${house.long_name} ${street.long_name}` : street.long_name;
  }
  return result.formatted_address;
}

function toResult(result: google.maps.GeocoderResult): GeocodeResult {
  return {
    address: result.formatted_address,
    name: nameFromResult(result),
    lat: result.geometry.location.lat(),
    lng: result.geometry.location.lng(),
  };
}

/** Search suggestions for a typed term. Empty on failure. */
export async function geocodeSearch(
  term: string,
  bias?: { lat: number; lng: number },
): Promise<GeocodeResult[]> {
  const query = term.trim();
  if (!query) return [];
  const geocoder = new google.maps.Geocoder();
  try {
    // "One, and only one, of address, location and placeId must be supplied"
    // for the JS-API Geocoder, so a bias `location` alongside `address` is a
    // rejected request (`locationType` isn't even a property). The map centre
    // therefore only biases ranking through a soft `bounds` window when the
    // caller passes one - and Google widens automatically when a bounded
    // search would return nothing.
    const response = await geocoder.geocode({
      address: query,
      ...(bias
        ? {
            bounds: {
              north: bias.lat + 0.12,
              south: bias.lat - 0.12,
              east: bias.lng + 0.12,
              west: bias.lng - 0.12,
            },
          }
        : {}),
    });
    return (response.results ?? []).slice(0, 8).map(toResult);
  } catch (error) {
    reportFailure(error);
    return [];
  }
}

/** Reverse geocode a map click/drag into name + address. Null on failure. */
export async function reverseGeocode(position: {
  lat: number;
  lng: number;
}): Promise<GeocodeResult | null> {
  const geocoder = new google.maps.Geocoder();
  try {
    const response = await geocoder.geocode({ location: position });
    const first = response.results?.[0];
    return first ? toResult(first) : null;
  } catch (error) {
    reportFailure(error);
    return null;
  }
}
