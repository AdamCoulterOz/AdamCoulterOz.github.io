# Repository Context

## Purpose and current state

This repository owns Adam Coulter's root GitHub Pages site at `https://adamcoulteroz.github.io/`. The site is a lightweight personal project index linking to public sub-sites hosted under the same Pages origin.

The current index contains:

- Bandwidth Calculator — `/bandwidth-calculator/`
- Fluent Icon Browser — `/fluent-icon-browser/`
- Meridian — `/Meridian/`

Its header also provides direct email, LinkedIn, and GitHub profile links.

## Architecture

- `site/index.html` owns the semantic project index and public metadata.
- `site/styles.css` owns the responsive visual system and system-following light/dark themes. Its Keel design tokens, typography, surfaces, spacing, radii, and motion align with the Meridian public site.
- `site/404.html` owns the root-site not-found experience.
- `site/robots.txt` and `site/sitemap.xml` expose the root index and all verified child sites to crawlers.
- `site/google8597bcde2d69f04b.html` is the durable Google Search Console ownership proof for the root URL-prefix property.
- `.github/workflows/deploy-pages.yml` uploads the static `site` directory and deploys it to GitHub Pages after every `main` push.
- `site/app-insights.js` emits only explicit, cookie- and storage-free root-page views and allowlisted project/contact click categories; it redacts URL and referrer fields before telemetry leaves the browser.
- `infra/bootstrap/` is the separate, operator-run Terraform trust/state root. `archive/` contains the .NET 10 isolated weekly archive Function and its platform Terraform root.

## Current invariants

- The root site remains static HTML and CSS with no runtime framework or build dependency.
- The full project index and descriptions remain available without JavaScript. Canonical, social, keyword, crawler, and structured metadata describe the same visible projects rather than a crawler-only variant.
- Project entries link only to verified public Pages sites.
- The visible index and `INTERFACE.md` project list stay synchronized.
- The header's email and profile destinations remain synchronized with `INTERFACE.md` and the `Person` structured data.
- The page follows the operating-system colour scheme and remains usable at a 320px viewport width.
- The visual language remains aligned with Meridian's published Keel system while retaining this site's project-index information architecture.
- The Google ownership verification file remains published at the site root so Search Console access is not invalidated.
- The Pages artifact contains only `site` content, not repository documentation or implementation metadata; the workflow provisions Azure first, then deploys that staged artifact.
- The Application Insights connection string is generated only in the staged Pages artifact; it is not committed or retained as a long-lived GitHub secret.
- The weekly Function runs on Flex Consumption FC1 with one 2,048 MB instance maximum and no always-ready instances. Its user-assigned managed identity, not storage keys or connection strings, accesses Log Analytics and archive storage.
- Archive rows are create-only and checkpoints advance by ETag only after record blobs and manifests are durable; a failed run can replay but cannot acknowledge a gap.
- The first live Function deployment indexed after the Application Insights 3.1.2 startup correction, but a package-injected legacy empty storage setting caused MAC authentication and trigger-sync failure. The pipeline now removes only the two known injected legacy settings, verifies the five identity-based host-storage settings, and asserts the sole weekly timer before acceptance.
- The Function app's FTP basic-publishing-credentials policy is managed explicitly through pinned AzAPI because the required Web Apps API surface is not reliably exposed by the AzureRM schema. The deployer retains only resource-group Owner plus scoped storage data-plane roles needed by azd/package operations.

## Operational constraints

- `main` is the default and deployment branch.
- The repository name must remain `AdamCoulterOz.github.io` to preserve the root Pages URL.
- A project page is owned and deployed by its own repository; this repository owns discovery links only.
- The deployed scheduled-query v2 alert cannot inspect more than 48 hours, so no eight-day stale-checkpoint alert exists. A durable daily checkpoint-health watchdog remains a future enhancement if stale-run coverage is required.

## Outstanding actions and technical debt

- Add future project sites to the index only after their Pages deployment is live.
- Add a separate durable daily checkpoint-health watchdog before claiming stale-weekly-run alerts.
- Re-run and accept the protected deployment after the legacy-setting remediation; verify a real scheduled run produces archive records before claiming weekly archive output.
