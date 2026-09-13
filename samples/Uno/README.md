# Uno Platform sample

Desktop head (`net10.0-desktop`). WASM is not a target — the engine needs a real filesystem. **Run integration tour** runs [../Shared/SampleTour.cs](../Shared/SampleTour.cs): create encrypted DB, create `users`, CRUD, complex NQL, fluent find, fail-closed open.

## Integration

```xml
<PackageReference Include="Nuventra.NuvexaDB" Version="1.0.0" />
```

Same API as the Console sample. See [Console README](../Console/README.md).

```bash
dotnet run --project samples/Uno/UnoSample.sln -c Release
```
