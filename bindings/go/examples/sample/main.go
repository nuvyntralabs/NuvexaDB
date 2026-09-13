package main

import (
	"encoding/json"
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

	adaID, err := db.Insert("users", `{"name":"Ada","age":36,"status":"active","address":{"city":"London"}}`)
	must(err)
	_, err = db.InsertMany("users", `[
		{"name":"Grace","age":85,"status":"retired","address":{"city":"New York"}},
		{"name":"Cara","age":21,"status":"active","address":{"city":"Bengaluru"}},
		{"name":"Alan","age":42,"status":"active","address":{"city":"London"}}
	]`)
	must(err)
	scratchID, err := db.Insert("users", `{"name":"Scratch","age":19,"status":"active","address":{"city":"Paris"}}`)
	must(err)
	must(db.EnsureIndex("users", []string{"age"}))
	must(db.EnsureIndex("users", []string{"address.city", "status"}))
	names, err := db.ListCollections()
	must(err)
	fmt.Println("Created collection users. Collections", names)

	ada, err := db.FindByID("users", adaID)
	must(err)
	fmt.Println("Read Ada:", ada)
	updated, _ := json.Marshal(map[string]any{
		"_id": adaID, "name": "Ada Lovelace", "age": 36, "status": "active",
		"address": map[string]any{"city": "London"},
	})
	must(db.Replace("users", string(updated)))
	ada, err = db.FindByID("users", adaID)
	must(err)
	fmt.Println("Updated Ada:", ada)
	deleted, err := db.DeleteByID("users", scratchID)
	must(err)
	fmt.Println("Deleted scratch:", deleted)

	fmt.Println("-- NQL age >= 21, sort name, limit 10 --")
	printRows(db, `db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(10)`)
	fmt.Println("-- NQL $and London + active --")
	printRows(db, `db.users.find({ $and: [ { "address.city": "London" }, { status: "active" } ] }).sort({ age: -1 })`)
	fmt.Println("-- NQL $or age < 30 or New York --")
	printRows(db, `db.users.find({ $or: [ { age: { $lt: 30 } }, { "address.city": "New York" } ] })`)
	must(db.Close())

	enc, err := nuvexa.IsEncrypted(path)
	must(err)
	fmt.Println("encrypted", enc)
	if _, err := nuvexa.Open(path, ""); err != nil {
		fmt.Println("fail-closed", err)
		return
	}
	panic("open without key should fail")
}

func printRows(db *nuvexa.Database, nql string) {
	rows, err := db.Execute(nql)
	must(err)
	for _, row := range rows {
		fmt.Println(row)
	}
}

func must(err error) {
	if err != nil {
		panic(err)
	}
}
