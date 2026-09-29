import type { Metadata, Viewport } from "next";
import localFont from "next/font/local";
import { SiteHeader } from "@/components/layout/site-header";
import { AuthProvider } from "@/lib/auth/auth-provider";
import { ThemeProvider } from "@/components/layout/theme-provider";
import { Toaster } from "@/components/ui/toast";
import "./globals.css";

/**
 * Self-hosted through `next/font` so no render-blocking request leaves the
 * origin (spec §11). Inter is the family the token scale was drawn against.
 *
 * `next/font/google` fetches from Google Fonts at build time, which would make
 * `docker build` fail whenever that host is unreachable; the fonts are the same
 * Inter files, vendored by `@fontsource-variable/inter`. The variable axis
 * covers every weight tokens.css asks for (400 body, 500 meta, 600 h3/micro,
 * 700 hero/h2) in one file.
 */
const inter = localFont({
  src: "./fonts/inter-latin-wght.woff2",
  variable: "--font-sans",
  display: "swap",
});

export const metadata: Metadata = {
  title: { default: "Event Manager", template: "%s · Event Manager" },
  description: "Find and join local sports games near you.",
};

export const viewport: Viewport = {
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#ffffff" },
    { media: "(prefers-color-scheme: dark)", color: "#0b0f14" },
  ],
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html
      lang="en"
      suppressHydrationWarning
      className={`${inter.variable} h-full antialiased`}
    >
      <body className="flex min-h-full flex-col bg-surface text-fg">
        <ThemeProvider>
          {/* Above SiteHeader because the header renders the session: one
              provider for both, and it must be an ancestor of both. */}
          <AuthProvider>
            {/* first focusable thing on every page (spec §10) */}
            <a
              href="#main"
              className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-md focus:border focus:border-border focus:bg-surface focus:px-4 focus:py-2 focus:text-body focus:text-fg"
            >
              Skip to content
            </a>
            <SiteHeader />
            <main id="main" className="flex flex-1 flex-col">
              {children}
            </main>
            <Toaster />
          </AuthProvider>
        </ThemeProvider>
      </body>
    </html>
  );
}
