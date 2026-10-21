#!/usr/bin/env bash
# Publishes SMDatabase to the deployment's Azure SQL server, signed in as the pipeline's own
# Entra identity, with BlockOnPossibleDataLoss on. Writes the deploy report first, so a change that
# would lose data is on record before the publish refuses it.
#
#   publish-schema.sh before|after
#
# "before" does nothing when the server does not exist yet (the first deploy) and says so in
# its step output, published=false. The app host's local publish turns data-loss protection
# off; this is the path that never does.
set -euo pipefail

phase="$1"
server=$(az sql server list -g "$Azure__ResourceGroup" --query "[0].fullyQualifiedDomainName" -o tsv)

if [ -z "$server" ]; then
  if [ "$phase" = "before" ]; then
    echo "No SQL server yet: this is the first deploy, and the schema follows it."
    echo "published=false" >> "$GITHUB_OUTPUT"
    exit 0
  fi
  echo "::error::No SQL server in $Azure__ResourceGroup after the deploy."
  exit 1
fi

# The runner's address, allowed for the length of this script and no longer.
runner_ip=$(curl -s https://api.ipify.org)
rule="deploy-${GITHUB_RUN_ID}-${phase}"
server_name="${server%%.*}"
az sql server firewall-rule create -g "$Azure__ResourceGroup" -s "$server_name" -n "$rule" \
  --start-ip-address "$runner_ip" --end-ip-address "$runner_ip" >/dev/null
# Aspire makes its own managed identity (sql-admin-...) the server's only Entra admin, and uses
# it to give each app its database user. The pipeline is nobody on the server, so it becomes the
# admin for the length of this script, and Aspire's goes back on the way out. A run that dies
# before then is put right by the next deploy, which sets Aspire's admin again.
admin_login=$(az sql server ad-admin list -g "$Azure__ResourceGroup" -s "$server_name" --query "[0].login" -o tsv)
admin_sid=$(az sql server ad-admin list -g "$Azure__ResourceGroup" -s "$server_name" --query "[0].sid" -o tsv)
pipeline_app_id=$(az account show --query user.name -o tsv)

cleanup() {
  if [ -n "$admin_sid" ]; then
    az sql server ad-admin create -g "$Azure__ResourceGroup" -s "$server_name" \
      --display-name "$admin_login" --object-id "$admin_sid" >/dev/null
  fi
  az sql server firewall-rule delete -g "$Azure__ResourceGroup" -s "$server_name" -n "$rule" >/dev/null
}
trap cleanup EXIT

az sql server ad-admin create -g "$Azure__ResourceGroup" -s "$server_name" \
  --display-name "deploy-pipeline" --object-id "$pipeline_app_id" >/dev/null

token=$(az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv)
target="Server=tcp:${server},1433;Database=SMDatabase;Encrypt=True;TrustServerCertificate=False"

# A new admin can take a little while to be accepted at sign-in.
for attempt in 1 2 3 4; do
  if sqlpackage /Action:DeployReport /SourceFile:SMDatabase/bin/Release/SMDatabase.dacpac \
      "/TargetConnectionString:$target" "/AccessToken:$token" \
      "/OutputPath:schema-$phase.xml" /p:BlockOnPossibleDataLoss=true; then
    break
  fi
  if [ "$attempt" = 4 ]; then exit 1; fi
  echo "Sign-in refused; trying again in 20 seconds."
  sleep 20
done

sqlpackage /Action:Publish /SourceFile:SMDatabase/bin/Release/SMDatabase.dacpac \
  "/TargetConnectionString:$target" "/AccessToken:$token" /p:BlockOnPossibleDataLoss=true

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  echo "published=true" >> "$GITHUB_OUTPUT"
fi
