#!/usr/bin/env bash
# Merge the per-project Cobertura files and render the HTML and Markdown reports.
#   report.sh <results-dir> <out-dir>
# Writes <out-dir>/merged.cobertura.xml (dotnet-coverage) and <out-dir>/report/{index.html,SummaryGithub.md} (ReportGenerator).
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="${1:?results dir}"; out="${2:?output dir}"
mkdir -p "$out"
files=()
while IFS= read -r f; do files+=("$f"); done < <(find "$results" -name '*.cobertura.xml' -type f | sort)
[ "${#files[@]}" -gt 0 ] || { echo "No Cobertura files under $results" >&2; exit 1; }
dotnet-coverage merge --output "$out/merged.cobertura.xml" --output-format cobertura --remove-input-files false "${files[@]}"
args=()
while IFS= read -r line; do case "$line" in ''|'#'*) ;; *) args+=("$line") ;; esac; done < "$here/reportgenerator.config"
reportgenerator "-reports:$out/merged.cobertura.xml" "-targetdir:$out/report" "${args[@]}"
