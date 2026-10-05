#!/usr/bin/env bash
# Publishes SMDatabase to the staging Azure SQL server, signed in as the pipeline's own Entra
# identity, with BlockOnPossibleDataLoss on. Writes the deploy report first, so a change that
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
trap 'az sql server firewall-rule delete -g "$Azure__ResourceGroup" -s "$server_name" -n "$rule" >/dev/null' EXIT

token=$(az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv)
target="Server=tcp:${server},1433;Database=SMDatabase;Encrypt=True;TrustServerCertificate=False"

sqlpackage /Action:DeployReport /SourceFile:SMDatabase/bin/Release/SMDatabase.dacpac \
  "/TargetConnectionString:$target" "/AccessToken:$token" \
  "/OutputPath:schema-$phase.xml" /p:BlockOnPossibleDataLoss=true

sqlpackage /Action:Publish /SourceFile:SMDatabase/bin/Release/SMDatabase.dacpac \
  "/TargetConnectionString:$target" "/AccessToken:$token" /p:BlockOnPossibleDataLoss=true

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  echo "published=true" >> "$GITHUB_OUTPUT"
fi
