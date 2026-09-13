//go:build darwin

package nuvexa

/*
#cgo LDFLAGS: -Wl,-undefined,dynamic_lookup
#include <dlfcn.h>
#include <stdlib.h>
*/
import "C"

import (
	"os"
	"path/filepath"
	"unsafe"
)

// Delay-load so Native AOT can install SIGUSR1/SIGUSR2 after the Go runtime.
func loadNativeLibrary() {
	path := os.Getenv("NUVEXA_NATIVE_LIB")
	if path == "" {
		dir := os.Getenv("NUVEXA_NATIVE_DIR")
		if dir != "" {
			for _, name := range []string{"libnuvexa.dylib", "nuvexa.dylib"} {
				candidate := filepath.Join(dir, name)
				if _, err := os.Stat(candidate); err == nil {
					path = candidate
					break
				}
			}
		}
	}
	if path == "" {
		path = "libnuvexa.dylib"
	}

	cpath := C.CString(path)
	defer C.free(unsafe.Pointer(cpath))
	if C.dlopen(cpath, C.RTLD_NOW|C.RTLD_GLOBAL) == nil {
		panic("nuvexa: dlopen " + path + ": " + C.GoString(C.dlerror()))
	}
}
