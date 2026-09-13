#import "NuvexaDB.h"
#include "include/nuvexa.h"

@implementation NuvexaDB

RCT_EXPORT_MODULE();

static NSString *LastError(void) {
  char *message = NULL;
  nuvexa_last_error(&message);
  if (message == NULL) {
    return @"NuvexaDB native call failed.";
  }
  NSString *text = [NSString stringWithUTF8String:message];
  nuvexa_free(message);
  return text ?: @"NuvexaDB native call failed.";
}

static void Reject(RCTPromiseRejectBlock reject, int status) {
  NSString *code = status == NUVEXA_ENCRYPTION ? @"ENCRYPTION"
                    : status == NUVEXA_INTEGRITY ? @"INTEGRITY"
                    : @"ERROR";
  reject(code, LastError(), nil);
}

RCT_EXPORT_METHOD(create:(NSString *)path
                  key:(NSString *)key
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  nuvexa_handle handle = 0;
  int status = nuvexa_create(path.UTF8String, key.UTF8String, &handle);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(@((long long)handle));
}

RCT_EXPORT_METHOD(open:(NSString *)path
                  key:(NSString *)key
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  nuvexa_handle handle = 0;
  int status = nuvexa_open(path.UTF8String, key.UTF8String, &handle);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(@((long long)handle));
}

RCT_EXPORT_METHOD(close:(nonnull NSNumber *)handle
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_close((nuvexa_handle)handle.longLongValue);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(isEncrypted:(NSString *)path
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int32_t encrypted = 0;
  int status = nuvexa_is_encrypted(path.UTF8String, &encrypted);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(@(encrypted != 0));
}

RCT_EXPORT_METHOD(insert:(nonnull NSNumber *)handle
                  collection:(NSString *)collection
                  json:(NSString *)json
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  char *id = NULL;
  int status = nuvexa_insert((nuvexa_handle)handle.longLongValue, collection.UTF8String, json.UTF8String, &id);
  if (status != NUVEXA_OK) {
    if (id) nuvexa_free(id);
    Reject(reject, status);
    return;
  }
  NSString *text = id ? [NSString stringWithUTF8String:id] : @"";
  if (id) nuvexa_free(id);
  resolve(text);
}

RCT_EXPORT_METHOD(replace:(nonnull NSNumber *)handle
                  collection:(NSString *)collection
                  json:(NSString *)json
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_replace((nuvexa_handle)handle.longLongValue, collection.UTF8String, json.UTF8String);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(deleteById:(nonnull NSNumber *)handle
                  collection:(NSString *)collection
                  id:(NSString *)id
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int32_t deleted = 0;
  int status = nuvexa_delete_by_id((nuvexa_handle)handle.longLongValue, collection.UTF8String, id.UTF8String, &deleted);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(@(deleted != 0));
}

RCT_EXPORT_METHOD(findById:(nonnull NSNumber *)handle
                  collection:(NSString *)collection
                  id:(NSString *)id
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  char *json = NULL;
  int status = nuvexa_find_by_id((nuvexa_handle)handle.longLongValue, collection.UTF8String, id.UTF8String, &json);
  if (status == NUVEXA_NOT_FOUND) {
    if (json) nuvexa_free(json);
    resolve([NSNull null]);
    return;
  }
  if (status != NUVEXA_OK) {
    if (json) nuvexa_free(json);
    Reject(reject, status);
    return;
  }
  NSString *text = json ? [NSString stringWithUTF8String:json] : @"";
  if (json) nuvexa_free(json);
  resolve(text);
}

RCT_EXPORT_METHOD(execute:(nonnull NSNumber *)handle
                  nql:(NSString *)nql
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  char *json = NULL;
  int status = nuvexa_execute((nuvexa_handle)handle.longLongValue, nql.UTF8String, &json);
  if (status != NUVEXA_OK) {
    if (json) nuvexa_free(json);
    Reject(reject, status);
    return;
  }
  NSString *text = json ? [NSString stringWithUTF8String:json] : @"[]";
  if (json) nuvexa_free(json);
  resolve(text);
}

RCT_EXPORT_METHOD(ensureIndex:(nonnull NSNumber *)handle
                  collection:(NSString *)collection
                  fieldsJson:(NSString *)fieldsJson
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_ensure_index((nuvexa_handle)handle.longLongValue, collection.UTF8String, fieldsJson.UTF8String);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(abiVersion:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  resolve(@(nuvexa_abi_version()));
}

static NSString *TakeString(char *text) {
  if (text == NULL) {
    return @"";
  }
  NSString *value = [NSString stringWithUTF8String:text] ?: @"";
  nuvexa_free(text);
  return value;
}

RCT_EXPORT_METHOD(insertMany:(nonnull NSNumber *)handle
                  collection:(NSString *)collection
                  jsonArray:(NSString *)jsonArray
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  char *ids = NULL;
  int status = nuvexa_insert_many((nuvexa_handle)handle.longLongValue, collection.UTF8String, jsonArray.UTF8String, &ids);
  if (status != NUVEXA_OK) {
    if (ids) nuvexa_free(ids);
    Reject(reject, status);
    return;
  }
  resolve(TakeString(ids));
}

RCT_EXPORT_METHOD(listCollections:(nonnull NSNumber *)handle
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  char *json = NULL;
  int status = nuvexa_list_collections((nuvexa_handle)handle.longLongValue, &json);
  if (status != NUVEXA_OK) {
    if (json) nuvexa_free(json);
    Reject(reject, status);
    return;
  }
  resolve(TakeString(json));
}

RCT_EXPORT_METHOD(dropCollection:(nonnull NSNumber *)handle
                  collection:(NSString *)collection
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_drop_collection((nuvexa_handle)handle.longLongValue, collection.UTF8String);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(renameCollection:(nonnull NSNumber *)handle
                  from:(NSString *)from
                  to:(NSString *)to
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_rename_collection((nuvexa_handle)handle.longLongValue, from.UTF8String, to.UTF8String);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(listIndexes:(nonnull NSNumber *)handle
                  collection:(NSString *)collection
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  char *json = NULL;
  int status = nuvexa_list_indexes((nuvexa_handle)handle.longLongValue, collection.UTF8String, &json);
  if (status != NUVEXA_OK) {
    if (json) nuvexa_free(json);
    Reject(reject, status);
    return;
  }
  resolve(TakeString(json));
}

RCT_EXPORT_METHOD(dropIndex:(nonnull NSNumber *)handle
                  collection:(NSString *)collection
                  name:(NSString *)name
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_drop_index((nuvexa_handle)handle.longLongValue, collection.UTF8String, name.UTF8String);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(count:(nonnull NSNumber *)handle
                  collection:(NSString *)collection
                  filterJson:(NSString *)filterJson
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int64_t n = 0;
  int status = nuvexa_count((nuvexa_handle)handle.longLongValue, collection.UTF8String, filterJson.UTF8String, &n);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(@(n));
}

RCT_EXPORT_METHOD(checkpoint:(nonnull NSNumber *)handle
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_checkpoint((nuvexa_handle)handle.longLongValue);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(backup:(nonnull NSNumber *)handle
                  destPath:(NSString *)destPath
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_backup((nuvexa_handle)handle.longLongValue, destPath.UTF8String);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(compact:(nonnull NSNumber *)handle
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_compact((nuvexa_handle)handle.longLongValue);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(restore:(NSString *)backupPath
                  destPath:(NSString *)destPath
                  overwrite:(BOOL)overwrite
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_restore(backupPath.UTF8String, destPath.UTF8String, overwrite ? 1 : 0);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(stats:(nonnull NSNumber *)handle
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  char *json = NULL;
  int status = nuvexa_stats((nuvexa_handle)handle.longLongValue, &json);
  if (status != NUVEXA_OK) {
    if (json) nuvexa_free(json);
    Reject(reject, status);
    return;
  }
  resolve(TakeString(json));
}

RCT_EXPORT_METHOD(changeEncryptionKey:(nonnull NSNumber *)handle
                  currentKey:(NSString *)currentKey
                  nextKey:(NSString *)nextKey
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_change_encryption_key((nuvexa_handle)handle.longLongValue, currentKey.UTF8String, nextKey.UTF8String);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(beginTransaction:(nonnull NSNumber *)handle
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_begin_transaction((nuvexa_handle)handle.longLongValue);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(commit:(nonnull NSNumber *)handle
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_commit((nuvexa_handle)handle.longLongValue);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(rollback:(nonnull NSNumber *)handle
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int status = nuvexa_rollback((nuvexa_handle)handle.longLongValue);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(nil);
}

RCT_EXPORT_METHOD(uploadFile:(nonnull NSNumber *)handle
                  fileName:(NSString *)fileName
                  sourcePath:(NSString *)sourcePath
                  chunkSize:(nonnull NSNumber *)chunkSize
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  char *id = NULL;
  int status = nuvexa_fs_upload((nuvexa_handle)handle.longLongValue, fileName.UTF8String, sourcePath.UTF8String, chunkSize.intValue, &id);
  if (status != NUVEXA_OK) {
    if (id) nuvexa_free(id);
    Reject(reject, status);
    return;
  }
  resolve(TakeString(id));
}

RCT_EXPORT_METHOD(downloadFile:(nonnull NSNumber *)handle
                  fileId:(NSString *)fileId
                  destPath:(NSString *)destPath
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  int32_t found = 0;
  int status = nuvexa_fs_download((nuvexa_handle)handle.longLongValue, fileId.UTF8String, destPath.UTF8String, &found);
  if (status != NUVEXA_OK) {
    Reject(reject, status);
    return;
  }
  resolve(@(found != 0));
}

RCT_EXPORT_METHOD(fileMetadata:(nonnull NSNumber *)handle
                  fileId:(NSString *)fileId
                  resolve:(RCTPromiseResolveBlock)resolve
                  reject:(RCTPromiseRejectBlock)reject)
{
  char *json = NULL;
  int status = nuvexa_fs_metadata((nuvexa_handle)handle.longLongValue, fileId.UTF8String, &json);
  if (status == NUVEXA_NOT_FOUND) {
    if (json) nuvexa_free(json);
    resolve([NSNull null]);
    return;
  }
  if (status != NUVEXA_OK) {
    if (json) nuvexa_free(json);
    Reject(reject, status);
    return;
  }
  resolve(TakeString(json));
}

@end
