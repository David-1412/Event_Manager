"use client";

import { useState } from "react";
import { mutate as globalMutate } from "swr";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ConfirmDialog } from "@/components/ui/dialog";
import { toast } from "@/components/ui/toast";
import { addDemoEngagement, removeDemoEngagement, type DemoEngagementResult } from "@/lib/admin";
import { ApiError } from "@/lib/api";

/**
 * Admin: sample engagement for a new site. "Add" gives every event and forum post
 * 1-9 anonymous views and 1-3 Interested marks from the placeholder accounts
 * demo1..demo20, which nobody else can see; "Remove" takes back exactly what it
 * added. Both ask first, because both change numbers every visitor sees.
 */
export function DemoEngagementPanel() {
  const [confirm, setConfirm] = useState<"add" | "remove" | null>(null);
  const [busy, setBusy] = useState(false);
  const [last, setLast] = useState<{ action: "add" | "remove"; result: DemoEngagementResult } | null>(null);

  async function run(action: "add" | "remove") {
    setBusy(true);
    try {
      const result = action === "add" ? await addDemoEngagement() : await removeDemoEngagement();
      setLast({ action, result });
      setConfirm(null);
      // Counts moved on every event and post: refresh whatever is cached.
      void globalMutate((key) => typeof key === "string" && /\/api\/(events|forum|trending)/.test(key));
      toast(action === "add" ? "Demo engagement added" : "Demo engagement removed", "success");
    } catch (error) {
      toast(error instanceof ApiError ? error.message : "That didn't work. Try again.", "info");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card className="flex flex-col gap-3 p-4">
      <div>
        <h2 className="text-h3 text-fg">Demo engagement</h2>
        <p className="mt-1 text-meta text-fg-muted">
          Gives every event and forum post 1&ndash;9 views and 1&ndash;3 Interested from 20 hidden
          demo accounts (demo1&ndash;demo20). They don&apos;t appear in profiles, activity or messages.
          Each press adds more; Remove takes all of it back.
        </p>
      </div>
      <div className="flex flex-wrap gap-2">
        <Button size="sm" onClick={() => setConfirm("add")} disabled={busy}>
          Add demo views &amp; interests
        </Button>
        <Button size="sm" variant="secondary" onClick={() => setConfirm("remove")} disabled={busy}>
          Remove demo engagement
        </Button>
      </div>
      {last && (
        <p role="status" className="text-meta text-fg-muted">
          {last.action === "add" ? "Added" : "Removed"} {last.result.views.toLocaleString("en-AU")} views and{" "}
          {last.result.interests.toLocaleString("en-AU")} Interested across {last.result.events} events and{" "}
          {last.result.posts} posts.
        </p>
      )}

      <ConfirmDialog
        open={confirm !== null}
        titleId="demo-engagement-title"
        title={confirm === "remove" ? "Remove demo engagement?" : "Add demo engagement?"}
        body={
          confirm === "remove"
            ? "Every demo view and Interested goes, and counts return to real engagement only."
            : "Every event and post gets 1-9 more views and 1-3 more Interested from the demo accounts. Visitors will see the new counts."
        }
        confirmLabel={confirm === "remove" ? "Remove" : "Add"}
        busy={busy}
        onConfirm={() => confirm && void run(confirm)}
        onClose={() => setConfirm(null)}
      />
    </Card>
  );
}
