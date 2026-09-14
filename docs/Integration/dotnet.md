# .NET and .NET MAUI

Managed engine. You do **not** download `libnuvexa` / `nuvexa.dll`. The `Nuventra.NuvexaDB` package is the database.

## 1. Download

From [NuvexaDB v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1):

| Release asset | Open the zip and use |
| --- | --- |
| **`NuvexaDB-NuGet.zip`** | `Nuventra.NuvexaDB.1.0.1.nupkg` (optional: `Nuventra.NuvexaDB.1.0.1.snupkg` for symbols) |

Later tags use the same zip name; the nupkg version matches the tag (`Nuventra.NuvexaDB.1.0.3.nupkg` on v1.0.3).

Ignore `NuvexaDB-Native-*.zip` for a C# app. Those are for Java, Python, Node, Go, C++, Swift, Flutter, and React Native.

## 2. Empty project

```bash
dotnet new console -n Acme.Store -f net10.0
cd Acme.Store
```

MAUI:

```bash
dotnet new maui -n Acme.Store.Maui
cd Acme.Store.Maui
```

WPF, WinUI, Avalonia, and Uno samples in this repo use the same package.

## 3. Add the downloaded package

```bash
dotnet add package Nuventra.NuvexaDB --source /path/to/unzipped-nuget
```

`/path/to/unzipped-nuget` is the folder that contains `Nuventra.NuvexaDB.1.0.1.nupkg` (the zip root after you extract `NuvexaDB-NuGet.zip`).

Or add a local feed in `NuGet.config` and then `dotnet add package Nuventra.NuvexaDB --version 1.0.1`.

## 4. Create, open, close

```csharp
using Nuventra.NuvexaDB;

var path = Path.Combine(AppContext.BaseDirectory, "app.nvx");

await using (var db = NuvexaDatabase.Create(path, new NuvexaCreateOptions
{
    EncryptionKey = key // omit or null for a plaintext file
}))
{
    // work
}

if (NuvexaDatabase.IsEncrypted(path) && string.IsNullOrEmpty(key))
{
    throw new InvalidOperationException("This .nvx is encrypted. Supply EncryptionKey.");
}

await using var opened = NuvexaDatabase.Open(path, new NuvexaOpenOptions
{
    EncryptionKey = key
});
// Dispose / await using closes the file. One process per path.
```

MAUI: put the file under `FileSystem.AppDataDirectory` so iOS/Android sandbox rules apply.

Opening an encrypted file without `EncryptionKey` throws `NuvexaEncryptionException`.

## 5. Delete the database

There is no `DropDatabase` API. Close the handle, then delete the file and its WAL sibling:

```csharp
await db.DisposeAsync();
File.Delete(path);
var wal = path + "-wal";
if (File.Exists(wal))
{
    File.Delete(wal);
}
```

## 6. Collections

`GetCollection` creates the collection if it is missing.

```csharp
var users = db.GetCollection("users");
foreach (var name in db.GetCollectionNames())
{
    Console.WriteLine(name);
}

await db.RenameCollectionAsync("users", "people");
await db.DropCollectionAsync("people");
```

## 7. Documents

```csharp
var users = db.GetCollection("users");

var id = await users.InsertAsync(NuvexaDocument.Parse("""{"name":"Ada","age":36}"""));
await users.InsertManyAsync(
[
    NuvexaDocument.Parse("""{"name":"Grace","age":85}"""),
    NuvexaDocument.Parse("""{"name":"Cara","age":21}""")
]);

var ada = await users.FindByIdAsync(id);
await users.ReplaceAsync(NuvexaDocument.Parse($$"""{"_id":"{{id}}","name":"Ada Lovelace","age":36}"""));
var removed = await users.DeleteByIdAsync(id);

var rows = await db.ExecuteAsync("""db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(20)""");
```

On 1.0.2+ you can also write:

```csharp
await db.ExecuteAsync("""db.users.update({ name: "Cara" }, { $set: { age: 22 } })""");
await db.ExecuteAsync("""db.users.delete({ age: { $lt: 18 } })""");
```

## 8. Password for an encrypted file

Pass `EncryptionKey` on create and on every later open. Use a long random secret or a multi-word passphrase; store it in the platform secret store (`SecureStorage` on MAUI, DPAPI / Credential Manager on Windows, Keychain on Apple). Do not put `1234` or the connection string in `appsettings.json` in source control.

Rotate:

```csharp
await db.ChangeEncryptionKeyAsync(currentKey, nextKey);
```

See [Encryption notes](README.md#encrypted-databases--choose-a-password).
