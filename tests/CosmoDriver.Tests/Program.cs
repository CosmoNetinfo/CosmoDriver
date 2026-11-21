using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CosmoDriver.Core;

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Reject(Action action)
{
    try { action(); } catch { return; }
    throw new Exception("Unexpected acceptance");
}

Test("Empty result is successful only after explicit success", () => Assert(Protocol.Parse("{\"Success\":true,\"Drivers\":[],\"Updates\":[],\"ErrorCode\":null}").Updates.Count == 0));
Test("Invalid JSON rejected", () => Reject(() => Protocol.Parse("broken")));
Test("Null result rejected", () => Reject(() => Protocol.Parse("null")));
Test("Missing lists rejected", () => Reject(() => Protocol.Parse("{\"Success\":true}")));
Test("Null driver rejected", () => Reject(() => Protocol.Parse("{\"Success\":true,\"Drivers\":[null],\"Updates\":[]}")));
Test("Null driver fields rejected", () => Reject(() => Protocol.Parse("{\"Success\":true,\"Drivers\":[{}],\"Updates\":[]}")));
Test("Missing success never means updated", () => Reject(() => Protocol.Parse("{\"Drivers\":[],\"Updates\":[]}")));
Test("Failed scan never means no updates", () => Reject(() => Protocol.Parse("{\"Success\":false,\"Drivers\":[],\"Updates\":[],\"ErrorCode\":\"PARTIAL\"}")));
Test("Network and permission errors distinguished", () => Assert(Protocol.FriendlyError("0x80070005").Contains("Accesso negato") && Protocol.FriendlyError("0x80072EE7").Contains("raggiungibile")));
Test("Driver date parsed without age heuristic", () => Assert(Protocol.DriverDate("20060621000000.000000+000") == "2006-06-21"));
Test("Invalid dates remain unknown", () => Assert(Protocol.DriverDate("20261399") == "Non disponibile" && Protocol.DriverDate(null) == "Non disponibile"));
var driver = new DriverInfo("Scheda VIDEO", "Cosmo", "1.2.3", "2006-06-21", "DISPLAY", null, "oem1.inf");
Test("Search covers name, class, version and INF", () => Assert(driver.Matches("video") && driver.Matches("display") && driver.Matches("1.2.3") && driver.Matches("OEM1") && !driver.Matches("network")));
Test("Unknown signature stays unknown", () => Assert(driver.Signature == "Non disponibile"));
Test("WMI DBNull signature remains unknown (real-device regression)", () => Assert(Protocol.SignatureValue(DBNull.Value) is null && Protocol.SignatureValue(null) is null));
Test("WMI true and false signatures are preserved", () => Assert(Protocol.SignatureValue(true) == true && Protocol.SignatureValue(false) == false));
Test("Report preserves unknown update status and coverage", () =>
{
    var report = new ScanReport(DateTimeOffset.Now, DateTimeOffset.Now, null, "Non verificato", [driver], []);
    string serialized = JsonSerializer.Serialize(report);
    Assert(serialized.Contains("Non verificato") && serialized.Contains("Coverage") && report.UpdatesAt is null);
});
using var key = RSA.Create(2048);
string publicKey = key.ExportSubjectPublicKeyInfoPem();
byte[] payload = Encoding.UTF8.GetBytes(new string('x', 4096));
var manifest = new ReleaseManifest("1.2.3", "CosmoDriver.exe", Convert.ToHexString(SHA256.HashData(payload)), payload.Length);
byte[] Data(ReleaseManifest m) => JsonSerializer.SerializeToUtf8Bytes(m);
byte[] Sign(byte[] data) => key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
ReleaseManifest Verify(ReleaseManifest m) { var data = Data(m); return UpdateTrust.VerifyManifest(data, Sign(data), publicKey); }
Test("Valid signed manifest accepted", () => Assert(Verify(manifest) == manifest));
Test("Modified manifest rejected", () => { var data = Data(manifest); byte[] sig = Sign(data); data[8] ^= 1; Reject(() => UpdateTrust.VerifyManifest(data, sig, publicKey)); });
Test("Wrong signing key rejected", () => { using var other = RSA.Create(2048); var data = Data(manifest); Reject(() => UpdateTrust.VerifyManifest(data, Sign(data), other.ExportSubjectPublicKeyInfoPem())); });
Test("Unsigned manifest rejected by pinned production key", () => { var data = Data(manifest); Reject(() => UpdateTrust.VerifyManifest(data, Sign(data))); });
Test("Path traversal in payload name rejected", () => Reject(() => Verify(manifest with { FileName = "../CosmoDriver.exe" })));
Test("Invalid hash rejected", () => Reject(() => Verify(manifest with { Sha256 = new string('z', 64) })));
Test("Oversized download rejected", () => Reject(() => Verify(manifest with { Size = long.MaxValue })));
Test("Pre-release version rejected", () => Reject(() => Verify(manifest with { Version = "1.2.3-beta" })));
Test("Noncanonical version rejected", () => Reject(() => Verify(manifest with { Version = "01.2.3" })));
Test("Four-part version rejected", () => Reject(() => Verify(manifest with { Version = "1.2.3.4" })));
Test("Valid payload hash accepted", () => { using var stream = new MemoryStream(payload); UpdateTrust.VerifyPayload(stream, manifest); Assert(stream.Position == 0); });
Test("Modified payload rejected", () => { byte[] changed = (byte[])payload.Clone(); changed[9] ^= 1; using var stream = new MemoryStream(changed); Reject(() => UpdateTrust.VerifyPayload(stream, manifest)); });
Test("Truncated payload rejected", () => { using var stream = new MemoryStream(payload[..^1]); Reject(() => UpdateTrust.VerifyPayload(stream, manifest)); });
Test("Release URL restricted to repository and asset allowlist", () =>
{
    Assert(UpdateTrust.AssetUri("1.2.3", "CosmoDriver.exe").AbsoluteUri == "https://github.com/CosmoNetinfo/CosmoDriver/releases/download/v1.2.3/CosmoDriver.exe");
    Reject(() => UpdateTrust.AssetUri("1.2.3/../../", "CosmoDriver.exe"));
    Reject(() => UpdateTrust.AssetUri("1.2.3", "evil.exe"));
});
var installRequest = new InstallRequest(Guid.NewGuid().ToString("D"), 42, InstallPolicy.EulaHash("license text"), true);
Test("Confirmed update identity accepted", () => InstallPolicy.Validate(installRequest));
Test("Unconfirmed installation rejected", () => Reject(() => InstallPolicy.Validate(installRequest with { UserConfirmed = false })));
Test("Update ID query injection rejected", () => Reject(() => InstallPolicy.Validate(installRequest with { UpdateId = "' or Type='Software'" })));
Test("Negative revision rejected", () => Reject(() => InstallPolicy.Validate(installRequest with { Revision = -1 })));
Test("Missing EULA consent hash rejected", () => Reject(() => InstallPolicy.Validate(installRequest with { EulaSha256 = "" })));
Test("Changed EULA requires new consent", () => Assert(InstallPolicy.EulaHash("license text") != InstallPolicy.EulaHash("new license")));
Test("Unknown or forged vendor never produces a URL", () => Assert(InstallPolicy.VendorSupport("Intel Corporation evil") is null && InstallPolicy.VendorSupport("https://evil.example") is null));
Test("Curated official vendor support only", () => Assert(InstallPolicy.VendorSupport("Intel Corporation")?.Host == "www.intel.com" && InstallPolicy.VendorSupport("NVIDIA")?.Scheme == "https"));
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
