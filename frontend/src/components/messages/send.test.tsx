import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MessagesView } from "@/components/messages/messages-view";
import { sendMessage } from "@/features/messages/messages-api";
import { useThread } from "@/features/messages/use-messages";
import type { DirectMessage } from "@/types/messages";

vi.mock("@/lib/auth/auth-provider", () => ({
  useAuth: () => ({ user: { id: "u-me", displayName: "Me" }, loading: false }),
}));
vi.mock("@/lib/query", () => ({ useSearchParamsValue: () => new URLSearchParams("c=c-1") }));
vi.mock("@/features/messages/messages-api", async (original) => ({
  ...(await original<object>()),
  sendMessage: vi.fn(),
  markConversationRead: vi.fn(async () => undefined),
}));
vi.mock("@/features/messages/use-messages", () => ({
  useConversations: () => ({ conversations: [], isLoading: false, error: undefined, retry: vi.fn() }),
  useConversation: () => ({
    conversation: { id: "c-1", other: { id: "u-2", name: "Priya", photoUrl: null, role: "Member" } },
  }),
  useThread: vi.fn(),
  useNotificationCounts: () => ({ comments: 0, likes: 0, events: 0, social: 0, total: 0 }),
  useActivity: () => ({ items: [], isLoading: false, error: undefined, mutate: vi.fn() }),
  useEventNotifications: () => ({ list: { items: [], unreadCount: 0 }, isLoading: false, error: undefined, mutate: vi.fn() }),
}));

const appendMessage = vi.fn();

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

const stored: DirectMessage = {
  id: "m-1",
  conversationId: "c-1",
  senderId: "u-me",
  content: "Hello",
  createdAt: new Date().toISOString(),
};

/**
 * Sending is optimistic: the message is on screen and the box is empty before the
 * server has answered, the stored copy then takes its place, and a failure stays
 * visible on the message with a Retry rather than vanishing.
 */
describe("sending a message", () => {
  beforeEach(() => {
    appendMessage.mockReset();
    vi.mocked(sendMessage).mockReset();
    vi.mocked(useThread).mockReturnValue({
      messages: [],
      isLoading: false,
      hasOlder: false,
      isLoadingOlder: false,
      loadOlder: vi.fn(),
      refresh: vi.fn(),
      appendMessage,
      error: undefined,
    } as never);
  });

  it("shows the message and clears the box before the server answers", async () => {
    const request = deferred<DirectMessage>();
    vi.mocked(sendMessage).mockReturnValue(request.promise);
    render(<MessagesView />);

    const box = screen.getByRole("textbox", { name: /Message Priya/ });
    await userEvent.type(box, "Hello{Enter}");

    expect(screen.getByText("Hello")).toBeInTheDocument();
    expect(screen.getByText("Sending…")).toBeInTheDocument();
    expect(box).toHaveValue("");
    expect(sendMessage).toHaveBeenCalledWith("c-1", "Hello");
    expect(appendMessage).not.toHaveBeenCalled();
  });

  it("puts the stored copy into the thread once the server confirms", async () => {
    const request = deferred<DirectMessage>();
    vi.mocked(sendMessage).mockReturnValue(request.promise);
    render(<MessagesView />);
    await userEvent.type(screen.getByRole("textbox", { name: /Message Priya/ }), "Hello{Enter}");

    await act(async () => request.resolve(stored));

    expect(appendMessage).toHaveBeenCalledWith(stored);
    expect(screen.queryByText("Sending…")).toBeNull();
  });

  it("keeps a failed message on screen with Retry, and retrying sends it again", async () => {
    const first = deferred<DirectMessage>();
    vi.mocked(sendMessage).mockReturnValueOnce(first.promise).mockResolvedValueOnce(stored);
    render(<MessagesView />);
    await userEvent.type(screen.getByRole("textbox", { name: /Message Priya/ }), "Hello{Enter}");

    await act(async () => first.reject(new Error("offline")));
    expect(screen.getByRole("alert")).toHaveTextContent("Not sent");
    expect(screen.getByText("Hello")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(sendMessage).toHaveBeenCalledTimes(2);
    expect(vi.mocked(sendMessage).mock.calls[1]).toEqual(["c-1", "Hello"]);
    expect(appendMessage).toHaveBeenCalledWith(stored);
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("lets a failed message be removed", async () => {
    vi.mocked(sendMessage).mockRejectedValueOnce(new Error("offline"));
    render(<MessagesView />);
    await userEvent.type(screen.getByRole("textbox", { name: /Message Priya/ }), "Oops{Enter}");
    await userEvent.click(await screen.findByRole("button", { name: "Remove" }));
    expect(screen.queryByText("Oops")).toBeNull();
  });

  it("does not send an empty message", async () => {
    render(<MessagesView />);
    await userEvent.type(screen.getByRole("textbox", { name: /Message Priya/ }), "   {Enter}");
    expect(sendMessage).not.toHaveBeenCalled();
  });
});
