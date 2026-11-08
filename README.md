# im too lazy

Windows için vMix ve OBS yayın kontrolü, SRT altyazı yönetimi ve video oynatma listeleri.

## Kurulum

GitHub Releases sayfasından Windows x64 için `im-too-lazy-<sürüm>-setup.exe` dosyasını indirip çalıştırın. Kurucu, eksikse Microsoft Edge WebView2 Runtime’ı da kurar. Uygulama Windows Ayarları’ndaki **Yüklü uygulamalar** bölümünden kaldırılabilir.

İlk sürümün kurulum dosyası imzasızdır. Uygulamayı resmi proje sürümleri sayfasından indirin.

## Kullanım

- Açılışta vMix veya OBS’yi seçin ve **Ayarlar → Bağlantı ayarları** bölümünden bağlantıyı kurun.
- **Broadcast → Altyazı** bölümünde bir video ve SRT dosyası seçin. İsterseniz orijinal altyazıyı kullanabilir veya ChatGPT hesabınızla çeviri yapabilirsiniz.
- **Oynatma Listesi** bölümünde videoları sıraya dizin. **Control** bölümünde tek videoları ve listeleri PGM çıkışına hazırlayın veya oynatın.
- **Profiller** bölümünden yayın ayarlarını ve altyazı çalışmalarınızı kaydedin.

ChatGPT oturumu Windows kullanıcı hesabına bağlı olarak şifrelenir. Çeviriyi seçtiğinizde altyazı metni ve çeviri için kullanılan terimler OpenAI’ye gönderilir. vMix, OBS ve OMT bağlantıları seçtiğiniz yayın kaynaklarıyla kurulur. Güncelleme adresi yapılandırılmışsa uygulama açılışta o adresten sürüm bilgisi alır.

## Derleme

Uygulama .NET 8, WPF ve React kullanır. Derleme ve Windows kurucusu için [sürüm kılavuzuna](Installer/RELEASE.md) bakın.

```powershell
dotnet run --project App/Tests/ReleaseChecks.csproj -c Release
```

## Lisans

Proje [MIT lisansı](LICENSE) ile sunulur. Üçüncü taraf bileşenlerinin koşulları [lisans bildirimlerinde](THIRD-PARTY-NOTICES.txt) yer alır.
