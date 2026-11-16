// TEST ONLY. The real game's module initializer calls this before the test main.
// No engine or telemetry is initialized by the command-line test harness.
namespace Sentry.Godot.Internal;
public static class SentryGodotInitializer { public static void AutoInit() { } }
