# Go library

cgo SDK over `nuvexa.h`. ABI v2. On Linux the library ignores `SIGUSR1` / `SIGUSR2`. On Darwin the Go runtime cannot host Native AOT GC signals (`signal.Ignore` deadlocks `nuvexa_create`; the default handler dies with SIGUSR1). Runtime interop tests run on Linux; the native ABI job covers macOS golden cases.

| Piece | Path |
| --- | --- |
| Library | `nuvexa.go` + `include/nuvexa.h` (`go.mod`) |
| Tests | `nuvexa_test.go` |
| Sample | [examples/sample](examples/sample) |
| This file | `README.md` |

```bash
# from the NuvexaDB repo root
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_DIR="$(pwd)/artifacts/native/linux-x64"  # Darwin go test skips runtime interop
export CGO_LDFLAGS="-L$NUVEXA_NATIVE_DIR -lnuvexa"
export LD_LIBRARY_PATH="$NUVEXA_NATIVE_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
cd bindings/go
go test
cd examples/sample && go run .
```

```go
db, err := nuvexa.Create("app.nvx", "correct-horse")
_, err = db.Insert("users", `{"name":"Ada","age":36}`)
rows, err := db.Execute(`db.users.find({ age: { $gte: 21 } }).limit(20)`)
```

Do not publish a Go module from this clone.
