# Go

cgo wrapper (`github.com/nuvyntralabs/NuvexaDB/bindings/go`). Link `-lnuvexa` against the published C ABI.

Runtime interop tests run on **Linux**. On macOS, Native AOT GC uses SIGUSR1; create/open from a Darwin Go process is not the supported interop path. Prefer Linux for Go + native, or call the engine from another language on macOS.

## 1. Download

From [NuvexaDB v1.0.1](https://github.com/nuvyntralabs/NuvexaDB/releases/tag/v1.0.1):

| Your machine | Go zip | Native zip | Library file |
| --- | --- | --- | --- |
| Linux x64 | `NuvexaDB-Go-linux-x64.zip` | `NuvexaDB-Native-linux-x64.zip` | `libnuvexa.so` |
| macOS Apple Silicon (sources only) | `NuvexaDB-Go-osx-arm64.zip` | `NuvexaDB-Native-osx-arm64.zip` | `libnuvexa.dylib` |

Inside the Go zip: `nuvexadb-go-1.0.1.tgz` (`go.mod`, `nuvexa.go`, `include/nuvexa.h`, examples).

## 2. Empty project

```bash
mkdir acme-store && cd acme-store
go mod init acme.example/store
```

## 3. Add the downloaded package

Unpack `nuvexadb-go-1.0.1.tgz` and add a replace, or copy the module next to your app:

```go
// go.mod
require github.com/nuvyntralabs/NuvexaDB/bindings/go v0.0.0
replace github.com/nuvyntralabs/NuvexaDB/bindings/go => ../nuvexadb-go
```

```bash
export NUVEXA_NATIVE_DIR="/absolute/path/to/native-unzip"
export CGO_ENABLED=1
export CGO_LDFLAGS="-L$NUVEXA_NATIVE_DIR -lnuvexa"
export LD_LIBRARY_PATH="$NUVEXA_NATIVE_DIR"          # Linux
export DYLD_LIBRARY_PATH="$NUVEXA_NATIVE_DIR"        # macOS, if you run there
```

```go
import nuvexa "github.com/nuvyntralabs/NuvexaDB/bindings/go"
```

## 4. Create, open, close

```go
path := "app.nvx"
db, err := nuvexa.Create(path, key) // empty key → plaintext
if err != nil {
    log.Fatal(err)
}
defer db.Close()

enc, err := nuvexa.IsEncrypted(path)
opened, err := nuvexa.Open(path, key)
defer opened.Close()
```

## 5. Delete the database

```go
_ = db.Close()
_ = os.Remove(path)
_ = os.Remove(path + "-wal")
```

## 6. Collections

The first `Insert` into a name creates that collection.

```go
_, err = db.Insert("users", `{"name":"Ada","age":36}`)
names, err := db.ListCollections()
```

The Go wrapper on 1.0.1 does not expose `drop_collection` / `rename_collection`. To discard a collection, close the handle and delete the `.nvx`, or call `nuvexa_drop_collection` from `nuvexa.h` yourself.

## 7. Documents

```go
id, err := db.Insert("users", `{"name":"Ada","age":36}`)
ids, err := db.InsertMany("users", `[{"name":"Grace","age":85}]`)
raw, err := db.FindByID("users", id)
err = db.Replace("users", `{"_id":"`+id+`","name":"Ada Lovelace","age":36}`)
ok, err := db.DeleteByID("users", id)
rows, err := db.Execute(`db.users.find({ age: { $gte: 21 } }).limit(20)`)
```

## 8. Password for an encrypted file

Pass the key into `Create` and `Open`. Store it outside the binary. The published Go wrapper does not wrap `nuvexa_change_encryption_key`; rekey from .NET, the CLI, or `nuvexa.h`.

See [Encryption notes](README.md#encrypted-databases--choose-a-password).
