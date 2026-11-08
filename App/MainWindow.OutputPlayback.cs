using System.Xml.Linq;
namespace VmixSubtitleController;
public partial class MainWindow
{
 private async Task ToggleOutputPauseAsync(){
  if(_refreshing)throw new InvalidOperationException("Video geçişi sürüyor.");
  _refreshing=true;
  try{
   bool play;string? currentKey=null;
   if(_outputMode=="vmix"){
    if(string.IsNullOrWhiteSpace(_apiBase))throw new InvalidOperationException("vMix’e bağlanın.");
    var xml=XDocument.Parse(await _http.GetStringAsync(_apiBase,_lifetime.Token));var active=xml.Root?.Element("active")?.Value;
    var input=xml.Descendants("input").FirstOrDefault(x=>(string?)x.Attribute("number")==active)??throw new InvalidOperationException("PGM girişi bulunamadı.");
    var type=(string?)input.Attribute("type");if(type is not("Video" or "VideoList"))throw new InvalidOperationException("PGM’de duraklatılabilir video yok.");
    currentKey=(string?)input.Attribute("key");play=!string.Equals((string?)input.Attribute("state"),"Running",StringComparison.OrdinalIgnoreCase);
    using var response=await _http.GetAsync($"{_apiBase.TrimEnd('/')}/?Function={(play?"Play":"Pause")}&Input={Uri.EscapeDataString(currentKey!)}",_lifetime.Token);response.EnsureSuccessStatusCode();
   }else{
    if(_obsClient?.IsConnected!=true)throw new InvalidOperationException("OBS’ye bağlanın.");
    var scene=await _obsClient.GetCurrentProgramSceneAsync(_lifetime.Token);var items=await _obsClient.GetSceneItemsAsync(scene,_lifetime.Token);
    var names=_inputs.Where(x=>x.Type.Equals("Video",StringComparison.OrdinalIgnoreCase)).Select(x=>x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var targets=items.Where(x=>x.Enabled&&names.Contains(x.Name)).Select(x=>x.Name).Distinct().ToArray();
    if(targets.Length==0)throw new InvalidOperationException("OBS çıkışında etkin video kaynağı yok.");
    var statuses=new List<(string Name,string State)>();foreach(var name in targets){var status=await _obsClient.GetMediaStatusAsync(name,_lifetime.Token);statuses.Add((name,status.State));}
    if(!statuses.Any(x=>x.State is "OBS_MEDIA_STATE_PLAYING" or "OBS_MEDIA_STATE_PAUSED"))throw new InvalidOperationException("OBS çıkışında oynayan veya duraklatılmış video yok.");
    play=!statuses.Any(x=>x.State=="OBS_MEDIA_STATE_PLAYING");
    foreach(var target in statuses){if(play&&target.State=="OBS_MEDIA_STATE_PAUSED")await _obsClient.PlayMediaInputAsync(target.Name,_lifetime.Token);else if(!play&&target.State=="OBS_MEDIA_STATE_PLAYING")await _obsClient.PauseMediaInputAsync(target.Name,_lifetime.Token);}
    currentKey=targets.FirstOrDefault(x=>x==_broadcastCardInputKey)??targets[0];
   }
   _videoIsPlaying=play;
   if(!play)_playlistActive=false;
   else{if(_playlistIndex>=0&&_playlistIndex<_playlistQueue.Count&&_playlistQueue[_playlistIndex]==currentKey)_playlistActive=true;_preparedControlCard=null;_playlistTransitionUntil=DateTime.UtcNow.AddSeconds(2);}
   GlobalStatus.Text=play?"Çıkıştaki video devam ediyor.":"Çıkıştaki video duraklatıldı.";
  }finally{_refreshing=false;}
 }
}
