# Avalonia sample

Desktop window (`net10.0`) on Windows, macOS, and Linux. **Run integration tour** executes [../Shared/SampleTour.cs](../Shared/SampleTour.cs): package/project reference, encrypted create, `users` collection, CRUD, complex NQL, fluent find, fail-closed open.

## Integration

```xml
<PackageReference Include="Nuventra.NuvexaDB" Version="1.0.0" />
```

```csharp
await using var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "sample-key" });
var users = db.GetCollection("users");
await users.InsertAsync(NuvexaDocument.Parse("""{"name":"Ada","age":36,"status":"active","address":{"city":"London"}}"""));
var rows = await db.ExecuteAsync("""db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })""");
```

Full CRUD + query list: [Console README](../Console/README.md).

```bash
dotnet run --project samples/Avalonia/AvaloniaSample.sln -c Release
```
