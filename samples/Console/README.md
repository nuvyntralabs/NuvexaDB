# Console sample

Complete .NET integration tour (`net10.0`): package/project reference, create an encrypted `.nvx`, create a `users` collection, CRUD, and complex NQL plus fluent filters. Shared source: [../Shared/SampleTour.cs](../Shared/SampleTour.cs).

## Integration

### Package reference

This sample uses a project reference. In your app:

```xml
<PackageReference Include="Nuventra.NuvexaDB" Version="1.0.0" />
```

```bash
dotnet add package Nuventra.NuvexaDB
```

Do not publish the engine package from a local clone.

### Create the database and a collection

`GetCollection("users")` creates the collection if it does not exist.

```csharp
// Create writes on-disk format 2. Format 1 files still open.
await using var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "sample-key" });
// db.FormatVersion == 2
var users = db.GetCollection("users");
```

### CRUD

- **Create** — `InsertAsync` / `InsertManyAsync`
- **Read** — `FindByIdAsync`
- **Update** — `ReplaceAsync` (JSON must include `_id`)
- **Delete** — `DeleteByIdAsync`

### Queries

NQL:

```text
db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)
db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })
db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })
```

Fluent:

```csharp
await users
    .Find(NuvexaFilter.And(NuvexaFilter.Gte("age", 21), NuvexaFilter.Eq("status", "active")))
    .Sort("name")
    .Limit(10)
    .ToListAsync();
```

Opening an encrypted file without a key throws `NuvexaEncryptionException`.

```bash
dotnet run --project samples/Console/Console.sln -c Release
```
