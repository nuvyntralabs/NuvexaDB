package nuvexa

/*
#cgo CFLAGS: -I${SRCDIR}/include
#cgo LDFLAGS: -lnuvexa
#include "nuvexa.h"
#include <stdlib.h>
*/
import "C"

import (
	"encoding/json"
	"errors"
	"os"
	"os/signal"
	"syscall"
	"unsafe"
)

func init() {
	// Native AOT uses SIGUSR1/SIGUSR2. An unhandled SIGUSR1 terminates Go.
	signal.Ignore(syscall.SIGUSR1, syscall.SIGUSR2)
}

const AbiVersion = 2

var (
	ErrEncryption = errors.New("nuvexa encryption")
	ErrIntegrity  = errors.New("nuvexa integrity")
	ErrNotFound   = errors.New("nuvexa not found")
)

type Error struct {
	Status int
	Msg    string
}

func (e *Error) Error() string { return e.Msg }

func (e *Error) Unwrap() error {
	switch e.Status {
	case C.NUVEXA_ENCRYPTION:
		return ErrEncryption
	case C.NUVEXA_INTEGRITY:
		return ErrIntegrity
	case C.NUVEXA_NOT_FOUND:
		return ErrNotFound
	default:
		return nil
	}
}

type Database struct {
	handle C.nuvexa_handle
}

func Version() int { return int(C.nuvexa_abi_version()) }

func Create(path, key string) (*Database, error) {
	return openOrCreate(path, key, true)
}

func Open(path, key string) (*Database, error) {
	return openOrCreate(path, key, false)
}

func IsEncrypted(path string) (bool, error) {
	cPath := C.CString(path)
	defer C.free(unsafe.Pointer(cPath))
	var flag C.int32_t
	if err := check(C.nuvexa_is_encrypted(cPath, &flag)); err != nil {
		return false, err
	}
	return flag != 0, nil
}

func Restore(backupPath, destPath string, overwrite bool) error {
	cBackup := C.CString(backupPath)
	cDest := C.CString(destPath)
	defer C.free(unsafe.Pointer(cBackup))
	defer C.free(unsafe.Pointer(cDest))
	flag := C.int32_t(0)
	if overwrite {
		flag = 1
	}
	return check(C.nuvexa_restore(cBackup, cDest, flag))
}

func openOrCreate(path, key string, create bool) (*Database, error) {
	cPath := C.CString(path)
	defer C.free(unsafe.Pointer(cPath))
	var cKey *C.char
	if key != "" {
		cKey = C.CString(key)
		defer C.free(unsafe.Pointer(cKey))
	}
	var handle C.nuvexa_handle
	var status C.nuvexa_status
	if create {
		status = C.nuvexa_create(cPath, cKey, &handle)
	} else {
		status = C.nuvexa_open(cPath, cKey, &handle)
	}
	if err := check(status); err != nil {
		return nil, err
	}
	return &Database{handle: handle}, nil
}

func (db *Database) Close() error {
	if db.handle == 0 {
		return nil
	}
	status := C.nuvexa_close(db.handle)
	db.handle = 0
	return check(status)
}

func (db *Database) Insert(collection, jsonDoc string) (string, error) {
	return db.stringOut(func(cCol, cJSON *C.char, out **C.char) C.nuvexa_status {
		return C.nuvexa_insert(db.handle, cCol, cJSON, out)
	}, collection, jsonDoc)
}

func (db *Database) InsertMany(collection, jsonArray string) ([]string, error) {
	raw, err := db.stringOut(func(cCol, cJSON *C.char, out **C.char) C.nuvexa_status {
		return C.nuvexa_insert_many(db.handle, cCol, cJSON, out)
	}, collection, jsonArray)
	if err != nil {
		return nil, err
	}
	var ids []string
	if err := json.Unmarshal([]byte(raw), &ids); err != nil {
		return nil, err
	}
	return ids, nil
}

func (db *Database) Replace(collection, jsonDoc string) error {
	cCol, cJSON := C.CString(collection), C.CString(jsonDoc)
	defer C.free(unsafe.Pointer(cCol))
	defer C.free(unsafe.Pointer(cJSON))
	return check(C.nuvexa_replace(db.handle, cCol, cJSON))
}

func (db *Database) DeleteByID(collection, id string) (bool, error) {
	cCol, cID := C.CString(collection), C.CString(id)
	defer C.free(unsafe.Pointer(cCol))
	defer C.free(unsafe.Pointer(cID))
	var deleted C.int32_t
	if err := check(C.nuvexa_delete_by_id(db.handle, cCol, cID, &deleted)); err != nil {
		return false, err
	}
	return deleted != 0, nil
}

func (db *Database) FindByID(collection, id string) (string, error) {
	cCol, cID := C.CString(collection), C.CString(id)
	defer C.free(unsafe.Pointer(cCol))
	defer C.free(unsafe.Pointer(cID))
	var out *C.char
	status := C.nuvexa_find_by_id(db.handle, cCol, cID, &out)
	if status == C.NUVEXA_NOT_FOUND {
		if out != nil {
			C.nuvexa_free(out)
		}
		return "", nil
	}
	if err := check(status); err != nil {
		return "", err
	}
	return take(out), nil
}

func (db *Database) Execute(nql string) ([]map[string]any, error) {
	cNql := C.CString(nql)
	defer C.free(unsafe.Pointer(cNql))
	var out *C.char
	if err := check(C.nuvexa_execute(db.handle, cNql, &out)); err != nil {
		return nil, err
	}
	var rows []map[string]any
	if err := json.Unmarshal([]byte(take(out)), &rows); err != nil {
		return nil, err
	}
	return rows, nil
}

func (db *Database) EnsureIndex(collection string, fields []string) error {
	payload, err := json.Marshal(fields)
	if len(fields) == 1 {
		payload, err = json.Marshal(fields[0])
	}
	if err != nil {
		return err
	}
	cCol, cFields := C.CString(collection), C.CString(string(payload))
	defer C.free(unsafe.Pointer(cCol))
	defer C.free(unsafe.Pointer(cFields))
	return check(C.nuvexa_ensure_index(db.handle, cCol, cFields))
}

func (db *Database) ListCollections() ([]string, error) {
	var out *C.char
	if err := check(C.nuvexa_list_collections(db.handle, &out)); err != nil {
		return nil, err
	}
	var names []string
	if err := json.Unmarshal([]byte(take(out)), &names); err != nil {
		return nil, err
	}
	return names, nil
}

func (db *Database) Count(collection, filterJSON string) (int64, error) {
	if filterJSON == "" {
		filterJSON = "{}"
	}
	cCol, cFilter := C.CString(collection), C.CString(filterJSON)
	defer C.free(unsafe.Pointer(cCol))
	defer C.free(unsafe.Pointer(cFilter))
	var n C.int64_t
	if err := check(C.nuvexa_count(db.handle, cCol, cFilter, &n)); err != nil {
		return 0, err
	}
	return int64(n), nil
}

func (db *Database) BeginTransaction() error {
	return check(C.nuvexa_begin_transaction(db.handle))
}

func (db *Database) Commit() error { return check(C.nuvexa_commit(db.handle)) }

func (db *Database) Rollback() error { return check(C.nuvexa_rollback(db.handle)) }

func (db *Database) stringOut(fn func(*C.char, *C.char, **C.char) C.nuvexa_status, collection, payload string) (string, error) {
	cCol, cJSON := C.CString(collection), C.CString(payload)
	defer C.free(unsafe.Pointer(cCol))
	defer C.free(unsafe.Pointer(cJSON))
	var out *C.char
	if err := check(fn(cCol, cJSON, &out)); err != nil {
		return "", err
	}
	return take(out), nil
}

func check(status C.nuvexa_status) error {
	if status == C.NUVEXA_OK {
		return nil
	}
	return &Error{Status: int(status), Msg: lastError()}
}

func lastError() string {
	var msg *C.char
	C.nuvexa_last_error(&msg)
	text := take(msg)
	if text == "" {
		return "NuvexaDB native call failed."
	}
	return text
}

func take(ptr *C.char) string {
	if ptr == nil {
		return ""
	}
	defer C.nuvexa_free(ptr)
	return C.GoString(ptr)
}

func NativeDir() string {
	return os.Getenv("NUVEXA_NATIVE_DIR")
}
