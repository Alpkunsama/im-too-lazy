# Windows sürümü

Node bağımlılıklarını `App/WebUI` içinde kilit dosyasından kurun. Derleme için .NET 8 SDK, Node.js, Inno Setup derleyicisi ve Microsoft imzalı x64 Evergreen WebView2 çevrimdışı kurucusu gerekir.

```powershell
./Installer/build.ps1 -Compiler 'C:\tools\Inno Setup\ISCC.exe' -WebViewInstaller 'C:\tools\MicrosoftEdgeWebView2RuntimeInstallerX64.exe' -UpdateFeedUrl 'https://release.example/updates/windows.json'
```

Kurucu `Installer/output` altında oluşturulur. `-UpdateFeedUrl` verilmezse uygulama güncelleme sorgusu yapmaz. Üretimde son sürüm bilgisini yayıncının kontrolündeki sabit HTTPS adresinden doğrudan JSON olarak döndürün. HTTP yönlendirmeleri takip edilmez.

Örnek `windows.json`:

```json
{
  "version": "0.0073",
  "downloadUrl": "https://release.example/downloads/im-too-lazy-0.0073-setup.exe",
  "added": ["İsteğe bağlı güncelleme penceresi"],
  "fixed": ["Kurulumda WebView2 dosyasının yolu yapılandırılabilir hale getirildi"]
}
```

Önce kurucuyu dağıtım adresine koyup indirmeyi doğrulayın, ardından JSON dosyasını güncelleyin. Kullanıcı indirmeyi seçerse varsayılan tarayıcı açılır ve kurulumu kendisi başlatır.
