# AdamCoulterOz.github.io

Source for [adamcoulteroz.github.io](https://adamcoulteroz.github.io/), a concise index of Adam Coulter's published project sites, with direct email, LinkedIn, and GitHub links.

## Local preview

Serve the `site` directory with any static HTTP server, for example:

```sh
npx --yes serve site -l 8080
```

## Deployment

Every push to `main` validates the static site and archive Function. The protected `github-pages` environment then authenticates to Azure through its exact GitHub OIDC subject, provisions the platform Terraform root using remote Entra/OIDC state, deploys the weekly archive Function, writes the public Application Insights connection string only into the staged Pages artifact, and deploys that artifact to GitHub Pages.

`infra/bootstrap/` is deliberately separate and operator-run: it establishes the resource group, protected-environment OIDC trust, and Terraform-state account before a pipeline can authenticate. `archive/infra/` consumes that bootstrap and owns the platform resources.

## Telemetry and archive

The home page records one explicit, cookie- and storage-free root-page view plus allowlisted project and contact link categories. It does not record email addresses, full destination URLs, arbitrary page text, or hostile referrer/current-URL values. The browser connection string is public routing configuration, generated only during deployment and never committed.

The archive worker is a .NET 10 isolated Azure Functions 4.x timer on Flex Consumption FC1 (one 2,048 MB instance maximum and zero always-ready instances). It runs weekly, uses a user-assigned managed identity and least-privilege RBAC, and stores raw Log Analytics rows in an LRS, Entra-only archive with create-only deterministic records and ETag-protected checkpoints. Each Application Insights component has a 0.1 GB/day cap; an Azure Monitor action group immediately emails `ads@me.com` when the archive Function logs a failure.

The deployed scheduled-query v2 resource supports at most a 48-hour lookback. It therefore cannot truthfully provide an eight-day stale-checkpoint alert. A separate durable daily checkpoint-health watchdog is the next enhancement if that coverage is required; it is intentionally not part of the weekly-pull design.

Add a project only after its public page is live, then update `site/index.html`, `CONTEXT.md`, and `INTERFACE.md` together.
