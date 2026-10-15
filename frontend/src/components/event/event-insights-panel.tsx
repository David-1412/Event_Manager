"use client";

import { useEventAnalytics } from "@/features/events/use-events";

/**
 * The host's Event Insights: how many people looked, how many said Interested, how
 * many are in.
 *
 * Mounted only for the event's host. That is not decoration — `views` is a private
 * number and the endpoint 404s for everyone else, so a fetch outside that condition
 * would fail on purpose. The panel is the client half of a rule the server enforces
 * on its own; hiding it here is courtesy, not security.
 *
 * The three counts are a set rather than a funnel to be read top-down. Views and
 * Interested both include people who never joined, and a joined person was cleared
 * out of Interested when they joined, so the rows do not subtract into anything
 * meaningful and are presented as three facts side by side.
 */
export function EventInsightsPanel({ eventId }: { eventId: string }) {
  const { analytics, error, isLoading } = useEventAnalytics(eventId, true);

  if (error) {
    // Stated as unavailable rather than as zero. A host shown "Views: 0" on a
    // failed read would conclude nobody is interested, which is the opposite of
    // what a network blip means.
    return (
      <section
        aria-labelledby="insights-heading"
        className="rounded-card border border-line bg-surface p-4"
      >
        <h2 id="insights-heading" className="text-h4">
          {"\u{1F4CA}"} Event Insights
        </h2>
        <p className="mt-2 text-meta text-fg-muted">
          Event insights could not be loaded right now.
        </p>
      </section>
    );
  }

  return (
    <section
      aria-labelledby="insights-heading"
      className="rounded-card border border-line bg-surface p-4"
    >
      <h2 id="insights-heading" className="text-h4">
        {"\u{1F4CA}"} Event Insights
      </h2>
      <dl className="mt-3 flex flex-wrap gap-x-8 gap-y-3">
        <Metric label="Views" value={analytics?.views} loading={isLoading} />
        <Metric label="Interested" value={analytics?.interestedCount} loading={isLoading} />
        <Metric label="Joined" value={analytics?.joinedCount} loading={isLoading} />
      </dl>
    </section>
  );
}

/**
 * One figure. `value` is nullable rather than defaulted to 0 because "not loaded
 * yet" and "genuinely zero" are different answers to a host deciding whether to
 * promote the event, and rendering 0 for the first one is how a dashboard teaches
 * its owner to ignore it.
 */
function Metric({
  label,
  value,
  loading,
}: {
  label: string;
  value: number | undefined;
  loading: boolean;
}) {
  return (
    <div className="flex flex-col">
      <dt className="text-micro uppercase text-fg-muted">{label}</dt>
      <dd className="text-h3 tabular-nums text-fg" aria-busy={value === undefined}>
        {value === undefined ? (loading ? "…" : "—") : value}
      </dd>
    </div>
  );
}
