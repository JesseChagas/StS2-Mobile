using Sts2.SteamClientPrototype;

NativeGns.CheckAvailability();
if (NativeGns.TransportRevision != 7) throw new Exception("The rebuilt native transport was not loaded.");
NativeGns.SelfTest();
NativeGns.HostSelfTest();
using (var native = new NativeGns(76561198000000001, 2868840)) {
    var count = native.LocalAddressCount();
    if (count < 0) Console.WriteLine("SKIP: OS network enumeration is restricted in this execution environment; use the native C address-list fixture to test the production native parser.");
    else Console.WriteLine($"INFO: OS network enumeration returned {count} addresses.");
    Console.WriteLine("PASS: native revision 7 and encrypted socket-pair/UDP roundtrip.");
}
// Destruction must leave the library reusable after discovery.
NativeGns.SelfTest();
Console.WriteLine("PASS: network discovery cleanup and native reinitialization.");
