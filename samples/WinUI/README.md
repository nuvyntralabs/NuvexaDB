# WinUI 3 sample

Unpackaged WinUI 3 (`net10.0-windows10.0.19041.0`). **Run integration tour** runs [../Shared/SampleTour.cs](../Shared/SampleTour.cs): create encrypted DB, create `users`, CRUD, complex NQL, fluent find, fail-closed open.

## Integration

```xml
<PackageReference Include="Nuventra.NuvexaDB" Version="1.0.0" />
```

Same API as the Console sample: `NuvexaDatabase.Create`, `GetCollection`, `InsertAsync` / `ReplaceAsync` / `DeleteByIdAsync`, `ExecuteAsync`, `NuvexaFilter` fluent find. See [Console README](../Console/README.md).

```bash
dotnet run --project samples/WinUI/WinUISample.sln -c Release
```
