package nuvexa_test

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"runtime"
	"testing"

	nuvexa "github.com/nuvyntralabs/NuvexaDB/bindings/go"
)

func skipDarwinAOT(t *testing.T) {
	t.Helper()
	if runtime.GOOS == "darwin" {
		t.Skip("Native AOT SIGUSR1/SIGUSR2 cannot be hosted in the Go runtime on Darwin. Linux go test and the native ABI job cover these cases.")
	}
}

type fixture struct {
	Key        string              `json:"key"`
	Collection string              `json:"collection"`
	Documents  []json.RawMessage   `json:"documents"`
	Indexes    [][]string          `json:"indexes"`
	Cases      []struct {
		Name        string   `json:"name"`
		Nql         string   `json:"nql"`
		ExpectNames []string `json:"expectNames"`
	} `json:"cases"`
}

func loadFixture(t *testing.T) fixture {
	t.Helper()
	dir, err := os.Getwd()
	if err != nil {
		t.Fatal(err)
	}
	for i := 0; i < 8; i++ {
		candidate := filepath.Join(dir, "tests", "interop", "cases.json")
		data, err := os.ReadFile(candidate)
		if err == nil {
			var fx fixture
			if err := json.Unmarshal(data, &fx); err != nil {
				t.Fatal(err)
			}
			return fx
		}
		dir = filepath.Dir(dir)
	}
	t.Fatal("tests/interop/cases.json was not found")
	return fixture{}
}

func TestGoldenCases(t *testing.T) {
	skipDarwinAOT(t)
	if os.Getenv("NUVEXA_NATIVE_DIR") == "" && os.Getenv("NUVEXA_NATIVE_LIB") == "" {
		t.Skip("set NUVEXA_NATIVE_DIR or NUVEXA_NATIVE_LIB")
	}
	fx := loadFixture(t)
	path := filepath.Join(t.TempDir(), "golden.nvx")
	db, err := nuvexa.Create(path, fx.Key)
	if err != nil {
		t.Fatal(err)
	}
	for _, doc := range fx.Documents {
		if _, err := db.Insert(fx.Collection, string(doc)); err != nil {
			t.Fatal(err)
		}
	}
	for _, fields := range fx.Indexes {
		if err := db.EnsureIndex(fx.Collection, fields); err != nil {
			t.Fatal(err)
		}
	}
	for _, query := range fx.Cases {
		rows, err := db.Execute(query.Nql)
		if err != nil {
			t.Fatal(err)
		}
		var names []string
		for _, row := range rows {
			names = append(names, row["name"].(string))
		}
		if len(names) != len(query.ExpectNames) {
			t.Fatalf("%s: got %v want %v", query.Name, names, query.ExpectNames)
		}
		for i := range names {
			if names[i] != query.ExpectNames[i] {
				t.Fatalf("%s: got %v want %v", query.Name, names, query.ExpectNames)
			}
		}
	}
	if err := db.Close(); err != nil {
		t.Fatal(err)
	}
}

func TestAbiVersion(t *testing.T) {
	skipDarwinAOT(t)
	if os.Getenv("NUVEXA_NATIVE_DIR") == "" && os.Getenv("NUVEXA_NATIVE_LIB") == "" {
		t.Skip("set NUVEXA_NATIVE_DIR or NUVEXA_NATIVE_LIB")
	}
	if nuvexa.Version() != 2 {
		t.Fatalf("abi version %d", nuvexa.Version())
	}
}

func TestGoldenAndExtended(t *testing.T) {
	skipDarwinAOT(t)
	if os.Getenv("NUVEXA_NATIVE_DIR") == "" && os.Getenv("NUVEXA_NATIVE_LIB") == "" {
		t.Skip("set NUVEXA_NATIVE_DIR or NUVEXA_NATIVE_LIB")
	}

	dir := t.TempDir()
	path := filepath.Join(dir, "ext.nvx")
	db, err := nuvexa.Create(path, "key")
	if err != nil {
		t.Fatal(err)
	}
	defer db.Close()

	if _, err := db.Insert("users", `{"name":"Ada","age":36}`); err != nil {
		t.Fatal(err)
	}
	if _, err := db.InsertMany("users", `[{"name":"Ben","age":12}]`); err != nil {
		t.Fatal(err)
	}
	names, err := db.ListCollections()
	if err != nil {
		t.Fatal(err)
	}
	if len(names) != 1 || names[0] != "users" {
		t.Fatalf("collections %v", names)
	}
	n, err := db.Count("users", "{}")
	if err != nil || n != 2 {
		t.Fatalf("count %d %v", n, err)
	}
	if err := db.BeginTransaction(); err != nil {
		t.Fatal(err)
	}
	if _, err := db.Insert("users", `{"name":"Zoe","age":40}`); err != nil {
		t.Fatal(err)
	}
	if err := db.Rollback(); err != nil {
		t.Fatal(err)
	}
	n, err = db.Count("users", "{}")
	if err != nil || n != 2 {
		t.Fatalf("after rollback %d %v", n, err)
	}

	rows, err := db.Execute(`db.users.find({ age: { $gte: 21 } }).sort({ name: 1 })`)
	if err != nil {
		t.Fatal(err)
	}
	if len(rows) != 1 || rows[0]["name"] != "Ada" {
		t.Fatalf("nql %v", rows)
	}

	if err := db.Close(); err != nil {
		t.Fatal(err)
	}
	enc, err := nuvexa.IsEncrypted(path)
	if err != nil || !enc {
		t.Fatalf("encrypted %v %v", enc, err)
	}
	_, err = nuvexa.Open(path, "")
	if !errors.Is(err, nuvexa.ErrEncryption) {
		t.Fatalf("expected encryption, got %v", err)
	}
}
