# Card signing test vectors

Files in this folder are compared byte for byte, so git never rewrites them (`tests/vectors/** -text` in `.gitattributes`).
Payload files have no trailing newline.

| File | Source | Upstream issue |
| --- | --- | --- |
| `jcs/numbers.txt` | RFC 8785 Appendix B, "Number Serialization Samples" (https://datatracker.ietf.org/doc/html/rfc8785#appendix-B), one `<IEEE-754 hex> <expected>` line per row; NaN and Infinity rows mean "must be rejected" | none |
| `jcs/arrays.input.json`, `jcs/arrays.output.json` | cyberphone/json-canonicalization `testdata/input/arrays.json` and `testdata/output/arrays.json` at commit 32a48195b9b20fe67b1795e39e4509f14d860b45 (Apache-2.0) | none |
| `jcs/french.input.json`, `jcs/french.output.json` | same repository and commit, `french.json` | none |
| `jcs/structures.input.json`, `jcs/structures.output.json` | same repository and commit, `structures.json` | none |
| `jcs/unicode.input.json`, `jcs/unicode.output.json` | same repository and commit, `unicode.json` | none |
| `jcs/values.input.json`, `jcs/values.output.json` | same repository and commit, `values.json` | none |
| `jcs/weird.input.json`, `jcs/weird.output.json` | same repository and commit, `weird.json` | none |
