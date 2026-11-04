# Golden wire fixtures (A2A v1.0)

One JSON file plus one `.meta.json` sidecar per case.

## Layout

- `<ProtoMessage>/<case>.json` - one folder per proto message (for example `Task/`, `TaskStatus/`).
- `enums/<Enum>.json` - enum value fixtures.
- `errors/<binding>/<case>.json` - error bodies, one folder per binding (`jsonrpc`, `rest`, `grpc`).

## Meta fields

- `source`: `spec-example`, `hand-written`, or `peer-capture:<peer>@<version>`.
- `specSection`: the A2A v1.0.1 spec section the case comes from or is defined in.
- `expect`: see below.
- `type` (optional): proto message name; defaults to the folder name.
- `binding` (required under `errors/`): `jsonrpc`, `rest` or `grpc`.
- `note` (optional): free text.

## `expect` values

- `roundtrip`: parse, re-serialize, and the result is JCS-equal to the input.
- `read-only`: must parse, but our output may differ from the input (for example accepted legacy spellings).
- `must-reject`: parsing must fail.

## Must-reject rule

A `must-reject` fixture must be well-formed JSON with wrong content (wrong types, bad timestamps, bad base64). Fixtures whose point is malformed JSON text are not allowed.

Peer captures arrive through `interop/promote-capture.py`.
