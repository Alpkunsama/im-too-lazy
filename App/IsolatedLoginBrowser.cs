using System.Diagnostics;
using System.IO;
namespace VmixSubtitleController;

// OAuth stays in a full external browser, with no access to the user's regular browser profile.
internal static class IsolatedLoginBrowser
{
 public static void Open(string authorizationUrl)
 {
  if(!Uri.TryCreate(authorizationUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="auth.openai.com")
   throw new ArgumentException("ChatGPT giriş adresi geçersiz.");
  var roots=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)};
  var edge=roots.Where(root=>!string.IsNullOrWhiteSpace(root)).Select(root=>Path.Combine(root,"Microsoft","Edge","Application","msedge.exe")).FirstOrDefault(File.Exists);
  var chrome=roots.Where(root=>!string.IsNullOrWhiteSpace(root)).Select(root=>Path.Combine(root,"Google","Chrome","Application","chrome.exe")).FirstOrDefault(File.Exists);
  var browser=edge??chrome??throw new InvalidOperationException("Bağımsız ChatGPT girişi için Microsoft Edge veya Google Chrome gerekiyor. Mevcut tarayıcı profili kullanılmayacak.");
  var profileDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"im-too-lazy","chatgpt-login",Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(profileDirectory);
  var start=new ProcessStartInfo(browser){UseShellExecute=false};
  start.ArgumentList.Add("--user-data-dir="+profileDirectory);
  start.ArgumentList.Add(edge!=null?"--inprivate":"--incognito");
  start.ArgumentList.Add("--no-first-run");
  start.ArgumentList.Add("--no-default-browser-check");
  start.ArgumentList.Add("--disable-sync");
  start.ArgumentList.Add("--disable-background-mode");
  start.ArgumentList.Add("--new-window");
  start.ArgumentList.Add(authorizationUrl);
  using var process=Process.Start(start)??throw new InvalidOperationException("Bağımsız ChatGPT giriş penceresi açılamadı.");
 }
}
