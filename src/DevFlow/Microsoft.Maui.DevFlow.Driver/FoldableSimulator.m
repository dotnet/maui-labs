// Simulator-only private protocol researched by artemnovichkov/hinge and skarol/duoctl.
// See THIRD-PARTY-NOTICES.txt. Never link this helper into an app.
#import <Foundation/Foundation.h>
#import <mach/mach_time.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct __IOHIDEvent *IOHIDEventRef;
typedef struct __IOHIDEventSystemClient *IOHIDEventSystemClientRef;
extern CFDataRef IOCFSerialize(CFTypeRef, CFOptionFlags);
extern IOHIDEventRef IOHIDEventCreateVendorDefinedEvent(CFAllocatorRef, uint64_t,
    uint32_t, uint32_t, uint32_t, const uint8_t *, CFIndex, uint32_t);
extern IOHIDEventSystemClientRef IOHIDEventSystemClientCreateWithType(CFAllocatorRef, int, CFDictionaryRef);
extern void IOHIDEventSystemClientDispatchEvent(IOHIDEventSystemClientRef, IOHIDEventRef);

@interface NSObject (DevFlowHinge)
+ (BOOL)isAvailable;
- (float)angleDegrees;
- (void)startAngleUpdatesToQueue:(NSOperationQueue *)queue handler:(void (^)(id))handler;
- (void)stopAngleUpdates;
@end

static BOOL sampleAngle(double *result) {
    Class angleClass = NSClassFromString(@"CMAngleManager");
    if (!angleClass || ![angleClass respondsToSelector:@selector(isAvailable)] || ![angleClass isAvailable]) return NO;
    id manager = [[angleClass alloc] init];
    if (![manager respondsToSelector:@selector(startAngleUpdatesToQueue:handler:)] ||
        ![manager respondsToSelector:@selector(stopAngleUpdates)]) return NO;
    __block BOOL received = NO;
    __block double value = NAN;
    NSOperationQueue *queue = [NSOperationQueue mainQueue];
    [manager startAngleUpdatesToQueue:queue handler:^(id angle) {
        value = [angle angleDegrees];
        received = YES;
    }];
    CFAbsoluteTime deadline = CFAbsoluteTimeGetCurrent() + 5;
    while (!received && CFAbsoluteTimeGetCurrent() < deadline)
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.02, false);
    [manager stopAngleUpdates];
    if (!received || !isfinite(value)) {
        fprintf(stderr, "No valid hinge reading arrived within five seconds.\n");
        exit(1);
    }
    *result = value;
    return YES;
}

static BOOL sendAngle(IOHIDEventSystemClientRef client, double angle) {
    NSDictionary *payload = @{
        @"provider": @"com.apple.Virtualization.VirtualMachines",
        @"source": @"hinge-slider-control",
        @"type": @"range",
        @"value": @(angle),
    };
    CFDataRef data = IOCFSerialize((__bridge CFTypeRef)payload, 1);
    if (!data) return NO;
    IOHIDEventRef event = IOHIDEventCreateVendorDefinedEvent(kCFAllocatorDefault,
        mach_absolute_time(), 0xFF61, 0x5B, 0, CFDataGetBytePtr(data), CFDataGetLength(data), 0);
    CFRelease(data);
    if (!event) return NO;
    IOHIDEventSystemClientDispatchEvent(client, event);
    CFRelease(event);
    return YES;
}

static BOOL number(const char *text, double *value) {
    char *end = NULL;
    *value = strtod(text, &end);
    return end != text && *end == '\0' && isfinite(*value);
}

int main(int argc, char **argv) {
    @autoreleasepool {
        BOOL get = argc == 2 && strcmp(argv[1], "get") == 0;
        BOOL set = argc == 4 && strcmp(argv[1], "set") == 0;
        double target = 0, duration = 0;
        if ((!get && !set) || (set && (!number(argv[2], &target) || target < 0 || target > 180 ||
            !number(argv[3], &duration) || duration < 0 || duration > 30))) {
            fprintf(stderr, "Usage: hinge get | hinge set <0..180 degrees> <0..30 seconds>\n");
            return 64;
        }
        double current = 0;
        if (!sampleAngle(&current)) {
            if (get) {
                puts("{\"supported\":false}");
                return 0;
            }
            fprintf(stderr, "This simulator has no hinge angle source.\n");
            return 1;
        }
        if (get) {
            printf("{\"supported\":true,\"angleDegrees\":%.8f}\n", current);
            return 0;
        }
        IOHIDEventSystemClientRef client =
            IOHIDEventSystemClientCreateWithType(kCFAllocatorDefault, 4, NULL);
        if (!client) {
            fprintf(stderr, "Failed to create the simulator HID client.\n");
            return 1;
        }
        CFAbsoluteTime start = CFAbsoluteTimeGetCurrent();
        BOOL ok = YES;
        for (;;) {
            double progress = duration == 0 ? 1 : fmin(1, (CFAbsoluteTimeGetCurrent() - start) / duration);
            if (!sendAngle(client, current + (target - current) * progress)) {
                ok = NO;
                break;
            }
            if (progress >= 1) break;
            CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.02, false);
        }
        // Device-state delivery is asynchronous; do not exit before the last event drains.
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.1, false);
        CFRelease(client);
        if (!ok) fprintf(stderr, "Failed to construct the simulator hinge event.\n");
        return ok ? 0 : 1;
    }
}
