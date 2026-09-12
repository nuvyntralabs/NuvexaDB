# Query

Shell:

```
db.<collection>.find({ ... }).sort({ field: 1 }).skip(n).limit(n).project({ field: 1 })
```

Unquoted JS keys are accepted (`age: { $gte: 21 }` → valid JSON).

.NET:

```csharp
collection.Find(NuvexaFilter.Gte("age", 21) & /* use And() */)
    .Sort("name")
    .Limit(20);
```

`explain()` reports `ID`, `IXSCAN`, or `COLLSCAN`.

LINQ (typed collection, AOT-safe visitor for comparisons / `StartsWith`; captured locals compile):

```csharp
var adults = await db.GetCollection<Person>("people").ToListAsync(p => p.Age >= 21);
```

Aggregation (in-memory after a collection scan): `$match $project $sort $skip $limit $count $lookup`.

```javascript
db.orders.aggregate([{ $lookup: { from: "users", localField: "userId", foreignField: "_id", as: "user" } }, { $count: "n" }])
```

GridFS-style files: `db.Files.UploadAsync` / `DownloadAsync` store chunks in `fs.files` / `fs.chunks`.
