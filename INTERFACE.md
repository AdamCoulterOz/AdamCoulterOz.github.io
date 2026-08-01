# Repository Interface

## 1. Purpose

This repository owns the public root index at `https://adamcoulteroz.github.io/`. It provides a stable discovery surface for Adam Coulter's independently deployed GitHub Pages projects.

## 2. Responsibilities

Current responsibilities:

- Present Adam Coulter's name and location.
- Link to verified project sites under the `adamcoulteroz.github.io` origin.
- Provide accessible, responsive, system-themed static presentation.
- Deploy the `site` artifact after each `main` push.

Potential future ownership:

- Additional verified public project links and concise personal contact links.

The repository does not own any linked project's implementation, release lifecycle, routing, availability, or content.

## 3. Domain model

A project entry has an ordinal, name, concise description, and absolute-root Pages path. Entries are editorial source content in `site/index.html`; there is no runtime data store.

## 4. Public interfaces

- Root index: `https://adamcoulteroz.github.io/`
- GitHub profile: `https://github.com/AdamCoulterOz`
- Bandwidth Calculator: `https://adamcoulteroz.github.io/bandwidth-calculator/`
- Fluent Icon Browser: `https://adamcoulteroz.github.io/fluent-icon-browser/`
- Meridian: `https://adamcoulteroz.github.io/Meridian/`

## 5. Invariants

- Every listed project path resolves to a public site before it is added.
- Root-relative assets and links remain valid from the user-site origin.
- Project descriptions remain concise and do not claim capabilities beyond the owning repository's published description.
- Light and dark presentation follow `prefers-color-scheme`; no site-owned theme state is persisted.

## 6. Side effects

At runtime the site loads static files and follows links only. A `main` push invokes GitHub Actions to package `site` and deploy it to GitHub Pages.

## 7. Dependency boundaries

- GitHub Actions and GitHub Pages own build execution and static hosting.
- Linked repositories own their project pages and are consumed only through public URLs.
- The root index does not inspect GitHub APIs or linked project internals at runtime.

## 8. Lifecycle and execution model

The browser loads static HTML and CSS without client application startup. Deployment is a single Pages workflow job triggered by a `main` push or manual dispatch.

## 9. Anti-goals

- Becoming a dynamic portfolio CMS.
- Mirroring linked project content or coupling their deployment lifecycles to this repository.
- Adding runtime analytics, tracking, or API dependencies without explicit direction.
- Listing repositories that do not expose a public user-facing site.

## 10. Agent guidance

- Verify a prospective project URL before adding it.
- Preserve the open editorial list rather than converting it to a generic card grid.
- Keep visible copy, `CONTEXT.md`, and this public contract synchronized.
- Verify desktop, mobile, light, and dark projections after presentation changes.
