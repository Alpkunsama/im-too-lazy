using System.Diagnostics;
using System.Windows.Forms;
var path=Path.Combine(AppContext.BaseDirectory,"unins000.exe");
if(!File.Exists(path)){MessageBox.Show("Kaldırıcı bulunamadı. Windows Ayarlar > Uygulamalar bölümünden kaldırın.","im too lazy");return;}
try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=AppContext.BaseDirectory});}
catch(Exception ex){MessageBox.Show(ex.Message,"im too lazy - Kaldırma hatası");}
