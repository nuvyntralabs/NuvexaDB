#include "nuvexa.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <process.h>
#define nuvexa_pid() _getpid()
#else
#include <unistd.h>
#define nuvexa_pid() getpid()
#endif

static int failures;

static void expect(int ok, const char* message)
{
    if (!ok)
    {
        fprintf(stderr, "FAIL %s\n", message);
        failures++;
    }
}

static void require_ok(nuvexa_status status, const char* what)
{
    if (status == NUVEXA_OK)
    {
        return;
    }

    char* message = NULL;
    nuvexa_last_error(&message);
    fprintf(stderr, "FAIL %s (%d): %s\n", what, (int)status, message ? message : "");
    if (message)
    {
        nuvexa_free(message);
    }
    failures++;
}

static int contains(const char* haystack, const char* needle)
{
    return haystack != NULL && needle != NULL && strstr(haystack, needle) != NULL;
}

static void temp_path(char* buffer, size_t size, const char* name)
{
    const char* root = getenv("NUVEXA_TEST_DIR");
    if (root == NULL || root[0] == '\0')
    {
        root = getenv("TMPDIR");
    }
    if (root == NULL || root[0] == '\0')
    {
        root = getenv("TEMP");
    }
    if (root == NULL || root[0] == '\0')
    {
        root = "/tmp";
    }

#ifdef _WIN32
    snprintf(buffer, size, "%s\\%s-%d.nvx", root, name, nuvexa_pid());
#else
    snprintf(buffer, size, "%s/%s-%d.nvx", root, name, nuvexa_pid());
#endif
}

static void seed(nuvexa_handle db)
{
    char* id = NULL;
    require_ok(nuvexa_insert(db, "users", "{\"name\":\"Ada\",\"age\":36,\"city\":\"London\"}", &id), "insert Ada");
    if (id)
    {
        nuvexa_free(id);
    }
    require_ok(nuvexa_insert(db, "users", "{\"name\":\"Cara\",\"age\":21,\"city\":\"Bengaluru\"}", &id), "insert Cara");
    if (id)
    {
        nuvexa_free(id);
    }
    require_ok(nuvexa_insert(db, "users", "{\"name\":\"Ben\",\"age\":12,\"city\":\"Bengaluru\"}", &id), "insert Ben");
    if (id)
    {
        nuvexa_free(id);
    }
    require_ok(nuvexa_ensure_index(db, "users", "[\"age\"]"), "index age");
    require_ok(nuvexa_ensure_index(db, "users", "[\"city\"]"), "index city");
}

static void assert_cases(nuvexa_handle db)
{
    char* json = NULL;
    require_ok(
        nuvexa_execute(db, "db.users.find({ age: { $gte: 21 } }).sort({ name: 1 }).limit(20)", &json),
        "adults NQL");
    expect(contains(json, "\"Ada\"") && contains(json, "\"Cara\"") && !contains(json, "\"Ben\""), "adults-sorted");
    if (json)
    {
        nuvexa_free(json);
        json = NULL;
    }

    require_ok(nuvexa_execute(db, "db.users.find({}).sort({ name: 1 }).page(2, 1)", &json), "page NQL");
    expect(contains(json, "\"Ben\"") && !contains(json, "\"Ada\""), "page-two-size-one");
    if (json)
    {
        nuvexa_free(json);
        json = NULL;
    }

    require_ok(nuvexa_execute(db, "db.users.find({ city: \"Bengaluru\" }).sort({ name: 1 })", &json), "city NQL");
    expect(contains(json, "\"Ben\"") && contains(json, "\"Cara\""), "city-equality");
    if (json)
    {
        nuvexa_free(json);
    }

    int64_t count = 0;
    require_ok(nuvexa_count(db, "users", NULL, &count), "count");
    expect(count == 3, "seed count");
}

int main(void)
{
    expect(nuvexa_abi_version() == NUVEXA_ABI_VERSION, "abi version is 2");

    char path[1024];
    temp_path(path, sizeof path, "nuvexa-abi");

    nuvexa_handle db = 0;
    require_ok(nuvexa_create(path, "correct-horse", &db), "create");
    seed(db);
    assert_cases(db);

    require_ok(nuvexa_begin_transaction(db), "begin tx");
    char* id = NULL;
    require_ok(nuvexa_insert(db, "users", "{\"name\":\"Zoe\",\"age\":40}", &id), "insert Zoe");
    if (id)
    {
        nuvexa_free(id);
    }
    require_ok(nuvexa_rollback(db), "rollback");
    int64_t count = 0;
    require_ok(nuvexa_count(db, "users", NULL, &count), "count after rollback");
    expect(count == 3, "rollback restores count");

    char* collections = NULL;
    require_ok(nuvexa_list_collections(db, &collections), "list collections");
    expect(contains(collections, "users"), "users collection listed");
    if (collections)
    {
        nuvexa_free(collections);
    }

    require_ok(nuvexa_close(db), "close");

    int32_t encrypted = 0;
    require_ok(nuvexa_is_encrypted(path, &encrypted), "is_encrypted");
    expect(encrypted != 0, "file is encrypted");

    nuvexa_handle opened = 0;
    expect(nuvexa_open(path, NULL, &opened) == NUVEXA_ENCRYPTION, "fail-closed open");

    if (failures != 0)
    {
        fprintf(stderr, "%d assertion(s) failed\n", failures);
        return 1;
    }

    printf("ok abi_runner cases.json\n");
    return 0;
}
