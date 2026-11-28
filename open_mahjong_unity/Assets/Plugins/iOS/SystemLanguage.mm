#import <Foundation/Foundation.h>
#include <string.h>

extern "C" {
  const char* SalasasaPreferredLanguages() {
    NSArray<NSString*>* languages = [NSLocale preferredLanguages];
    if (!languages || languages.count == 0) {
      return strdup("");
    }
    NSString* joined = [languages componentsJoinedByString:@","];
    const char* utf8 = [joined UTF8String];
    return strdup(utf8 ? utf8 : "");
  }
}
