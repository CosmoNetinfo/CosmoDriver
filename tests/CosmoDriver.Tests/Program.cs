using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
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
string tempRoot = Path.Combine(Path.GetTempPath(), "CosmoDriver-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    string source = Path.Combine(tempRoot, "source"); Directory.CreateDirectory(source);
    string inf = Path.Combine(source, "driver.inf"); File.WriteAllText(inf, "[Version]\r\nSignature=\"$Windows NT$\"\r\n");
    Directory.CreateDirectory(Path.Combine(source, "nested")); File.WriteAllText(Path.Combine(source, "nested", "driver.sys"), "binary fixture");
    var files = PackPolicy.Snapshot(source);
    string infHash = files.Single(f => f.RelativePath == "driver.inf").Sha256;
    var pack = new PackInstallRequest("PCI\\TEST", inf, source, files, true, infHash);
    Test("Local package snapshot includes dependencies", () => Assert(files.Count == 2 && files.All(f => f.Sha256.Length == 64)));
    Test("Package consent is required", () => Reject(() => PackPolicy.Validate(pack with { UserConfirmed = false })));
    Test("Traversal outside package is rejected", () => Reject(() => PackPolicy.ContainedPath(source, "../outside.inf")));
    Test("UNC and device paths are rejected", () => Reject(() => PackPolicy.LocalDirectory("\\\\server\\share")));
    Test("Alternate streams rejected", () => Reject(() => PackPolicy.ContainedPath(source, "driver.inf:evil")));
    Test("Manifest without selected INF rejected", () => Reject(() => PackPolicy.Validate(pack with { Files = files.Where(f => f.RelativePath != "driver.inf").ToList() })));
    Test("Different INF than scanned rejected", () => Reject(() => PackPolicy.Validate(pack with { ExpectedInfSha256 = new string('0',64) })));
    Test("Duplicated file destinations rejected", () => Reject(() => PackPolicy.Validate(pack with { Files = [..files,files[0]] })));
    Test("Package over size limit rejected", () => Reject(() => PackPolicy.Validate(pack with { Files = files.Select(f => f with { Size = PackPolicy.MaxBytes }).ToList() })));
    Test("Verified file snapshot can be staged without running it", () =>
    {
        string target = Path.Combine(tempRoot,"staged"); Directory.CreateDirectory(target); PackPolicy.CopyVerified(pack,target);
        Assert(File.ReadAllText(Path.Combine(target,"nested","driver.sys")) == "binary fixture");
    });
    Test("Payload modified after consent is rejected", () =>
    {
        File.WriteAllText(Path.Combine(source,"nested","driver.sys"), "changed payload");
        string target = Path.Combine(tempRoot,"tampered"); Directory.CreateDirectory(target); Reject(() => PackPolicy.CopyVerified(pack,target));
    });
    Test("Cancelled directory scans stop", () => { using var cts = new CancellationTokenSource(); cts.Cancel(); Reject(() => PackPolicy.Snapshot(source,cts.Token)); });
    Test("Unsigned or fake SDIO archive rejected", () => { using var stream = new MemoryStream(new byte[100]); Reject(() => SdioPackage.VerifyArchive(stream)); });
    void ExtractTest(string name, Action<ZipArchive> populate, bool reject)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream,ZipArchiveMode.Create,true)) populate(archive);
        stream.Position = 0;
        using var opened = new ZipArchive(stream,ZipArchiveMode.Read);
        string target = Path.Combine(tempRoot,name); Directory.CreateDirectory(target);
        if (reject) Reject(() => SdioPackage.ExtractBounded(opened,target)); else { SdioPackage.ExtractBounded(opened,target); Assert(File.Exists(Path.Combine(target,"safe.txt"))); }
    }
    Test("ZIP slip rejected before extraction", () => ExtractTest("zip-slip", z => z.CreateEntry("../outside.exe"),true));
    Test("Duplicate ZIP paths rejected", () => ExtractTest("zip-dupe", z => { z.CreateEntry("a.dll"); z.CreateEntry("A.dll"); },true));
    Test("ZIP symlink rejected", () => ExtractTest("zip-link", z => { var entry = z.CreateEntry("link"); entry.ExternalAttributes = unchecked((int)0xA1FF0000); },true));
    Test("Bounded ZIP extraction accepts safe entries", () => ExtractTest("zip-safe", z => { using var writer = new StreamWriter(z.CreateEntry("safe.txt").Open()); writer.Write("safe data"); },false));
}
finally { Directory.Delete(tempRoot,true); }
Test("Fresh verified restore point recognised", () => Assert(RestorePolicy.Evaluate(0,[new(5,"older")],[new(5,"older"),new(6,"expected")],"expected") is { Success:true,Created:true,Sequence:6 }));
Test("API success reusing old restore point is not fresh success", () => Assert(!RestorePolicy.Evaluate(0,[new(5,"older")],[new(5,"older")],"expected").Success));
Test("Restore point API failure stays failure", () => Assert(!RestorePolicy.Evaluate(5,[],[],"expected").Success));
Test("Unverified point does not authorise installation", () => Assert(!RestorePolicy.Evaluate(0,[],[],"expected").Success));
Test("Failed restore point blocks installer callback", () =>
{
    bool called = false;
    var result = RestorePolicy.BeforeInstall(true,() => new(false,null,false,"blocked"),() => { called=true; return new(true,false,"installed"); });
    Assert(!called && !result.Success);
});
Test("Successful fresh restore point permits installer", () => Assert(RestorePolicy.BeforeInstall(true,() => new(true,4,true,"created"),() => new(true,false,"installed")).Success));
Test("Explicit opt out skips restore callback", () =>
{
    bool called=false; Assert(RestorePolicy.BeforeInstall(false,() => { called=true; throw new Exception(); },() => new(true,false,"installed")).Success && !called);
});
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
