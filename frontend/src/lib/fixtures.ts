import type { DraftQueue, EventDetail, EventDraft, EventListItem, Paged, PopularTag, SkillLevel, Tag } from "@/types/events";
import { MELBOURNE_CBD } from "@/lib/sports";

/**
 * Offline/dev fixtures so `/`, `/events/[id]`, the style guide, Vitest and
 * Playwright all run before the .NET API does — and so every card variant in
 * spec §7 is reachable without staging data.
 *
 * Dates are generated relative to "now" so the relative-time formatters always
 * have something to chew on (`Tonight 6–8PM`, `Tomorrow 7AM`, `Sat 11AM`).
 */

function at(daysFromNow: number, hour: number, minute = 0): string {
  const d = new Date();
  d.setDate(d.getDate() + daysFromNow);
  d.setHours(hour, minute, 0, 0);
  return d.toISOString();
}

type Seed = {
  id: string;
  title: string;
  tags: Tag[];
  icon: string;
  skill: SkillLevel;
  start: string;
  end: string;
  venue: string;
  address: string;
  lat: number;
  lng: number;
  cost: number | null;
  max: number;
  count: number;
  cancelled?: boolean;
  description?: string;
};

const seeds: Seed[] = [
  {
    id: "evt-badminton-monday",
    title: "Badminton Monday",
    tags: ["badminton"],
    icon: "🏸",
    skill: "Intermediate",
    start: at(0, 18),
    end: at(0, 20),
    venue: "Glen Waverley Badminton Centre",
    address: "100 Kingsway, Glen Waverley VIC 3150",
    lat: -37.8708,
    lng: 145.1615,
    cost: 15,
    max: 4,
    count: 2,
    description:
      "Two courts booked 6–8pm. Bring your own racket, shuttlecocks provided. Doubles format, rotate partners every game.",
  },
  {
    id: "evt-thursday-run",
    title: "Thursday Run at the Track",
    tags: ["running"],
    icon: "🏃",
    skill: "Beginner",
    start: at(1, 7),
    end: at(1, 8),
    venue: "Bob Jane Running Track",
    address: "Acland St, Melbourne VIC 3000",
    lat: -37.8247,
    lng: 144.9786,
    cost: null,
    max: 8,
    count: 7,
    description: "Four easy k's at a pace where nobody gets dropped. Water fountains on the north side.",
  },
  {
    id: "evt-social-soccer",
    title: "Social Soccer",
    tags: ["soccer"],
    icon: "⚽",
    skill: "Intermediate",
    start: at(3, 16),
    end: at(3, 17),
    venue: "Lakeside Stadium",
    address: "Storramadrup Rd, Melbourne VIC 3009",
    lat: -37.8136,
    lng: 144.9331,
    cost: 10,
    max: 8,
    count: 8,
    description: "Full five-a-side. Moulded boots and shin pads are on you.",
  },
  {
    id: "evt-basketball-thursday",
    title: "Basketball Thursday",
    tags: ["basketball"],
    icon: "🏀",
    skill: "Advanced",
    start: at(1, 20),
    end: at(1, 21),
    venue: "Melbourne Sports and Aquatic Centre",
    address: "00 Olympian Way, West Melbourne VIC 3003",
    lat: -37.8024,
    lng: 144.9437,
    cost: 12.5,
    max: 6,
    count: 5,
    description: "Half-court, winner stays. Competitive but nobody keeps score.",
  },
  {
    id: "evt-badminton-wednesday",
    title: "Badminton Wednesday",
    tags: ["badminton"],
    icon: "🏸",
    skill: "Intermediate",
    start: at(2, 18),
    end: at(2, 20),
    venue: "Glen Waverley Badminton Centre",
    address: "100 Kingsway, Glen Waverley VIC 3150",
    lat: -37.8708,
    lng: 145.1615,
    cost: 15,
    max: 4,
    count: 3,
    description: "The midweek one. Same court, same crowd, slightly more tired.",
  },
  {
    id: "evt-sunday-cricket",
    title: "Sunday Cricket",
    tags: ["cricket"],
    icon: "🏏",
    skill: "Beginner",
    start: at(6, 11),
    end: at(6, 15),
    venue: "Flemington Park",
    address: "Langs Rd, Ascot Vale VIC 3032",
    lat: -37.7806,
    lng: 144.9409,
    cost: 8,
    max: 6,
    count: 0,
    cancelled: true,
    description: "Twenty-over casual. Bat and ball provided.",
  },
  {
    id: "evt-tennis-saturday",
    title: "Tennis Saturday",
    tags: ["tennis"],
    icon: "🎾",
    skill: "Advanced",
    start: at(5, 9),
    end: at(5, 10),
    venue: "Coode Island",
    address: "Kings Way, Melbourne VIC 3008",
    lat: -37.8055,
    lng: 144.9525,
    cost: 20,
    max: 4,
    count: 1,
    description: "Singles if four show, doubles if six do. Courts 3 and 4.",
  },
  {
    id: "evt-netball-tuesday",
    title: "Netball Tuesday",
    tags: ["netball"],
    icon: "🥎",
    skill: "Beginner",
    start: at(8, 19),
    end: at(8, 20),
    venue: "Olympic Park",
    address: "747 Lewar St, Melbourne VIC 3000",
    lat: -37.81,
    lng: 144.9803,
    cost: 7,
    max: 10,
    count: 4,
    description: "Newcomers welcome — we teach the positions on the night.",
  },
];

const HOST = { id: "usr-host", displayName: "Priya Raman", avatarUrl: null };

const ROSTER = [
  { id: "usr-1", displayName: "Dan Patel", avatarUrl: null },
  { id: "usr-2", displayName: "Lisa Kim", avatarUrl: null },
  { id: "usr-3", displayName: "Marco Rossi", avatarUrl: null },
  { id: "usr-4", displayName: "Aisha Nawaz", avatarUrl: null },
  { id: "usr-5", displayName: "Tom Nguyen", avatarUrl: null },
];

/** Straight-line distance; good enough for fixtures and the distance sort. */
export function haversineKm(
  aLat: number,
  aLng: number,
  bLat: number,
  bLng: number,
): number {
  const toRad = (v: number) => (v * Math.PI) / 180;
  const dLat = toRad(bLat - aLat);
  const dLng = toRad(bLng - aLng);
  const h =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(aLat)) * Math.cos(toRad(bLat)) * Math.sin(dLng / 2) ** 2;
  return 12_742 * Math.asin(Math.sqrt(h));
}

function distanceFromCentre(seed: Seed): number {
  return Number(
    haversineKm(MELBOURNE_CBD.latitude, MELBOURNE_CBD.longitude, seed.lat, seed.lng).toFixed(1),
  );
}

function toListItem(seed: Seed, distanceKm: number): EventListItem {
  return {
    id: seed.id,
    title: seed.title,
    tags: seed.tags,
    sportIcon: seed.icon,
    skillLevel: seed.skill,
    startAt: seed.start,
    endAt: seed.end,
    timezone: "Australia/Melbourne",
    venueName: seed.venue,
    address: seed.address,
    latitude: seed.lat,
    longitude: seed.lng,
    cost: seed.cost,
    maxParticipants: seed.max,
    participantCount: seed.count,
    isCancelled: Boolean(seed.cancelled),
    distanceKm,
  };
}

export function fixtureList(): EventListItem[] {
  return seeds.map((s) => toListItem(s, distanceFromCentre(s)));
}

export function fixtureDetail(id: string): EventDetail | undefined {
  const seed = seeds.find((s) => s.id === id);
  if (!seed) return undefined;
  return {
    ...toListItem(seed, distanceFromCentre(seed)),
    description: seed.description ?? null,
    host: HOST,
    // One fixture per relation so every EventCard variant is reachable offline.
    isHost: seed.id === "evt-badminton-wednesday",
    isJoined: seed.id === "evt-basketball-thursday",
    participants: ROSTER.slice(0, Math.min(seed.count, ROSTER.length)),
    cancelledAt: seed.cancelled ? at(0, 9) : null,
  };
}

export function fixturePaged(): Paged<EventListItem> {
  const items = fixtureList();
  return { items, page: 1, pageSize: items.length, totalCount: items.length };
}

/** Mirrors `GET /api/tags/popular`: each tag with how many fixture events carry
 * it, most-used first. The home page's filter chips read this under fixtures. */
export function fixturePopularTags(): PopularTag[] {
  const counts = new Map<string, number>();
  for (const event of fixtureList()) {
    for (const tag of event.tags) counts.set(tag, (counts.get(tag) ?? 0) + 1);
  }
  return [...counts.entries()]
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
}

/**
 * Draft fixtures (used when VITE_/NEXT_PUBLIC_USE_FIXTURES=1) so the review flow
 * renders and Vitest/Playwright can exercise paste → draft → edit → publish.
 * Shaped exactly like the backend's EventDraftDto (flat source fields, ISO
 * instants in the payload) — the fixtures mirror the contract, they do not
 * replace it.
 */
const DRAFTS: EventDraft[] = [
  {
    id: "draft-1001",
    userId: "demo-user",
    status: "pending",
    payload: {
      title: "Thursday Night Netball",
      description: "Social netball, all levels. Bring a bib if you have one.",
      startAt: at(4, 20),
      endAt: at(4, 21, 30),
      timezone: "Australia/Melbourne",
      venueName: "Keilor Village Recreation Reserve",
      address: "45 Crane Rd, Keilor Village VIC 3036",
      latitude: -37.7229,
      longitude: 144.8225,
      maxParticipants: 12,
      cost: 10,
      tags: ["netball"],
    },
    confidence: 0.9,
    missingFields: [],
    subject: "Netball social this Thu?",
    fromAddr: "priya@example.com",
    sentAt: at(-1, 6, 30),
    bodyText: "Netball social this Thursday 8-9:30pm at Keilor Village Rec Reserve. $10 a head.",
    createdAt: at(-1, 6, 31),
    eventId: null,
    duplicateOfEventId: null,
    reviewNote: null,
    reviewedAt: null,
  },
  {
    id: "draft-1002",
    userId: "demo-user",
    status: "pending",
    payload: {
      title: "Community Cleanup & Social",
      venueName: "Yarra Bend Park",
    },
    confidence: 0.4,
    missingFields: ["startAt", "endAt", "maxParticipants", "cost"],
    subject: "Cleanup day (details TBC)",
    fromAddr: "hello@greenthicket.org",
    sentAt: at(-2, 19, 5),
    bodyText: "Park cleanup followed by a barbecue at Yarra Bend Park. Date and time still TBC.",
    createdAt: at(-2, 19, 6),
    eventId: null,
    duplicateOfEventId: null,
    reviewNote: null,
    reviewedAt: null,
  },
  {
    id: "draft-1003",
    userId: "demo-user",
    status: "pending",
    payload: {
      title: "Friday Social Badminton",
      description: "Same courts, all levels welcome.",
      startAt: at(5, 20),
      endAt: at(5, 22),
      timezone: "Australia/Melbourne",
      venueName: "Burwood East Community Badminton Club",
      address: "48 Burwood Hwy, Burwood East VIC 3151",
      latitude: -37.8313,
      longitude: 145.1722,
      maxParticipants: 8,
      cost: 7,
      tags: ["badminton", "social"],
    },
    confidence: 0.78,
    missingFields: [],
    subject: "Badminton Friday",
    fromAddr: "wei@example.com",
    sentAt: at(-3, 9, 12),
    bodyText: "Friday social badminton — Burwood East courts, 8pm-10pm, $7 a head.",
    createdAt: at(-3, 9, 13),
    eventId: null,
    duplicateOfEventId: "evt-1003",
    reviewNote: null,
    reviewedAt: null,
  },
];

let draftsStore = DRAFTS.map((d) => ({ ...d }));

export function fixtureDraftQueue(): DraftQueue {
  const pending = draftsStore.filter((d) => d.status === "pending");
  return {
    items: [...pending].sort((a, b) => b.createdAt.localeCompare(a.createdAt)).map((d) => ({ ...d })),
    totalCount: pending.length,
  };
}

export function fixtureDraft(id: string): EventDraft | undefined {
  return draftsStore.find((d) => d.id === id);
}

export function fixtureSaveDraft(
  id: string,
  payload: Partial<EventDraft["payload"]>,
): EventDraft | undefined {
  const draft = draftsStore.find((d) => d.id === id);
  if (!draft) return undefined;
  draft.payload = { ...draft.payload, ...payload };
  draft.missingFields = draft.missingFields.filter((f) => {
    const value = payload[f as keyof EventDraft["payload"]];
    return value == null || value === "";
  });
  return { ...draft };
}

export function fixtureDeleteDraft(id: string): void {
  draftsStore = draftsStore.filter((d) => d.id !== id);
}

export function fixtureAddDraft(payload: EventDraft["payload"]): EventDraft {
  const now = new Date().toISOString();
  const created: EventDraft = {
    id: `draft-${Date.now()}`,
    userId: "demo-user",
    status: "pending",
    payload,
    confidence: 0.55,
    missingFields: payload.startAt ? [] : ["startAt", "endAt"],
    subject: payload.title ?? "Pasted message",
    fromAddr: "you@example.com",
    sentAt: now,
    bodyText: "",
    createdAt: now,
    eventId: null,
    duplicateOfEventId: null,
    reviewNote: null,
    reviewedAt: null,
  };
  draftsStore = [created, ...draftsStore];
  return { ...created };
}

