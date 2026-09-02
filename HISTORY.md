# Repository History

## 2026-09-02: Add privacy-bounded telemetry and weekly raw archive design

- Added explicit cookie- and storage-free root-page and allowlisted link-category telemetry, with URL/referrer redaction and a connection string generated only in the staged Pages artifact.
- Added a protected-environment GitHub OIDC deployment path, separate bootstrap and platform Terraform state roots, and an FC1 scale-to-zero .NET 10 archive worker using UAMI/RBAC and LRS create-only checkpointed storage.
- Recorded 0.1 GB/day ingestion caps and immediate archive-failure email notification. The scheduled-query v2 48-hour limit means an eight-day stale-run alert is not deployed; a durable daily watchdog is a future enhancement.

## 2026-09-02: Add direct contact and profile links

- Replaced the standalone GitHub header link with accessible email, LinkedIn, and GitHub links.
- Added the public destinations to the Person structured data and repository interface.

## 2026-08-01: Establish the personal project index

- Created the root `AdamCoulterOz.github.io` user site as a static, responsive project index.
- Added the currently published Bandwidth Calculator, Fluent Icon Browser, and Meridian Pages sites.
- Adopted a restrained editorial list with system-following light and dark themes and no runtime framework.
- Added a single-job GitHub Pages deployment workflow for the `site` directory.

## 2026-08-02: Align the index with Meridian

- Replaced the index's standalone visual tokens with Meridian's Keel typography, semantic colours, surfaces, spacing, radii, focus treatment, and motion.
- Preserved the root site's concise project-index structure while making it visually coherent with the Meridian project site.

## 2026-08-02: Publish a complete discovery surface

- Added canonical crawler directives, keywords, social metadata, and Schema.org profile/project data without introducing a JavaScript dependency.
- Expanded the root sitemap to enumerate the verified Bandwidth Calculator, Fluent Icon Browser, and Meridian child sites.

## 2026-08-02: Establish Google Search Console ownership

- Added a durable root verification file for the `https://adamcoulteroz.github.io/` URL-prefix property.
- Kept ownership proof in the published static artifact so sitemap submission and indexing diagnostics remain available to the verified owner.
