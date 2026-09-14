# NQL (Nuvexa Query Language)

NuvexaDB’s query language. Write it in Nuvexa Data Studio’s **NQL** tab, `NuvexaDatabase.ExecuteAsync`, or `nuvexa query`.

Shell:

```
db.<collection>.find({ ... }).sort({ field: 1 }).skip(n).limit(n).project({ field: 1 })
db.<collection>.update({ ... }, { $set: { ... } })
db.<collection>.delete({ ... })
```

Unquoted JS keys are accepted (`age: { $gte: 21 }` → valid JSON).

.NET:

```csharp
collection.Find(NuvexaFilter.Gte("age", 21) & /* use And() */)
    .Sort("name")
    .Limit(20);
```

`explain()` reports `ID`, `IXSCAN`, `COLLSCAN`, `AGGREGATE`, `UPDATE`, or `DELETE`.

LINQ (typed collection, AOT-safe visitor for comparisons / `StartsWith`; captured locals compile):

```csharp
var adults = await db.GetCollection<Person>("people").ToListAsync(p => p.Age >= 21);
```

Aggregation (in-memory after a collection scan): `$match $project $sort $skip $limit $count $group $lookup`.

```javascript
db.orders.aggregate([{ $lookup: { from: "users", localField: "userId", foreignField: "_id", as: "user" } }, { $count: "n" }])
db.orders.aggregate([{ $group: { _id: "$city", n: { $sum: 1 }, total: { $sum: "$total" } } }])
```

`$lookup` fails if the foreign collection is larger than `NuvexaDatabase.LookupMaxDocuments` (default `NuvexaLimits.DefaultLookupMaxDocuments`). Set `0` for unlimited. `$group` accumulators: `$sum $min $max $avg $first`.

Compound indexes: `EnsureIndexAsync(["city", "status"])`. Equality on the prefix (or all fields) can `IXSCAN`. Format v2 numeric ranges (`d:` keys) use bounded IXSCAN; v1 files still walk `n:` G17 keys.

GridFS-style files: `db.Files.UploadAsync` / `DownloadAsync` store chunks in `fs.files` / `fs.chunks`.
