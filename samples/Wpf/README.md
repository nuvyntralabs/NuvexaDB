# WPF sample

Windows-only (`net10.0-windows10.0.17763.0`). **Run integration tour** runs [../Shared/SampleTour.cs](../Shared/SampleTour.cs): create encrypted DB, create `users`, CRUD, complex NQL, fluent find, fail-closed open.

## Integration

```xml
<PackageReference Include="Nuventra.NuvexaDB" Version="1.0.0" />
```

`GetCollection("users")` creates the collection. Insert / find-by-id / replace / delete-by-id, then:

```text
db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)
db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })
db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })
```

Details: [Console README](../Console/README.md).

```bash
dotnet run --project samples/Wpf/WpfSample.sln -c Release
```
