import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { ShareButton } from "@/components/event/share-button";
import { Toaster } from "@/components/ui/toast";

/**
 * Which share surface a tap produces, and what Copy Link does.
 *
 * The branch under test is the one that decides between a native sheet and the
 * modal, so the two capability halves are stubbed per test rather than globally:
 * a jsdom browser has neither API, which is exactly the desktop case, and the
 * mobile case has to be simulated.
 */
const EVENT = {
  title: "Sunday social",
  description: "Casual basketball at the park, all levels welcome.",
};

function setClipboard(writeText: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, "clipboard", {
    configurable: true,
    value: { writeText },
  });
}

function setShare(share: ((data: ShareData) => Promise<void>) | undefined) {
  Object.defineProperty(navigator, "share", {
    configurable: true,
    value: share,
  });
}

beforeEach(() => {
  setShare(undefined);
  setClipboard(async () => {});
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("ShareButton - desktop, no native share sheet", () => {
  it("opens the share modal with the event URL instead of doing nothing", async () => {
    render(<ShareButton {...EVENT} />);
    fireEvent.click(screen.getByRole("button", { name: /share/i }));
    const dialog = await screen.findByRole("dialog");
    expect(dialog).toBeTruthy();
  });

  it("puts the current page URL in the field so it can be read and copied", async () => {
    render(<ShareButton {...EVENT} />);
    fireEvent.click(screen.getByRole("button", { name: /share/i }));
    const field = (await screen.findByLabelText(/event link/i)) as HTMLInputElement;
    expect(field.value).toBe(window.location.href);
  });

  it("names the event in the modal, so a person sharing two of them knows which is which", async () => {
    render(<ShareButton {...EVENT} />);
    fireEvent.click(screen.getByRole("button", { name: /share/i }));
    expect(await screen.findByText(EVENT.title)).toBeTruthy();
  });

  it("closes on Escape, which the shared dialog shell already wires", async () => {
    render(<ShareButton {...EVENT} />);
    fireEvent.click(screen.getByRole("button", { name: /share/i }));
    await screen.findByRole("dialog");
    fireEvent.keyDown(document, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });
});

describe("ShareButton - copy link", () => {
  it("writes the URL to the clipboard and confirms it", async () => {
    const writeText = vi.fn(async () => {});
    setClipboard(writeText);
    render(
      <>
        <ShareButton {...EVENT} />
        <Toaster />
      </>,
    );
    fireEvent.click(screen.getByRole("button", { name: /share/i }));
    fireEvent.click(await screen.findByRole("button", { name: /copy link/i }));

    await waitFor(() =>
      expect(writeText).toHaveBeenCalledWith(window.location.href),
    );
    expect(await screen.findByText("\u2705 Link copied to clipboard")).toBeTruthy();
  });

  it("flips its own label to Copied, so the confirmation survives the toast", async () => {
    setClipboard(async () => {});
    render(<ShareButton {...EVENT} />);
    fireEvent.click(screen.getByRole("button", { name: /share/i }));
    fireEvent.click(await screen.findByRole("button", { name: /copy link/i }));
    expect(await screen.findByRole("button", { name: /copied/i })).toBeTruthy();
  });

  it("offers the manual route rather than a false success when the clipboard refuses", async () => {
    setClipboard(async () => {
      throw new Error("NotAllowedError");
    });
    render(
      <>
        <ShareButton {...EVENT} />
        <Toaster />
      </>,
    );
    fireEvent.click(screen.getByRole("button", { name: /share/i }));
    fireEvent.click(await screen.findByRole("button", { name: /copy link/i }));
    expect(await screen.findByText(/select the link and copy it/i)).toBeTruthy();
    expect(screen.queryByText(/link copied/i)).toBeNull();
  });

  it("does not claim success when the clipboard API is missing entirely", async () => {
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: undefined });
    render(
      <>
        <ShareButton {...EVENT} />
        <Toaster />
      </>,
    );
    fireEvent.click(screen.getByRole("button", { name: /share/i }));
    fireEvent.click(await screen.findByRole("button", { name: /copy link/i }));
    expect(await screen.findByText(/select the link and copy it/i)).toBeTruthy();
  });
});

describe("ShareButton - mobile, native share sheet", () => {
  it("sends the title, the description and the URL to the native sheet", async () => {
    // The payload is read back from a variable the stub writes rather than from
    // `mock.calls`, so the stub's parameter is genuinely used and the shape is
    // checked against the DOM lib's `ShareData` - which is the contract a phone
    // actually enforces.
    let captured: ShareData | undefined;
    const share = vi.fn(async (data: ShareData) => {
      captured = data;
    });
    setShare(share);
    render(<ShareButton {...EVENT} />);
    fireEvent.click(screen.getByRole("button", { name: /share/i }));

    await waitFor(() => expect(share).toHaveBeenCalledTimes(1));
    expect(captured).toEqual({
      title: EVENT.title,
      text: EVENT.description,
      url: window.location.href,
    });
  });

  it("never opens the modal when the sheet took the payload", async () => {
    setShare(async () => {});
    render(<ShareButton {...EVENT} />);
    fireEvent.click(screen.getByRole("button", { name: /share/i }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("falls back to the modal when the sheet fails, so the tap is not a dead end", async () => {
    setShare(async () => {
      throw new Error("NotAllowedError");
    });
    render(<ShareButton {...EVENT} />);
    fireEvent.click(screen.getByRole("button", { name: /share/i }));
    expect(await screen.findByRole("dialog")).toBeTruthy();
  });
});