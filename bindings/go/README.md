# Go library

cgo package `nuvexa` (`github.com/nuvyntralabs/NuvexaDB/bindings/go`). ABI v2.

Linux ignores `SIGUSR1` / `SIGUSR2`. Darwin cannot host Native AOT GC signals — **runtime interop tests run on Linux**. Darwin `init` does not `dlopen`.

## Integration

Do not publish a Go module from this clone. `replace` this folder or CI `NuvexaDB-Go-<rid>` plus `NuvexaDB-Native-<rid>`.

### Package reference

```go
import nuvexa "github.com/nuvyntralabs/NuvexaDB/bindings/go"
// go.mod: replace github.com/nuvyntralabs/NuvexaDB/bindings/go => ../NuvexaDB/bindings/go
```

```bash
export NUVEXA_NATIVE_DIR="$(pwd)/artifacts/native/linux-x64"
export CGO_LDFLAGS="-L$NUVEXA_NATIVE_DIR -lnuvexa"
export LD_LIBRARY_PATH="$NUVEXA_NATIVE_DIR"
```

`CGO_ENABLED=1`.

### Create DB, collection, CRUD, queries

```go
db, err := nuvexa.Create("app.nvx", "sample-key")
adaID, err := db.Insert("users", `{"name":"Ada","age":36,"status":"active","address":{"city":"London"}}`)
_, err = db.FindByID("users", adaID)
err = db.Replace("users", `{"_id":"...","name":"Ada Lovelace",...}`)
_, err = db.DeleteByID("users", scratchID)
rows, err := db.Execute(`db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)`)
rows, err = db.Execute(`db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })`)
rows, err = db.Execute(`db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })`)
```

First `Insert` creates `users`. Close the handle before `Open`. Failures wrap `ErrEncryption` / `ErrIntegrity` / `ErrNotFound`.

## In-repo sample

[examples/sample](examples/sample)

```bash
cd bindings/go
go test
cd examples/sample && go run .
```

See [docs/bindings.md](../../docs/bindings.md).
