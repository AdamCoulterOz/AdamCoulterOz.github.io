# One-time Azure bootstrap

This Terraform root creates the trust root for the `github-pages` GitHub
environment. It is operator-run exactly once from a signed-in Azure identity;
GitHub Actions must not be able to rewrite it.

It creates only the approved resources and identities:

- `rg-adamcoulter-github-pages-aue` in Australia East;
- state account `stadamcgpiac1319345545` and its private `tfstate` container;
- the single-tenant `AdamCoulterOz.github.io GitHub Actions` application and
  service principal, with no password, certificate, or API permissions;
- exactly one GitHub federated credential, scoped to
  `repo:AdamCoulterOz@6822248/AdamCoulterOz.github.io@1319345545:environment:github-pages`;
- `Owner` for that service principal at the new resource group only, and
  `Storage Blob Data Contributor` on the state account;
- `Storage Blob Data Contributor` for the one signed-in bootstrap operator on
  the state account, so the state can be migrated and inspected without
  enabling shared keys.

The storage account is Standard LRS, private, HTTPS-only, TLS 1.2+, Entra-only
for data access, versioned, and protected by 30-day blob and container soft
delete. Public networking remains enabled because the protected GitHub-hosted
runner requires a reachable Azure Storage endpoint; it does not make blobs
public and anonymous access is disabled.

## Preconditions

Run the bootstrap as the approved tenant's currently signed-in subscription
Owner. The identity also needs an Entra directory role capable of registering
an application and federated credential (for example, Application
Administrator). The root verifies the active tenant and subscription before
creating anything.

```zsh
cd infra/bootstrap
az login --tenant a098ad4f-34e6-46c8-aa14-e09f46c86f2e
az account set --subscription a26059bf-5574-47e9-b3e4-6a46a19d2407
az account show --query '{tenantId:tenantId, subscriptionId:id, user:user.name}' --output json
terraform init -backend=false
terraform fmt -check -recursive
terraform validate
terraform plan -out=bootstrap.tfplan
```

Review the plan before applying. It must contain the resource group, one state
storage account/container, one application/service principal/federated
credential, and the three role assignments documented above. Stop if the
active tenant/subscription output or plan differs.

On its first creation only, the plan waits a bounded 10 minutes after assigning
the bootstrap operator `Storage Blob Data Contributor` role before creating
the private state container. This accommodates Azure RBAC data-plane
propagation while shared-key access remains disabled; subsequent applies do
not repeat the wait.

```zsh
terraform apply bootstrap.tfplan
```

## Migrate from local to Entra-authenticated remote state

After a successful local apply, copy the non-secret example and migrate. The
distinct `bootstrap/terraform.tfstate` key prevents a future platform root
from sharing this state object.

```zsh
cp backend.tf.example backend.tf
terraform init -migrate-state
terraform state pull >/dev/null
```

Do not use `ARM_ACCESS_KEY`, a SAS token, an application password, or a
certificate. The Terraform backend obtains a token from the signed-in Azure
CLI session and accesses the private blob through the data-plane RBAC created
by this root.

## GitHub environment variables after migration

Set the following as **variables**, not GitHub secrets, on the existing
`github-pages` environment. They are identifiers, not credentials:

| Variable | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | `terraform output -raw github_actions_client_id` |
| `AZURE_TENANT_ID` | `a098ad4f-34e6-46c8-aa14-e09f46c86f2e` |
| `AZURE_SUBSCRIPTION_ID` | `a26059bf-5574-47e9-b3e4-6a46a19d2407` |
| `AZURE_RESOURCE_GROUP` | `rg-adamcoulter-github-pages-aue` |
| `AZURE_ENV_NAME` | `prod` |
| `TFSTATE_RESOURCE_GROUP` | `rg-adamcoulter-github-pages-aue` |
| `TFSTATE_STORAGE_ACCOUNT` | `stadamcgpiac1319345545` |
| `TFSTATE_CONTAINER` | `tfstate` |

The later platform Terraform root uses the same account and container but its
fixed, separate backend key is `platform/terraform.tfstate`. It must never use
or overwrite this bootstrap root's `bootstrap/terraform.tfstate` key.

No GitHub configuration is changed by this root. A later workflow must request
`id-token: write`, target the protected `github-pages` environment, and use
the exact OIDC subject represented above.
