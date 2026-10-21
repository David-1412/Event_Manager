import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { ThumbnailInput } from "@/components/event/thumbnail-input";
import { uploadThumbnail } from "@/features/create/thumbnail-api";
import { mediaUrl } from "@/lib/api";

vi.mock("@/lib/api", async (original) => ({
  ...(await original<object>()),
  usingFixtures: false,
}));
vi.mock("@/features/create/thumbnail-api", () => ({
  uploadThumbnail: vi.fn(async () => "/uploads/0123456789abcdef0123456789abcdef.png"),
}));

describe("mediaUrl", () => {
  it("uses absolute and root-relative URLs as they are", () => {
    expect(mediaUrl("https://cdn.example.com/a.png")).toBe("https://cdn.example.com/a.png");
    expect(mediaUrl("http://x.test/a.png")).toBe("http://x.test/a.png");
    // Same-origin: the web server forwards /uploads to the API (next.config.ts).
    expect(mediaUrl("/uploads/abc.png")).toBe("/uploads/abc.png");
  });

  it("makes a bare path root-relative and keeps empty input empty", () => {
    expect(mediaUrl("uploads/abc.png")).toBe("/uploads/abc.png");
    expect(mediaUrl("")).toBeNull();
    expect(mediaUrl("   ")).toBeNull();
    expect(mediaUrl(null)).toBeNull();
    expect(mediaUrl(undefined)).toBeNull();
  });
});

/** The form's side: holds the value the way react-hook-form does. */
function Harness({ onValue }: { onValue: (url: string | null) => void }) {
  const [value, setValue] = useState<string | null>(null);
  return (
    <ThumbnailInput
      id="photo"
      value={value}
      onChange={(url) => {
        setValue(url);
        onValue(url);
      }}
    />
  );
}

describe("ThumbnailInput", () => {
  it("previews the uploaded image and hands the relative URL to the form", async () => {
    const onValue = vi.fn();
    const { container } = render(<Harness onValue={onValue} />);
    const file = new File([new Uint8Array(1000)], "poster.png", { type: "image/png" });

    await userEvent.upload(container.querySelector<HTMLInputElement>("input[type=file]")!, file);

    expect(uploadThumbnail).toHaveBeenCalledWith(file);
    // The payload carries the relative path; the preview loads it same-origin.
    expect(onValue).toHaveBeenCalledWith("/uploads/0123456789abcdef0123456789abcdef.png");
    const img = await screen.findByRole("presentation");
    expect(img).toHaveAttribute("src", "/uploads/0123456789abcdef0123456789abcdef.png");
  });

  it("says so instead of showing a broken image when the preview cannot load", async () => {
    const { container } = render(<Harness onValue={vi.fn()} />);
    await userEvent.upload(
      container.querySelector<HTMLInputElement>("input[type=file]")!,
      new File([new Uint8Array(10)], "poster.png", { type: "image/png" }),
    );

    fireEvent.error(await screen.findByRole("presentation"));

    expect(screen.getByText(/preview couldn.t load/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Remove" })).toBeInTheDocument();
  });
});
