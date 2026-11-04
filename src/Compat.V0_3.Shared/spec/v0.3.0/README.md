# Vendored A2A v0.3.0 reference files

Reference data for the 0.3 compatibility packages. These files are not edited and not compiled except `a2a.proto`
(compiled `Access="Internal"` into the compat packages, see `docs/superpowers/plans/2026-11-03-sdk-15-compat-v03.md` Task 14/17).

- Spec version: A2A v0.3.0 (release tag `v0.3.0`)
- Repository: https://github.com/a2aproject/A2A
- Tag object: 210f03d426e2f2fa92000e14ef0de3b7ba15aee5 (annotated tag)
- Tag commit: 8d57eba286de756176892518a8fc39b0ac2ccefb
- Fetched: 2026-11-04

| File | Source path at the tag | SHA-256 |
|---|---|---|
| `a2a.proto` | `specification/grpc/a2a.proto` | 012d8072227377f7e0d2dbafc13f85359c566bcb3bb5c2621d72e667b1eb3f40 |
| `a2a.json` | `specification/json/a2a.json` | 97d6e2435336836cd1d41dffacf83a1a97902b62b826ef14ec5704db85c95f17 |
| `specification.md` | `docs/specification.md` | ce35a9f331ef3e679bc7834c98149d42129ab0b87d552bcb7446faa941d81329 |

CI (`v03-spec-drift` job) fails when a file differs from the tag or from the hash above.
