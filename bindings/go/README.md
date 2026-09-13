# Go library

cgo SDK over `nuvexa.h`. ABI v2. The library ignores `SIGUSR1` / `SIGUSR2` so Native AOT GC signals do not terminate the Go process.

| Piece | Path |
| --- | --- |
| Library | `nuvexa.go` + `include/nuvexa.h` (`go.mod`) |
| Tests | `nuvexa_test.go` |
| Sample | [examples/sample](examples/sample) |
| This file | `README.md` |

```bash
# from the NuvexaDB repo root
src/Nuventra.NuvexaDB.Native/publish.sh
export NUVEXA_NATIVE_DIR="$(pwd)/artifacts/native/osx-arm64"
export CGO_LDFLAGS="-L$NUVEXA_NATIVE_DIR -lnuvexa"
export DYLD_LIBRARY_PATH="$NUVEXA_NATIVE_DIR${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
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
