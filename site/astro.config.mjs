// @ts-check
import { defineConfig, fontProviders } from "astro/config"

// A static site: `npm run build` writes plain HTML, CSS and JS to dist/.
export default defineConfig({
  // Inter, self-hosted at build time (no request to Google from visitors).
  fonts: [
    {
      provider: fontProviders.fontsource(),
      name: "Inter",
      cssVariable: "--font-inter",
      weights: ["400 700"],
      styles: ["normal"],
      subsets: ["latin"],
      fallbacks: ["ui-sans-serif", "system-ui", "sans-serif"],
    },
  ],

  // The changelog page renders ../CHANGELOG.md from the repo root, so it's written once.
  vite: {
    server: { fs: { allow: [".."] } },
  },
})
