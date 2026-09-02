# Azure Deployment Plan

> **Status:** Validated

Generated: 2026-09-02 (Australia/Sydney)

---

## 1. Project overview

**Goal:** Provide cookie- and storage-free Azure Application Insights usage telemetry on the static GitHub Pages home page, deploy the supporting Azure resources with Terraform through GitHub Actions, and archive relevant raw Log Analytics table rows to durable Blob Storage every week before interactive retention expires.

**Path:** Add components to an existing pure static site while preserving GitHub Pages hosting.

**Repository:** `AdamCoulterOz/AdamCoulterOz.github.io` (repository ID `1319345545`, owner ID `6822248`)

**External boundaries:**

- GitHub Pages continues to host only the static `site` artifact.
- Azure owns telemetry ingestion, interactive querying, scheduled archival, storage, and alerts.
- Linked project repositories remain outside this telemetry scope.

---

## 2. Requirements

| Attribute | Value |
|---|---|
| Classification | Production, small public personal site |
| Scale | Small, expected under 1,000 users; no always-ready compute |
| Budget | Cost-optimized, pay-as-you-go, ingestion caps and alerts |
| Subscription | Basics (`a26059bf-5574-47e9-b3e4-6a46a19d2407`) |
| Tenant | `adamcoulter.au` (`a098ad4f-34e6-46c8-aa14-e09f46c86f2e`) |
| Location | Australia East |
| Resource group | `rg-adamcoulter-github-pages-aue` |
| Data residency | Azure telemetry and archive resources remain in Australia East |
| Interactive retention | 30 days |
| Archive retention | No automatic deletion; tier raw records from Hot to Cool after 30 days and Archive after 180 days |
| Browser privacy | No cookies, local storage identity, dependency capture, correlation headers, automatic exception capture, or visitor-provided content |
| Tracked usage | Root page views plus allowlisted `project_click` and `contact_click` events |
| Excluded usage | 404 traffic and all linked child project sites |

### Policy constraints

The live subscription check found only the inherited `ASC Default` Security Center policy assignment. No enforced naming, region, storage, or networking policy was observed. Terraform will nevertheless use RBAC-only storage, TLS 1.2 or later, disabled anonymous blob access, and disabled shared-key/local-user access.

---

## 3. Components detected

| Component | Type | Technology | Path |
|---|---|---|---|
| Home page | Pure static frontend | HTML and CSS, no build framework | `site/` |
| Pages deployment | CI/CD | GitHub Actions | `.github/workflows/deploy-pages.yml` |
| Archive worker | Scheduled worker | Azure Functions runtime 4.x, .NET 10 isolated worker | `archive/` |
| Infrastructure | Platform IaC | azd with Terraform | `archive/infra/` |
| Bootstrap | One-time IaC | Terraform AzureRM and AzureAD providers | `infra/bootstrap/` |

The current workflow uploads only `site`. There is no package manager, application runtime, CSP, analytics script, or existing Azure configuration.

---

## 4. Recipe selection

**Selected:** azd with Terraform, based on the official Flex Consumption Terraform Functions template and the timer-trigger source recipe.

**Rationale:**

- The user explicitly selected Terraform.
- Azure Functions Flex Consumption requires current, tested infrastructure shapes and `FC1`; the official Terraform base template is the starting point.
- azd supplies environment and Function-package orchestration while Terraform remains the infrastructure provider.
- The existing GitHub Actions workflow can sequence `azd provision`, Function deployment, static configuration generation, and Pages deployment.
- Bootstrap identity and backend resources remain a separate one-time Terraform root because a pipeline cannot create the OIDC credential and remote backend it needs before authenticating and initializing state.

---

## 5. Architecture

### 5.1 Bootstrap and GitHub OIDC

`infra/bootstrap/` was applied by Adam's currently signed-in subscription Owner identity. It created:

1. Resource group `rg-adamcoulter-github-pages-aue` in Australia East.
2. Entra single-tenant application and service principal `AdamCoulterOz.github.io GitHub Actions`.
3. A secretless federated credential with:
   - issuer `https://token.actions.githubusercontent.com`
   - audience `api://AzureADTokenExchange`
   - subject `repo:AdamCoulterOz@6822248/AdamCoulterOz.github.io@1319345545:environment:github-pages`
4. Resource-group-scoped `Owner` for the service principal. No subscription-scoped role is granted.
5. Terraform state storage `stadamcgpiac1319345545` and private `tfstate` container.
6. `Storage Blob Data Contributor` for the service principal on the state account.

The existing protected `github-pages` environment is reused. GitHub environment variables—not secrets—will hold the client ID, tenant ID, subscription ID, resource-group name, and backend names. No client secret, certificate, storage key, or SAS token is created.

After the initial local bootstrap apply, bootstrap state was migrated into the same Entra-authenticated Blob backend under a distinct key; its post-migration plan reported no changes. Bootstrap changes remain operator-run so the pipeline cannot silently rewrite its own trust root.

### 5.2 Telemetry

Terraform will create one Log Analytics workspace and two workspace-based Application Insights components:

- Site component: receives only explicit home-page telemetry.
- Function component: receives operational telemetry for the archive worker and is excluded from the raw site archive by resource ID.

The workspace uses pay-as-you-go pricing and 30-day interactive retention. Both Application Insights components have 0.1 GB/day caps with cap notifications enabled. An Azure Monitor action group immediately emails `ads@me.com` when the archive Function logs `Archive run failed.` These controls are last-resort misuse/cost and failure guards, not the primary collection filter.

The Pages pipeline generates `site/app-insights-config.js` from the site component's connection string after Terraform succeeds. The browser connection string is public configuration by design, but the pipeline masks it and does not commit it, place it in Terraform variables, or store it as a long-lived GitHub secret.

The browser SDK:

- uses the official Application Insights JavaScript loader;
- disables cookies and persistent user/session identity;
- disables automatic AJAX/fetch dependencies, correlation headers, route tracking, page-visit timing, and automatic exception tracking;
- emits one explicit page view on `/`;
- emits only `project_click` and `contact_click` events with allowlisted destination categories and a generated `archive_event_id` UUID;
- clears current-URL and referrer fields before telemetry processing, including hostile values supplied by browser APIs;
- never sends email addresses, full destination URLs, link query strings, form data, or arbitrary DOM text.

A concise visible privacy disclosure will explain the cookie-free page-view and link-click measurement.

### 5.3 Weekly archive Function

The scheduled worker uses:

- Azure Functions runtime `~4`;
- .NET 10 isolated worker, currently the newest GA .NET version supported by Flex Consumption;
- Flex Consumption `FC1`;
- 2,048 MB instances, maximum instance count 1;
- zero always-ready instances, so it scales to zero between runs;
- a monitored timer trigger at `0 15 2 * * 1` (02:15 UTC each Monday), with `run_on_startup=false`;
- a user-assigned managed identity from the official base template;
- bounded retries and operator-controlled timer invocation or redeployment for recovery testing; it exposes no HTTP recovery endpoint.

The Function identity receives only:

- `Log Analytics Data Reader` on the workspace;
- `Storage Blob Data Contributor` on the archive account;
- the template-required Blob/Queue/Table data roles on the separate Function host/deployment storage account.

The Function queries only `AppPageViews` and `AppEvents`, filtered to the site Application Insights resource ID. Function operational rows and unrelated workspace records are excluded.

### 5.4 Checkpoint and archive protocol

The archive account `stadamcgparch1319345545` uses Standard LRS, private containers, Entra-only data access, HTTPS-only transport, blob versioning, 30-day soft delete, and no delete lifecycle. Raw and control data use separate containers.

For each source table, the control blob records a committed `_TimeReceived` high-water boundary, schema/query version, ETag, and prior run manifest. A weekly run:

1. Reads the versioned checkpoint and fixes an upper boundary of `nowUtc - 24h`.
2. Requeries from seven days before the committed boundary to catch late arrivals safely.
3. Filters by site resource ID and orders by `_TimeReceived` plus a stable record identity (`AppPageViews.Id` or the custom `archive_event_id`).
4. Splits time windows recursively before a query can exceed 500,000 rows, about 100 MiB, or the 10-minute API limit; a limit response is a failed batch, never an empty success.
5. Stores every returned Log Analytics row plus a small archive envelope at a deterministic, create-only blob path derived from table, date, resource ID, and SHA-256 identity.
6. Treats an existing identical blob as a successful replay, while a hash mismatch fails the run.
7. Writes a per-table/run manifest containing interval, schema, row count, hashes, query version, and next boundary.
8. Advances the checkpoint only after all record blobs and the manifest are durable, using an ETag precondition. A crash can cause replay but cannot create an acknowledged gap.

“Raw” means every column returned by the relevant Log Analytics table row, not the original browser SDK transmission envelope. The archive retains the masked/derived location and client fields present in those rows indefinitely while the storage account exists. WORM immutability is intentionally not enabled because no irreversible compliance retention period was requested.

### 5.5 Supporting resources

| Component | Azure service | SKU/configuration |
|---|---|---|
| Site telemetry query store | Log Analytics | Pay-as-you-go, 30-day retention |
| Site usage ingestion | Application Insights | Workspace-based, browser local auth enabled |
| Worker operations | Application Insights | Separate workspace-based component |
| Scheduled archive | Azure Functions | Flex Consumption FC1, Functions 4.x, .NET 10 isolated, zero always-ready |
| Function host/deployment | StorageV2 | Standard LRS, Entra-only |
| Long-term archive | Blob Storage | StorageV2 Standard LRS, versioning/soft delete, Hot/Cool/Archive lifecycle without deletion |
| Terraform state | Blob Storage | StorageV2 Standard LRS, Entra-only, versioning/soft delete |
| Runtime identity | User-assigned managed identity | Least-privilege resource/data roles |
| Pipeline identity | Entra application service principal | GitHub OIDC; Owner only on the project resource group |
| Failure/cost notification | Azure Monitor action group | Immediate email to `ads@me.com` for archive Function failure; Insights cap notifications enabled |

---

## 6. Provisioning limit checklist

`Microsoft.Quota` and every required resource provider are registered. The quota API is reachable but returns only a non-applicable wildcard result for this deployment, so the documented Azure Resource Graph fallback remains the applicable capacity evidence. A fresh provider/quota preflight remains required immediately before provisioning.

| Resource type | Number to deploy | Total after deployment | Limit/quota | Evidence and result |
|---|---:|---:|---:|---|
| `Microsoft.Storage/storageAccounts` | 3 | 32 in Australia East | 250 per subscription/region by default | ARG current count 29 + 3; within limit |
| `Microsoft.Web/serverfarms` FC1 | 1 | 29 FC1 plans in Australia East | 512,000 MB/250 cores simultaneous regional memory | ARG current count 28; planned app uses at most one 2,048 MB instance and zero when idle; within default capacity unless unrelated apps concurrently exhaust the shared quota |
| `Microsoft.Web/sites` Function App | 1 | 29 Function/web apps observed in Australia East | One app per FC1 plan; ARM resource-group limits apply | Dedicated plan, new resource group; within limit |
| `Microsoft.OperationalInsights/workspaces` | 1 | 29 in Australia East | No subscription limit for current pay-as-you-go tier beyond ARM limits | ARG current count 28; within limit |
| `Microsoft.Insights/components` | 2 | 30 in Australia East | ARM resource-group limits; telemetry cap 100 GB/day per component by default | ARG current count 28; new resource group; within limit |
| `Microsoft.ManagedIdentity/userAssignedIdentities` | 1 | 1 in new resource group | ARM resource-group limits | New resource group; within limit |
| `Microsoft.Authorization/roleAssignments` | Bounded set for pipeline and Function | Well below subscription/resource-group role-assignment limits | Azure RBAC service limit | New resource group; within limit |

**Status:** Capacity is sufficient for the planned small deployment. The Australia East live catalog supports `dotnet-isolated` 10 on FC1 at 2,048 MB with a minimum maximum-instance setting of 1. Re-run the provider/quota preflight and stop before provisioning if live capacity has changed.

---

## 7. Pipeline and execution sequence

### One-time bootstrap

1. Validate Terraform formatting and configuration for `infra/bootstrap/`.
2. Apply bootstrap locally with Adam's signed-in tenant/subscription Owner identity.
3. Verify the app, service principal, exact federated subject, resource-group-only Owner assignment, state data-plane assignment, and absence of passwords/certificates.
4. Migrate bootstrap state to its remote backend key.
5. Set GitHub `github-pages` environment variables for the non-secret Azure IDs and backend coordinates.
6. Run a minimal OIDC workflow assertion that can read the target resource group and cannot access a control resource outside it.

### Pull requests

- Run static HTML/JSON-LD checks, Function unit tests, Terraform `fmt -check` and `validate`, and site link checks.
- Do not grant Azure OIDC or remote-state access to pull-request code, especially forked pull requests.

### Main and manual deployments

The protected `github-pages` environment job receives `id-token: write` and uses a single concurrency group:

1. Check out the exact commit.
2. Authenticate to Azure with `azure/login` and the environment-scoped federated identity.
3. Select the fixed azd production environment and explicit subscription/location.
4. Run the fresh quota/provider preflight.
5. Run `azd provision --no-prompt` so Terraform plans/applies the platform resources and RBAC.
6. Wait for RBAC propagation, then deploy the tested Function package with `azd deploy --no-prompt`.
7. Read and mask the public site Application Insights connection string.
8. Generate `site/app-insights-config.js` only in the staged Pages artifact.
9. Re-run static and telemetry-configuration checks.
10. Upload and deploy the Pages artifact.
11. Run bounded post-deployment telemetry and archive smoke checks without manufacturing a production visitor identity.

Terraform/Azure provisioning must succeed before Pages is changed, preventing a site release that references missing telemetry infrastructure.

---

## 8. Validation and acceptance

### Identity and infrastructure

- The Entra application is single-tenant, has exactly the approved GitHub environment federated credential, and has no client secrets or certificates.
- The service principal is Owner only on `rg-adamcoulter-github-pages-aue`, plus state-container data access; it has no subscription-scoped assignment.
- Terraform remote state uses Entra/OIDC authentication, locking, versioning, and soft delete.
- Terraform plan contains only the named Australia East resources and expected GitHub/Entra bootstrap objects.
- All storage denies anonymous access and shared-key/local-user authentication.
- Flex has no always-ready instances, maximum scale 1, Functions runtime 4.x, and .NET 10 isolated.

### Browser and privacy

- Desktop, 320 px mobile, light, dark, and keyboard focus checks pass for the contact links and privacy disclosure.
- The page remains fully usable with JavaScript disabled.
- No cookies or local-storage identity are created.
- Network inspection shows only the approved SDK load and telemetry posts; no automatic dependency/exception traffic is sent.
- One page load produces one site `AppPageViews` row; each allowlisted link click produces one correctly categorized `AppEvents` row with an `archive_event_id`.
- No email address, query string, form value, or arbitrary DOM text appears in telemetry.

### Archive correctness

- Seeded page-view, project-click, and contact-click rows are archived with all source columns and reconcile to KQL counts for the committed window.
- A forced failure after record writes leaves the checkpoint unchanged; rerun reuses identical record blobs and then advances the checkpoint once.
- A newly received row with an older `TimeGenerated` is captured through `_TimeReceived` and the seven-day overlap.
- A simulated oversized interval splits without truncation.
- A competing checkpoint update fails its ETag precondition rather than overwriting state.
- Unauthenticated archive reads fail; blob versions, soft-deleted objects, and lifecycle tiers are observable.
- A failed weekly run triggers the action-group notification. An eight-day stale-checkpoint notification is not implemented: scheduled-query v2 has a maximum 48-hour lookback, and no separate durable daily checkpoint-health watchdog has been introduced for this weekly-pull design.

### Required workflow handoff

The pre-bootstrap validation workflow has advanced this plan to `Validated`. Apply and accept bootstrap first; the platform live plan must then run and be accepted before platform provisioning.

---

## 9. Files to generate or update

| File/path | Purpose | Status |
|---|---|---|
| `.azure/deployment-plan.md` | Source-of-truth plan | Complete; Validated |
| `infra/bootstrap/` | One-time RG, state, Entra app/SP/OIDC, and bootstrap RBAC Terraform | Implemented; applied; post-apply/post-migration plan has no changes |
| `archive/azure.yaml` | azd service and Terraform orchestration | Implemented; not provisioned |
| `archive/infra/` | Platform Terraform derived from the official Flex Functions template | Implemented; not applied |
| `archive/src/` | .NET 10 isolated weekly timer only; no HTTP recovery endpoint | Implemented; not deployed |
| `archive/*.csproj` | Pinned Function/Azure SDK dependencies and isolated-worker build | Implemented; validation pending |
| `archive/tests/` | Cursor, retry, batching, idempotency, and fixture tests | Implemented; validation pending |
| `site/index.html` | Loader/config references, explicit page/click telemetry, privacy copy | Implemented; validation pending |
| `site/app-insights.js` | First-party allowlisted telemetry initialization and events | Implemented; validation pending |
| `.github/workflows/deploy-pages.yml` | OIDC, azd/Terraform, Function, generated config, Pages sequencing | Implemented; not run against Azure |
| `CONTEXT.md` | Current architecture/invariants | Implemented |
| `INTERFACE.md` | Public telemetry/privacy behavior and external boundaries | Implemented |
| `HISTORY.md` | Architectural and lifecycle decision record | Implemented |
| `README.md` | Bootstrap, local validation, deployment, recovery | Implemented |

---

## 10. Execution checklist

### Phase 1: planning

- [x] Analyze workspace.
- [x] Gather classification, scale, budget, privacy, retention, subscription, tenant, region, Terraform, OIDC, resource-group scope, and scale-to-zero requirements.
- [x] Scan codebase and deployment workflow.
- [x] Select azd with Terraform and official Flex timer recipe.
- [x] Confirm Australia East Flex availability and latest GA runtime/language versions.
- [x] Inspect subscription policy assignments and current resource counts.
- [x] Complete provisioning-limit fallback assessment without unresolved cells.
- [x] Plan architecture, identity, pipeline, archive protocol, and acceptance evidence.
- [x] User approves this complete plan with .NET 10 isolated, LRS archive storage, and RBAC-only Function access.

### Phase 2: execution

- [x] Research and verify current provider/action/SDK versions and official template revisions.
- [x] Register required Azure resource providers and re-run quotas; API wildcard result leaves ARG fallback applicable.
- [x] Generate, apply, and validate bootstrap Terraform; its post-migration remote plan has no changes.
- [x] Prove the bootstrap OIDC trust and RBAC scope.
- [x] Initialize the official Terraform Flex Functions template non-interactively.
- [x] Apply the timer recipe and archive implementation without replacing template security/lifecycle invariants.
- [x] Add telemetry and documentation changes.
- [x] Add the protected pipeline sequence.
- [x] Run focused unit, static, rendered, Terraform, identity, and failure-path checks.
- [x] Update status to `Ready for Validation` after owner acceptance.

### Phase 3: validation and deployment

- [x] Invoke pre-bootstrap Azure validation and populate validation proof below.
- [x] Update status to `Validated` after all required pre-deployment proof passed.
- [ ] Invoke the Azure deployment workflow.
- [ ] Confirm live Pages behavior, telemetry ingestion, weekly Function state, raw archive output, alert routing, and pipeline state.
- [ ] Update status to `Deployed`.

---

## 11. Validation proof

Azure validation is complete. The platform live plan depended on the bootstrap-owned resource group and remote backend; after bootstrap it ran remotely and was accepted before platform provisioning.

| Check | Command or evidence | Result | Timestamp |
|---|---|---|---|
| Function build | .NET 10 Release build completed with 0 warnings | Pass | 2026-09-02 |
| Function tests | 15/15 tests passed | Pass | 2026-09-02 |
| Terraform static validation | Both Terraform roots passed `fmt`, `init -backend=false`, and `validate` | Pass | 2026-09-02 |
| Subscription identity | Current Basics subscription ID and tenant/user match the approved subscription and tenant | Pass | 2026-09-02 |
| Providers and quota API | `Microsoft.Quota` and all required providers are Registered; quota API is accessible, with only a non-applicable wildcard result | Pass; ARG fallback retained | 2026-09-02 |
| Flex catalog | Australia East catalog supports `dotnet-isolated` 10, FC1, 2,048 MB, and a minimum maximum-instance count of 1 | Pass | 2026-09-02 |
| Policy | Only the inherited `ASC Default` policy assignment is present | Pass | 2026-09-02 |
| Azure Resource Graph capacity | Counts remain 29 storage accounts and 28 plans, sites, workspaces, and components; target resource group/application are absent and all three storage-account names are available | Pass | 2026-09-02 |
| Bootstrap pre-apply plan | Approved tenant/subscription plan: 0 add, 1 change, 0 destroy (the exact FIC subject correction) | Pass | 2026-09-02 |
| Bootstrap live apply | Applied with 0 add, 1 change, 0 destroy; app, client, and service-principal IDs and role scopes remained unchanged | Pass | 2026-09-02 |
| Bootstrap identity and RBAC | Entra application/service principal and corrected exact `github-pages` environment FIC present; Owner is scoped only to the resource group and Storage Blob Data Contributor only to the state account | Pass | 2026-09-02 |
| Bootstrap remote state | Bootstrap state migrated to the remote backend; post-apply/post-migration plan reported no changes | Pass | 2026-09-02 |
| Static RBAC verification | Required scoped role assignments and absence of broader credentials/roles verified | Pass | 2026-09-02 |
| azd availability | `azd` 1.32 installed | Pass | 2026-09-02 |
| Browser/static checks | JavaScript unit and syntax, workflow YAML, and diff checks passed | Pass | 2026-09-02 |
| Rendered site checks | 1440 px and 320 px light/dark/focus projections passed with no storage or overflow | Pass | 2026-09-02 |
| Platform remote live plan | Bootstrap-owned resource group and backend resolved; accepted plan: 25 add, 0 change, 0 destroy | Pass | 2026-09-02 |
| Protected GitHub pipeline deployment | Platform provisioning, Pages deployment, and post-deployment verification remain pending | Pending | 2026-09-02 |

---

## 12. Next step

Run the protected GitHub pipeline for platform provisioning and deployment. Platform deployment and live verification remain pending; do not mark this plan `Deployed` until their required evidence is complete.
