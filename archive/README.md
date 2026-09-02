# GitHub Pages telemetry archive Function

This directory starts from the official Azure Functions Terraform azd quickstart at commit `38f0b0a09626cca7bce678ab9b4d5092f7d9c219` and replaces its anonymous HTTP examples with the production archive worker.

## Runtime and access boundary

- Azure Functions `~4`, .NET 10 isolated worker, Linux Flex Consumption (`FC1`).
- 2,048 MB, maximum one instance, no `always_ready` configuration: the app scales to zero between timer executions.
- The monitored timer runs at `02:15 UTC` every Monday (`0 15 2 * * 1`) with five fixed-delay retries. It has no HTTP endpoints and no Function-key recovery path; recovery is an operator-controlled timer invocation/redeployment workflow, with health visible through Function telemetry and the archive checkpoint.
- The user-assigned managed identity is explicitly selected by client ID. It is the only runtime credential for Log Analytics, Blob archive storage, Function host storage, and Application Insights ingestion. There are no storage/service secrets, storage connection strings, storage keys, or SAS tokens. The Function Application Insights connection string is non-secret routing metadata and is paired with Entra authentication.

## Archive protocol

The worker reads only `AppPageViews` and `AppEvents` whose `_ResourceId` matches the site Application Insights component. It preserves every returned source column under `source_columns` and adds a deterministic archive envelope.

Each table has an ETag-protected checkpoint in the private `control` container. Checkpoints and manifests contain explicit archive schema and query versions. A run fixes its upper bound at `now - 24h`, queries seven days before the last committed `_TimeReceived` boundary to pick up late arrivals, and normally caps query intervals at seven days. Queries use `take 500001` as a conservative 500,000-row safety guard: any result at or above 500,000 rows recursively bisects before any row is archived. A partial Azure Monitor response only bisects when its error explicitly confirms a row, response-size, or query-time limit; permissions, KQL/schema diagnostics, other partial failures, and any unresolved minimum interval are fatal rather than treated as an empty/successful batch.

Raw blob paths derive only from table, received date, source resource, and source record identity. They are create-only; if an existing identity path has different serialized source content, the run fails instead of silently creating a second record. The manifest is written before the ETag-conditional checkpoint update, so a failure only causes safe replay.

`AppPageViews.Id` is its source identity. `AppEvents` must carry the browser-generated `archive_event_id` custom property; records without it are intentionally excluded rather than receiving an invented identity.

## Infrastructure

`infra/` consumes the bootstrap-created resource group; it never creates a resource group or GitHub/Entra trust. It creates one 30-day Log Analytics workspace, a browser-ingestion site Application Insights component, a separate Entra-only Function component, LRS host/deployment storage, and the private LRS archive account `stadamcgparch1319345545`.

Archive storage has versioning, 30-day blob/container soft delete, and an archive-only lifecycle: raw blobs move Hot → Cool after 30 days and Cool → Archive after 180 days. There is no lifecycle deletion. Anonymous access, shared key access, and local users are disabled on both storage accounts.

The required UAMI roles are Log Analytics Data Reader on the workspace, Storage Blob Data Contributor on the archive account, Monitoring Metrics Publisher on the Function Insights component, and the documented Blob Data Owner, Queue Data Contributor, and Table Data Contributor roles on Function host storage. Terraform waits 30 seconds for the newly created Entra principal and then a bounded ten minutes for relevant RBAC propagation before it creates containers or the Function app; Azure documents role propagation can take that long.

The deployment service principal also needs scoped Blob Data Contributor access on the Function host and archive accounts for azd/package container operations; its control-plane Owner assignment remains limited to the dedicated resource group. The workflow removes only package-injected legacy storage settings after deployment, then verifies identity-based host-storage configuration and the sole timer trigger. The first live deployment exposed this compatibility issue, so scheduled archive output remains unproven until a real weekly run.

Both Insights components have a conservative `0.1 GB/day` ingestion cap with cap notifications enabled. A Monitor action group emails `ads@me.com` when the Function logs `Archive run failed.` The current scheduled-query alert API has a maximum 48-hour lookback, so an accurate no-success-for-eight-days alert is not expressible by this Terraform resource. The smallest safe alternative is to keep the deployed immediate-failure alert and add a separate durable daily checkpoint-health producer before claiming eight-day coverage.

The Terraform pins are `hashicorp/azurerm 5.3.0` and `hashicorp/time 0.14.1`, the latest stable releases returned by the Terraform Registry during implementation. The .NET package pins were similarly checked against NuGet: Functions Worker `2.52.0`, Worker SDK `2.1.0`, Timer `4.3.1`, Azure Identity `1.21.0`, Azure Monitor Query `1.7.1`, and Azure Storage Blobs `12.29.2`.

## Clean-runner azd sequence

After GitHub OIDC has authenticated Azure CLI, create/select the azd environment explicitly. `azd provision --environment prod` expects that environment to already exist.

Before `azd provision`, initialise the distinct platform Terraform state. The protected GitHub environment must supply `TFSTATE_RESOURCE_GROUP=rg-adamcoulter-github-pages-aue`, `TFSTATE_STORAGE_ACCOUNT=stadamcgpiac1319345545`, and `TFSTATE_CONTAINER=tfstate`; the fixed state key is `platform/terraform.tfstate`. Export the existing OIDC values as `ARM_USE_OIDC=true`, `ARM_USE_AZUREAD=true`, `ARM_CLIENT_ID`, `ARM_TENANT_ID`, and `ARM_SUBSCRIPTION_ID`.

```shell
cd archive
terraform -chdir=infra init -reconfigure \
  -backend-config="resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -backend-config="storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -backend-config="container_name=$TFSTATE_CONTAINER" \
  -backend-config="key=platform/terraform.tfstate" \
  -backend-config="use_azuread_auth=true" \
  -backend-config="use_oidc=true"
azd env new prod --subscription "$AZURE_SUBSCRIPTION_ID" --location australiaeast --no-prompt
azd env set AZURE_RESOURCE_GROUP rg-adamcoulter-github-pages-aue --environment prod
azd provision --environment prod --no-prompt
azd deploy archive --environment prod --no-prompt
```

The provisioning outputs deliberately expose `AZURE_RESOURCE_GROUP` and `SITE_APPLICATIONINSIGHTS_NAME`, but never expose the browser connection string. The pipeline reads the site component after provisioning and writes the staged static configuration.

## Local validation

```shell
dotnet restore http.sln
dotnet build http.sln --no-restore
dotnet test tests/Archive.Functions.Tests.csproj --no-restore
terraform -chdir=infra fmt -check -recursive
terraform -chdir=infra init -backend=false
terraform -chdir=infra validate
```
