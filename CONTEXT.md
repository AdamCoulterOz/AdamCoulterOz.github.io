# Repository Context

## Purpose and current state

This repository owns Adam Coulter's root GitHub Pages site at `https://adamcoulteroz.github.io/`. The site is a lightweight personal project index linking to public sub-sites hosted under the same Pages origin.

The current index contains:

- Bandwidth Calculator — `/bandwidth-calculator/`
- Fluent Icon Browser — `/fluent-icon-browser/`
- Meridian — `/Meridian/`

## Architecture

- `site/index.html` owns the semantic project index and public metadata.
- `site/styles.css` owns the responsive visual system and system-following light/dark themes. Its Keel design tokens, typography, surfaces, spacing, radii, and motion align with the Meridian public site.
- `site/404.html` owns the root-site not-found experience.
- `site/robots.txt` and `site/sitemap.xml` expose the root index and all verified child sites to crawlers.
- `site/google8597bcde2d69f04b.html` is the durable Google Search Console ownership proof for the root URL-prefix property.
- `.github/workflows/deploy-pages.yml` uploads the static `site` directory and deploys it to GitHub Pages after every `main` push.

## Current invariants

- The root site remains static HTML and CSS with no runtime framework or build dependency.
- The full project index and descriptions remain available without JavaScript. Canonical, social, keyword, crawler, and structured metadata describe the same visible projects rather than a crawler-only variant.
- Project entries link only to verified public Pages sites.
- The visible index and `INTERFACE.md` project list stay synchronized.
- The page follows the operating-system colour scheme and remains usable at a 320px viewport width.
- The visual language remains aligned with Meridian's published Keel system while retaining this site's project-index information architecture.
- The Google ownership verification file remains published at the site root so Search Console access is not invalidated.
- The workflow publishes only `site`, not repository documentation or implementation metadata.

## Operational constraints

- `main` is the default and deployment branch.
- The repository name must remain `AdamCoulterOz.github.io` to preserve the root Pages URL.
- A project page is owned and deployed by its own repository; this repository owns discovery links only.

## Outstanding actions and technical debt

- Add future project sites to the index only after their Pages deployment is live.
