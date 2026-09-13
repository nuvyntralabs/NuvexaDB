#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

@protocol RCTBridgeModule <NSObject>
@end

typedef void (^RCTPromiseResolveBlock)(id _Nullable result);
typedef void (^RCTPromiseRejectBlock)(NSString *code, NSString *_Nullable message, NSError *_Nullable error);

#ifndef RCT_EXPORT_MODULE
#define RCT_EXPORT_MODULE(...) \
  + (NSString *)moduleName { return @"NuvexaDB"; }
#endif

#ifndef RCT_EXPORT_METHOD
#define RCT_EXPORT_METHOD(method) - (void)method
#endif

NS_ASSUME_NONNULL_END
