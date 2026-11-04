# Compat.V0_3.Shared

Source shared by the two A2A 0.3 compat packages. There is no csproj. `Ambinder.A2A.Compat.V0_3` and `Ambinder.A2A.Compat.V0_3.AspNetCore` each link `**/*.cs` from here and compile it as `internal` (plan 15 decision D5, spec 01 rules 6 and 10: no `InternalsVisibleTo` between shipped packages).

- `spec/v0.3.0/`: vendored A2A v0.3.0 reference files and the proto the gRPC adapters compile.
- `Models/`, `Json/`, `Conversion/`: added by later tasks.

No type in this folder may be `public`.
