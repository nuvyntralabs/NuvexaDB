package main

import (
	"fmt"
	"os"

	nuvexa "github.com/nuvyntralabs/NuvexaDB/bindings/go"
)

func main() {
	path := "sample.nvx"
	if len(os.Args) > 1 {
		path = os.Args[1]
	}
	_ = os.Remove(path)

	db, err := nuvexa.Create(path, "sample-key")
	if err != nil {
		panic(err)
	}
	if _, err := db.Insert("users", `{"name":"Ada","age":36}`); err != nil {
		panic(err)
	}
	if err := db.EnsureIndex("users", []string{"age"}); err != nil {
		panic(err)
	}
	rows, err := db.Execute(`db.users.find({ age: { $gte: 21 } }).limit(20)`)
	if err != nil {
		panic(err)
	}
	fmt.Println(rows)
	names, err := db.ListCollections()
	if err != nil {
		panic(err)
	}
	fmt.Println("collections", names)
	if err := db.Close(); err != nil {
		panic(err)
	}

	enc, err := nuvexa.IsEncrypted(path)
	if err != nil {
		panic(err)
	}
	fmt.Println("encrypted", enc)
	if _, err := nuvexa.Open(path, ""); err != nil {
		fmt.Println("fail-closed", err)
		return
	}
	panic("open without key should fail")
}
