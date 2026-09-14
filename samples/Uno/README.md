# Uno Platform sample

Desktop head (`net10.0-desktop`). WASM is not a target — the engine needs a real filesystem. **Run integration tour** runs [../Shared/SampleTour.cs](../Shared/SampleTour.cs): create encrypted DB, create `users`, CRUD, complex NQL, fluent find, fail-closed open.

## Integration

```xml
<PackageReference Include="Nuventra.NuvexaDB" Version="1.0.0" />
```

```csharp
// Create writes on-disk format 2. Format 1 files still open.
await using var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "sample-key" });
// db.FormatVersion == 2
```

Same API as the Console sample. See [Console README](../Console/README.md).

```bash
dotnet run --project samples/Uno/UnoSample.sln -c Release
```
