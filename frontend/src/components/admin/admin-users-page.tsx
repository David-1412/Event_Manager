"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { ConfirmDialog } from "@/components/ui/dialog";
import { Select } from "@/components/ui/field";
import { toast } from "@/components/ui/toast";
import { RoleBadge } from "@/components/admin/role-badge";
import { PendingEventsPanel } from "@/components/admin/pending-events-panel";
import { useAuth } from "@/lib/auth/auth-provider";
import { ApiError, usingFixtures } from "@/lib/api";
import { listAdminUsers, setUserRole } from "@/lib/admin";
import { USER_ROLES } from "@/lib/auth/permissions";
import type { AdminUser, UserRole } from "@/types/admin";

/**
 * The admin console: every account with its role and signup date, a role picker
 * (Member, Creator, Admin) with a confirmation step, and the queue of public events
 * awaiting approval.
 *
 * **The server is the authority.** `GET /api/admin/users` answers 404 to anyone who
 * is not an Admin — a Creator included — and that 404, not a client-side role check,
 * is what this page treats as "you don't work here". A guard that guessed from
 * `useAuth().permissions` would misrender during a session restore, and would be a
 * second, weaker copy of a decision the API already makes. The nav link is hidden
 * from Members and Creators purely so the surface is invisible rather than merely
 * forbidden.
 *
 * Every role change goes through `ConfirmDialog` first. The action is low-effort and
 * high-consequence, and the dialog is where the consequence gets stated — including
 * the case that matters most, demoting yourself.
 */
export function AdminUsersPage() {
  const { user, loading, permissions, refreshRole } = useAuth();

  const [users, setUsers] = useState<AdminUser[]>([]);
  const [adminCount, setAdminCount] = useState<number | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  /** Set when the list request 404s: the caller is not an Admin. */
  const [forbidden, setForbidden] = useState(false);

  const [pending, setPending] = useState<{ user: AdminUser; to: UserRole } | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (usingFixtures) {
      // No API to ask. Render the empty state rather than fixture accounts: a demo
      // table of fake people you could "promote" would be indistinguishable from a
      // working admin page.
      setIsLoading(false);
      return;
    }
    setIsLoading(true);
    try {
      const res = await listAdminUsers();
      setUsers(res.items);
      setAdminCount(res.adminCount);
      setForbidden(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) setForbidden(true);
      else toast(messageFor(err, "Could not load users."));
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!loading) void load();
  }, [loading, load]);

  async function confirmChange() {
    if (!pending) return;
    setBusy(true);
    try {
      const result = await setUserRole(pending.user.id, pending.to);

      setUsers((rows) => rows.map((r) => (r.id === result.user.id ? result.user : r)));
      setAdminCount(result.adminCount);
      toast(`Role changed to ${result.user.role}`);

      // The caller just changed a role that may be their own. Re-read it so the nav
      // link and this page's own visibility follow the database immediately —
      // otherwise demoting yourself leaves an Admin console on screen that can no
      // longer do anything, with nothing on it explaining why.
      await refreshRole();
    } catch (err) {
      // 422 from the last-admin floor arrives with the server's own sentence, which
      // is better wording than this file could invent because only the server knows
      // the count.
      toast(messageFor(err, "Could not change the role."));
    } finally {
      setBusy(false);
      setPending(null);
    }
  }


  if (loading || isLoading) {
    return (
      <main className="mx-auto flex w-full max-w-[900px] flex-col gap-4 px-4 py-6">
        <h1 className="text-h1 text-fg">Admin</h1>
        <ul className="flex flex-col gap-2" aria-busy="true">
          {[0, 1, 2].map((i) => (
            <li key={i} className="h-14 animate-pulse rounded-md bg-surface-2" />
          ))}
        </ul>
      </main>
    );
  }

  // Not signed in at all: say so and offer the way in, rather than the 404 wording
  // that would imply the account lacks a permission it never had a chance to have.
  if (!user) {
    return (
      <main className="mx-auto flex w-full max-w-[900px] flex-col gap-4 px-4 py-6">
        <h1 className="text-h1 text-fg">Admin</h1>
        <p className="rounded-md border border-dashed border-border px-4 py-8 text-center text-meta text-fg-muted">
          {"Sign in with an Admin account to manage users. "}
          <Link href="/login?next=%2Fadmin%2Fusers" className="text-brand-600 underline">
            Sign in
          </Link>
        </p>
      </main>
    );
  }

  if (forbidden || (!permissions.canManageUsers && users.length === 0)) {
    return (
      <main className="mx-auto flex w-full max-w-[900px] flex-col gap-4 px-4 py-6">
        <h1 className="text-h1 text-fg">Not found</h1>
        <p className="rounded-md border border-dashed border-border px-4 py-8 text-center text-meta text-fg-muted">
          This page doesn&rsquo;t exist for your account.
          <Link href="/" className="ml-1 text-brand-600 underline">Back to browse</Link>
        </p>
      </main>
    );
  }

  const selfId = user.id;

  return (
    <main className="mx-auto flex w-full max-w-[900px] flex-col gap-6 px-4 py-6">
      <div className="flex items-baseline justify-between gap-3">
        <h1 className="text-h1 text-fg">Admin</h1>
        <span className="text-meta text-fg-muted">
          {adminCount === null
            ? `${users.length} accounts`
            : `${users.length} accounts · ${adminCount} Admin${adminCount === 1 ? "" : "s"}`}
        </span>
      </div>

      <PendingEventsPanel />
      <UserTable
        users={users}
        adminCount={adminCount}
        selfId={selfId}
        onRequestChange={(u, to) => setPending({ user: u, to })}
      />

      <ConfirmDialog
        open={pending !== null}
        titleId="admin-role-change-title"
        title={pending ? `Change role to ${pending.to}` : ""}
        body={
          pending ? (
            <span>
              {`Make `}
              <strong>{pending.user.email ?? pending.user.name}</strong>
              {` ${pending.to === "Admin" ? "an" : "a"} ${pending.to}? `}
              {consequenceOf(pending.user.role, pending.to)}
              {pending.user.id === selfId ? " This is your own account." : ""}
            </span>
          ) : null
        }
        confirmLabel="Change role"
        busy={busy}
        onConfirm={() => void confirmChange()}
        onClose={() => setPending(null)}
      />
    </main>
  );
}

/** What a role change means for the person, stated in the confirmation dialog. */
function consequenceOf(from: UserRole, to: UserRole): string {
  if (to === "Admin") {
    return "They will be able to manage every account and approve or reject any event.";
  }
  const losesConsole = from === "Admin" ? " They will lose access to this console immediately." : "";
  if (to === "Creator") {
    return `Their public events will publish without review. They cannot review other people's events or manage users.${losesConsole}`;
  }
  return `Their public events will need approval before appearing on Browse.${losesConsole}`;
}

/**
 * The accounts table. Split out so the page's state machine and the table's
 * presentational rules — notably *when a role change is offerable* — stay readable
 * apart.
 *
 * The last Admin's role picker is disabled rather than hidden: a control that
 * silently isn't there reads as a bug, while a disabled one with a `title` explains
 * the rule. The server enforces the same floor, so this only saves the click.
 *
 * Picking a role does not change it: the select stays on the current role (it is
 * controlled by the row) and the confirmation dialog opens. Only the server's answer
 * moves the row.
 */
function UserTable({
  users,
  adminCount,
  selfId,
  onRequestChange,
}: {
  users: AdminUser[];
  adminCount: number | null;
  selfId?: string;
  onRequestChange: (user: AdminUser, to: UserRole) => void;
}) {
  return (
    <section aria-labelledby="admin-users-table-title" className="flex flex-col gap-3">
      <h2 id="admin-users-table-title" className="text-h2 text-fg">Accounts</h2>
      <p className="text-meta text-fg-muted">
        Creators publish their own public events without review. Admins also get this
        console and the event review queue. The last remaining Admin cannot change role —
        there would be no way back in.
      </p>

      {users.length === 0 ? (
        <p className="rounded-md border border-dashed border-border px-4 py-8 text-center text-meta text-fg-muted">
          No accounts yet.
        </p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-border">
          <table className="w-full border-collapse text-left text-meta">
            <thead>
              <tr className="border-b border-border bg-surface-2 text-micro uppercase text-fg-muted">
                <th scope="col" className="px-3 py-2 font-medium">Email</th>
                <th scope="col" className="px-3 py-2 font-medium">Role</th>
                <th scope="col" className="px-3 py-2 font-medium">Joined</th>
                <th scope="col" className="px-3 py-2 font-medium">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {users.map((row) => {
                const isSelf = row.id === selfId;
                const isLastAdmin = adminCount === 1 && row.role === "Admin";
                return (
                  <tr key={row.id} className="border-b border-border last:border-0">
                    <td className="px-3 py-2 text-fg">
                      <span className="block max-w-[28ch] truncate">
                        {row.email ?? <span className="text-fg-muted">(no email)</span>}
                      </span>
                      <span className="block text-micro text-fg-muted">
                        {row.name}
                        {isSelf ? " · you" : ""}
                      </span>
                    </td>
                    <td className="px-3 py-2"><RoleBadge role={row.role} /></td>
                    <td className="px-3 py-2 whitespace-nowrap text-fg-muted">
                      {formatJoinDate(row.createdAt)}
                    </td>
                    <td className="px-3 py-2 text-right">
                      <Select
                        aria-label={`Role for ${row.email ?? row.name}`}
                        value={row.role}
                        disabled={isLastAdmin}
                        title={isLastAdmin ? "This is the only Admin account" : undefined}
                        onChange={(e) => {
                          const to = e.target.value as UserRole;
                          if (to !== row.role) onRequestChange(row, to);
                        }}
                        className="h-9 min-w-[8.5rem]"
                      >
                        {USER_ROLES.map((role) => (
                          <option key={role} value={role}>
                            {role}
                          </option>
                        ))}
                      </Select>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

/** Server sentences first: the last-admin floor and the concurrent-edit guard both
 * arrive as 422 with wording chosen where the facts live. */
function messageFor(err: unknown, fallback: string): string {
  return err instanceof ApiError ? err.detail ?? fallback : fallback;
}

function formatJoinDate(iso: string): string {
  const time = Date.parse(iso);
  if (Number.isNaN(time)) return "—";
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(new Date(time));
}

