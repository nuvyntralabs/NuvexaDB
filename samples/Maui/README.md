# .NET MAUI sample

App-data `.nvx` on Android, iOS, Mac Catalyst, and Windows. The **Run integration tour** button runs the same create / collection / CRUD / complex-query flow as the Console sample ([../Shared/SampleTour.cs](../Shared/SampleTour.cs)). Extra buttons keep open, insert, NQL, LINQ, and GridFS.

## Integration

### Package reference

```xml
<PackageReference Include="Nuventra.NuvexaDB" Version="1.0.0" />
```

This sample uses a `ProjectReference` to `src/Nuventra.NuvexaDB`.

### Create the database and a collection

```csharp
var path = Path.Combine(FileSystem.AppDataDirectory, "cache.nvx");
// Create writes on-disk format 2. Format 1 files still open.
await using var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions { EncryptionKey = "sample-key" });
// db.FormatVersion == 2
var users = db.GetCollection("users"); // created on first use
```

### CRUD and queries

See [Console README](../Console/README.md) and `SampleTour`. NQL examples:

```text
db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)
db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })
db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })
```

LINQ (typed collection):

```csharp
var adults = await db.GetCollection<Person>("users").ToListAsync(p => p.Age >= 21);
```

```bash
dotnet run --project samples/Maui/MauiSample.sln -f net10.0-maccatalyst
dotnet run --project samples/Maui/MauiSample.sln -f net10.0-android
dotnet run --project samples/Maui/MauiSample.sln -f net10.0-ios
```

On Windows add `-f net10.0-windows10.0.19041.0`.
