# Repository Interface

## 1. Purpose

This repository owns the public root index at `https://adamcoulteroz.github.io/`. It provides a stable discovery surface for Adam Coulter's independently deployed GitHub Pages projects.

## 2. Responsibilities

Current responsibilities:

- Present Adam Coulter's name and location.
- Provide direct email, LinkedIn, and GitHub profile links.
- Link to verified project sites under the `adamcoulteroz.github.io` origin.
- Provide accessible, responsive, system-themed static presentation.
- Measure only explicit, cookie- and storage-free root-page views and allowlisted project/contact link categories.
- Deploy the `site` artifact after each `main` push.

Potential future ownership:

- Additional verified public project links.

The repository does not own any linked project's implementation, release lifecycle, routing, availability, or content.

## 3. Domain model

A project entry has an ordinal, name, concise description, and absolute-root Pages path. Entries are editorial source content in `site/index.html`; there is no runtime data store.

## 4. Public interfaces

- Root index: `https://adamcoulteroz.github.io/`
- GitHub profile: `https://github.com/AdamCoulterOz`
- LinkedIn profile: `https://www.linkedin.com/in/adamcoulter`
- Email: `mailto:ads@me.com`
- Bandwidth Calculator: `https://adamcoulteroz.github.io/bandwidth-calculator/`
- Fluent Icon Browser: `https://adamcoulteroz.github.io/fluent-icon-browser/`
- Meridian: `https://adamcoulteroz.github.io/Meridian/`
- Crawler policy: `https://adamcoulteroz.github.io/robots.txt`
- Project sitemap: `https://adamcoulteroz.github.io/sitemap.xml`

## 5. Invariants

- Every listed project path resolves to a public site before it is added.
- Root-relative assets and links remain valid from the user-site origin.
- Project descriptions remain concise and do not claim capabilities beyond the owning repository's published description.
- Metadata, structured data, sitemap entries, and visible project copy remain semantically aligned.
- The header contact links have visible labels at wider viewports and accessible names when their labels collapse at narrow viewports.
- Light and dark presentation follow `prefers-color-scheme`; no site-owned theme state, cookies, or browser storage identity is persisted.
- Browser telemetry redacts the current URL and referrer and excludes email addresses, full destination URLs, arbitrary DOM text, dependency traffic, correlation headers, and automatic exception telemetry.
- The generated Application Insights connection string is public routing configuration in the deployed artifact only; it is absent from source control and long-lived GitHub configuration.

## 6. Side effects

At runtime the site loads static files, follows links, and—when deployment has staged telemetry configuration—posts the explicitly allowlisted measurements. A `main` push invokes GitHub Actions to provision/deploy the Azure archive platform through the protected environment before packaging and deploying `site` to GitHub Pages.

## 7. Dependency boundaries

- GitHub Actions and GitHub Pages own build execution and static hosting.
- Azure owns telemetry ingestion, weekly raw-row archival, and immediate archive-failure notification. The archive worker uses a user-assigned managed identity with scoped RBAC; it does not use storage keys, SAS, or long-lived credentials.
- `infra/bootstrap/` owns the one-time OIDC/resource-group/remote-state trust root; `archive/infra/` owns the platform Terraform state and cannot change that bootstrap trust root.
- Linked repositories own their project pages and are consumed only through public URLs.
- The root index does not inspect GitHub APIs or linked project internals at runtime.

## 8. Lifecycle and execution model

The browser loads static HTML and CSS without client application startup. The optional telemetry script records one root-page view and only allowlisted click categories. A weekly .NET 10 isolated Functions 4.x timer on FC1 runs with a maximum of one instance and zero always-ready instances; it archives raw rows with deterministic create-only records and ETag-protected checkpoints. Deployment uses the protected `github-pages` environment's OIDC identity, then stages the public browser connection string only in the Pages artifact.

## 9. Anti-goals

- Becoming a dynamic portfolio CMS.
- Mirroring linked project content or coupling their deployment lifecycles to this repository.
- Adding runtime analytics, tracking, or API dependencies without explicit direction.
- Recording visitor-provided content, full URLs, referrers, cookies, persistent browser identity, or telemetry for linked child sites.
- Claiming an eight-day stale-checkpoint alert: scheduled-query v2 is limited to 48-hour lookback. A separate durable daily watchdog would be required and is not currently deployed.
- Listing repositories that do not expose a public user-facing site.

## 10. Agent guidance

- Verify a prospective project URL before adding it.
- Preserve the concise project-index structure and keep its design tokens aligned with Meridian's published Keel visual system.
- Keep visible copy, `CONTEXT.md`, and this public contract synchronized.
- Verify desktop, mobile, light, and dark projections after presentation changes.
