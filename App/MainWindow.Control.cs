using System.Xml.Linq;
namespace VmixSubtitleController;
public partial class MainWindow
{
 private readonly List<string> _controlCardOrder=new();
 private void SetControlCardOrder(string[] ids){
  var known=_slots.Select(x=>$"slot-{x.Number}").Concat(_playlists.Select(x=>$"playlist-{x.Id}")).ToHashSet();
  if(ids.Length>10000||ids.Distinct().Count()!=ids.Length||ids.Any(id=>!known.Contains(id)))throw new InvalidOperationException("Kart sırası geçersiz.");
  var remainder=_controlCardOrder.Where(id=>known.Contains(id)&&!ids.Contains(id)).ToArray();_controlCardOrder.Clear();_controlCardOrder.AddRange(ids);_controlCardOrder.AddRange(remainder);
 }
 private void MoveControlCard(string source,string target){
  var known=_slots.Select(x=>$"slot-{x.Number}").Concat(_playlists.Select(x=>$"playlist-{x.Id}")).ToList();
  if(source==target)return;
  if(!known.Contains(source)||!known.Contains(target))throw new InvalidOperationException("Sıralanacak kart bulunamadı.");
  var ordered=_controlCardOrder.Where(known.Contains).Concat(known).Distinct().ToList();
  var sourceIndex=ordered.IndexOf(source);var targetIndex=ordered.IndexOf(target);
  ordered.RemoveAt(sourceIndex);ordered.Insert(targetIndex,source);
  _controlCardOrder.Clear();_controlCardOrder.AddRange(ordered);
 }
 private readonly SemaphoreSlim _controlActionGate=new(1);
 private async Task RunControlActionAsync(string action,string id){
  if(!await _controlActionGate.WaitAsync(0))throw new InvalidOperationException("Önceki video işlemi sürüyor.");
  try{
   _revision++;
   if(action=="slot"){await PrepareControlSlotAsync(int.Parse(id));return;}
   if(action=="list"){await PrepareControlListAsync(id);return;}
   if(_preparedControlCard!=id){
    if(id.StartsWith("slot-")&&int.TryParse(id[5..],out var number))await PrepareControlSlotAsync(number,false);
    else if(id.StartsWith("playlist-"))await PrepareControlListAsync(id[9..],false);
    else throw new InvalidOperationException("Video kartı bulunamadı.");
    return;
   }
   await PlayPreparedControlAsync(id);
  }finally{_controlActionGate.Release();}
 }
 private async Task SkipControlListAsync(string id,int direction){
  if(direction is not(-1 or 1))throw new ArgumentException("Geçiş yönü geçersiz.");
  if(!await _controlActionGate.WaitAsync(0))throw new InvalidOperationException("Önceki video işlemi sürüyor.");
  try{
   if(_playingPlaylistId!=id||_playlistQueue.Count==0)throw new InvalidOperationException("Önce bu listeyi hazırlayın veya başlatın.");
   var index=_playlistIndex+direction;if(index<0||index>=_playlistQueue.Count)return;
   var prepared=_preparedControlCard==$"playlist-{id}";
   await PlayPlaylistItemAsync(index,prepared);_playlistActive=!prepared;
   if(prepared)_preparedControlCard=$"playlist-{id}";
  }finally{_controlActionGate.Release();}
 }
 private string? _preparedControlCard;
 private async Task PrepareControlSlotAsync(int number,bool prepareOnly=true){
  var slot=_slots.FirstOrDefault(x=>x.Number==number)??throw new InvalidOperationException("Slot bulunamadı.");
  if(string.IsNullOrWhiteSpace(slot.InputKey))throw new InvalidOperationException("Slot videosunu seçin.");
  ResetPlaylistPlayback();_preparedControlCard=null;_playlistQueue.Add(slot.InputKey);
  try{await PlayPlaylistItemAsync(0,prepareOnly);_playlistQueue.Clear();_playlistIndex=-1;_broadcastCardId=$"slot-{number}";_broadcastCardInputKey=slot.InputKey;_preparedControlCard=prepareOnly?_broadcastCardId:null;if(!prepareOnly)await EnsureControlPlaybackStartedAsync(slot.InputKey);GlobalStatus.Text=prepareOnly?"Video çıkışta hazır; oynatılmıyor.":"Video PGM’de oynatılıyor.";}
  catch{ResetPlaylistPlayback();throw;}
 }
 private async Task PrepareControlListAsync(string id,bool prepareOnly=true){
  var list=_playlists.FirstOrDefault(x=>x.Id==id&&x.OutputMode==_outputMode)??throw new InvalidOperationException("Liste bulunamadı.");
  if(list.Videos.Count==0)throw new InvalidOperationException("Liste boş.");
  ResetPlaylistPlayback();_preparedControlCard=null;await RefreshPlaylistDurationsAsync(list);_selectedPlaylistId=list.Id;_playingPlaylistId=list.Id;_playlistQueue.AddRange(list.Videos.Select(x=>x.Key));
  try{await PlayPlaylistItemAsync(0,prepareOnly);_preparedControlCard=prepareOnly?$"playlist-{list.Id}":null;if(!prepareOnly){await EnsureControlPlaybackStartedAsync(list.Videos[0].Key);_playlistActive=true;}GlobalStatus.Text=prepareOnly?"Çıkışta hazır; ilk video başında duraklatıldı.":"Liste PGM’de oynatılıyor.";}catch{ResetPlaylistPlayback();throw;}
 }
 private async Task PlayPreparedControlAsync(string cardId){
  if(_refreshing)throw new InvalidOperationException("Video geçişi sürüyor.");
  if(_preparedControlCard!=cardId||_broadcastCardId!=cardId||string.IsNullOrWhiteSpace(_broadcastCardInputKey))throw new InvalidOperationException("Önce bu kartı çıkışta hazırlayın.");
  var key=_broadcastCardInputKey;_refreshing=true;
  try{
   if(_outputMode=="vmix"){
    if(string.IsNullOrWhiteSpace(_apiBase))throw new InvalidOperationException("vMix’e bağlanın.");
    var xml=XDocument.Parse(await _http.GetStringAsync(_apiBase,_lifetime.Token));var active=xml.Root?.Element("active")?.Value;var input=xml.Descendants("input").FirstOrDefault(x=>(string?)x.Attribute("key")==key);
    if(input==null||(string?)input.Attribute("number")!=active)throw new InvalidOperationException("Hazırlanan video artık PGM’de değil; tekrar hazırlayın.");
    using var response=await _http.GetAsync($"{_apiBase.TrimEnd('/')}/?Function=Play&Input={Uri.EscapeDataString(key)}",_lifetime.Token);response.EnsureSuccessStatusCode();
   }else{
    if(_obsClient?.IsConnected!=true)throw new InvalidOperationException("OBS’ye bağlanın.");
    var scene=await _obsClient.GetCurrentProgramSceneAsync(_lifetime.Token);var items=await _obsClient.GetSceneItemsAsync(scene,_lifetime.Token);
    if(!items.Any(x=>x.Name.Equals(key,StringComparison.OrdinalIgnoreCase)&&x.Enabled))throw new InvalidOperationException("Hazırlanan video OBS çıkışında değil; tekrar hazırlayın.");
    await _obsClient.PlayMediaInputAsync(key,_lifetime.Token);
   }
   await EnsureControlPlaybackStartedAsync(key);
   _playlistActive=_playingPlaylistId!=null&&_playlistQueue.Count>0;_playlistTransitionUntil=DateTime.UtcNow.AddSeconds(2);_videoIsPlaying=true;_preparedControlCard=null;GlobalStatus.Text="Çıkışta hazır video oynatılıyor.";
  }finally{_refreshing=false;}
 }
 private async Task EnsureControlPlaybackStartedAsync(string key){
  var stable=0;
  for(var attempt=0;attempt<6;attempt++){
   await Task.Delay(120,_lifetime.Token);
   if(_outputMode=="vmix"){
    if(string.IsNullOrWhiteSpace(_apiBase))throw new InvalidOperationException("vMix bağlantısı kesildi.");
    var xml=XDocument.Parse(await _http.GetStringAsync(_apiBase,_lifetime.Token));var input=xml.Descendants("input").FirstOrDefault(x=>(string?)x.Attribute("key")==key);
    if(input==null)throw new InvalidOperationException("Oynatılacak video bulunamadı.");
    if((string?)input.Attribute("number")!=xml.Root?.Element("active")?.Value){stable=0;continue;}
    var state=(string?)input.Attribute("state");
    if(state is "Running" or "Playing"){if(++stable>=2)return;continue;}
    stable=0;using var response=await _http.GetAsync($"{_apiBase.TrimEnd('/')}/?Function=Play&Input={Uri.EscapeDataString(key)}",_lifetime.Token);response.EnsureSuccessStatusCode();
   }else{
    if(_obsClient?.IsConnected!=true)throw new InvalidOperationException("OBS bağlantısı kesildi.");
    var media=await _obsClient.GetMediaStatusAsync(key,_lifetime.Token);
    if(media.State=="OBS_MEDIA_STATE_PLAYING"){if(++stable>=2)return;continue;}
    if(media.State=="OBS_MEDIA_STATE_ENDED")return;
    stable=0;await _obsClient.PlayMediaInputAsync(key,_lifetime.Token);
   }
  }
  throw new InvalidOperationException("Video çıkışa alındı ancak oynatma doğrulanamadı. OBS/vMix video durumunu kontrol edin.");
 }

}
