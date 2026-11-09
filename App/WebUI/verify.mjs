import {chromium} from '@playwright/test';
import {createServer} from 'node:http';import {readFileSync,mkdirSync} from 'node:fs';import {strict as assert} from 'node:assert';
const server=createServer((q,r)=>{const name=q.url==='/'?'index.html':q.url.slice(1);if(!['index.html','app.js','app.css','brand-cat.png'].includes(name)){r.writeHead(404).end();return;}r.setHeader('Content-Type',name.endsWith('.js')?'application/javascript':name.endsWith('.css')?'text/css':name.endsWith('.png')?'image/png':'text/html');r.end(readFileSync('dist/'+name));});await new Promise(r=>server.listen(0,'127.0.0.1',r));
const browser=await chromium.launch({executablePath:process.env.IM_LAZY_CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});let checks=0;
const check=(v,n)=>{assert.ok(v,n);checks++;console.log('PASS: '+n);};
try{const page=await browser.newPage({viewport:{width:1920,height:1080}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
await page.addInitScript(()=>{
const s={type:'state',controlCardOrder:['slot-1'],appVersion:'0.0025',startup:true,startupOutputChosen:false,loginBusy:false,page:'home',appLanguage:'tr',theme:'graphite',savedTheme:'graphite',language:'Türkçe',translationPreset:'normal',status:'Hazır',program:'Program: test',api:'Ready',outputMode:'vmix',broadcastCard:null,vmix:{connected:true,text:'Connected',programInputKey:'v1',previewInputKey:'v2'},obs:{connected:false,status:'OBS bağlı değil',hosts:[],host:'127.0.0.1',port:4455},account:{connected:true,text:'Test hesabı',button:'ChatGPT hesabını bağla'},usage:{translations:1,cues:2,status:'Tamamlandı',lastActivity:'—'},models:[{key:'test',label:'Test'}],model:'test',inputs:[{key:'v1',label:'1 — First video'},{key:'v2',label:'2 — Second video'}],titles:[{key:'t1',label:'Title'}],title:'t1',profiles:[{name:'Test profile',path:'test.itlp.json',active:true}],glossary:[],languages:[{key:'Türkçe',label:'Türkçe'},{key:'İngilizce',label:'İngilizce'}],slots:Array.from({length:4},(_,i)=>({number:i+1,language:'Türkçe',sourceLanguage:'EN',input:i<2?'v'+(i+1):'',inputName:i?'Second video':'First video',file:i<2?'test.srt':'',status:i<2?'Hazır':'Bekliyor',preview:'Test subtitle',ready:i<2,translating:false})),playlist:{active:false,index:-1,selectedId:'list1',playingId:null,timing:{active:false,totalMs:null,elapsedMs:null,remainingMs:null,currentTitle:''},lists:[{id:'list1',name:'Opening',count:2,available:true,videoLabels:['First video','Second video']}],items:[{key:'v1',label:'First video',number:1,available:true},{key:'v2',label:'Second video',number:2,available:true}]},video:{playing:false,positionMs:1000,durationMs:60000,remainingMs:59000,source:'First video'},omt:{sources:[],source:'',connected:false},detail:{number:1,file:'test.srt',input:'v1',count:2,index:0,version:1,original:'Hello',text:'Merhaba',editable:true,dirty:false,language:'Türkçe',status:'Hazır',timing:'00:00:01,000'},settingsStatus:''};let fn;window.__state=s;window.__commands=[];window.__emit=()=>fn?.({data:JSON.parse(JSON.stringify(s))});window.__success=()=>fn?.({data:{type:'login-success'}});window.chrome=window.chrome||{};window.chrome.webview={addEventListener:(_,f)=>fn=f,postMessage:m=>{window.__commands.push(m);let value;
if(m.command==='chooseOutputMode'){s.outputMode=m.target;s.startupOutputChosen=true;}if(m.command==='startupContinue'||m.command==='skipLogin')s.startup=false;
if(m.command==='page')s.page=m.page;if(m.command==='input'){s.slots.find(x=>x.number===m.slot).input=m.key;s.detail.input=m.key;}
if(m.command==='slotLanguage')s.slots.find(x=>x.number===m.slot).language=m.language;
if(m.command==='controlSetCardVisibility')s.controlCardOrder=m.visible?[...new Set([...s.controlCardOrder,m.cardId])]:s.controlCardOrder.filter(id=>id!==m.cardId);
if(m.command==='cacheStats')value={count:12,bytes:4096};if(m.command==='cacheClear')value={count:0,bytes:0};
if(m.command==='theme')s.theme=m.theme;if(m.command==='translationPreset')s.translationPreset=m.preset;
if(m.command==='playlistStart'){s.playlist.active=true;s.playlist.playingId='list1';s.playlist.index=0;s.broadcastCard='playlist-list1';s.playlist.timing={active:true,totalMs:120000,elapsedMs:1000,remainingMs:119000,currentTitle:'First video'};}
queueMicrotask(()=>{window.__emit();fn?.({data:{type:'result',id:m.id,ok:true,value}});});}};
});
await page.goto('http://127.0.0.1:'+server.address().port);await page.getByRole('button',{name:'vMix kullan',exact:true}).click();await page.getByRole('button',{name:'Bağlı hesapla devam et',exact:true}).click();await page.getByRole('dialog',{name:'Profil seç',exact:true}).waitFor();check(true,'Startup provider and profile picker render');await page.getByRole('button',{name:'Test profile',exact:true}).click();await page.getByRole('dialog',{name:'Profil seç',exact:true}).waitFor({state:'hidden'});
const nav=page.locator('nav');await nav.getByRole('button',{name:'Broadcast',exact:true}).click();await page.getByRole('tab',{name:/Altyazı/}).click();check(await page.locator('.camera-preview-card:visible').count()===0,'Subtitle tab hides camera monitor');
const secondSlot=page.locator('.slot-row').nth(1);
await secondSlot.getByRole('button',{name:'Control’e ekle',exact:true}).click();
await secondSlot.getByRole('button',{name:'Control’den çıkar',exact:true}).waitFor();
check(await page.locator('[data-control-card="slot-2"]').count()===1,'Prepared subtitle is added to Control explicitly');
const slotLanguage=page.getByRole('combobox',{name:'Slot 1 çeviri dili',exact:true});
await slotLanguage.click();await page.getByRole('option',{name:'İngilizce',exact:true}).click();
await slotLanguage.getByText('İngilizce',{exact:true}).waitFor();
check(await page.getByRole('combobox',{name:'Slot 2 çeviri dili',exact:true}).getByText('Türkçe',{exact:true}).count()===1,'Slot languages remain independent');
const sel=page.getByRole('combobox',{name:'Slot 1 video girişi',exact:true});
await sel.click();await page.evaluate(()=>window.__emit());
check(await sel.getAttribute('aria-expanded')==='true','Dropdown stays open during live updates');
await page.getByRole('option',{name:'2 — Second video',exact:true}).click();
await sel.getByText('2 — Second video',{exact:true}).waitFor();
await page.evaluate(()=>window.__emit());check(await sel.getByText('2 — Second video',{exact:true}).count()===1,'Video selection survives live state update');
const refresh=page.getByRole('button',{name:'Girişleri yenile',exact:true});
const refreshPosition=await refresh.boundingBox();
const checkRefreshPosition=async()=>{const position=await refresh.boundingBox();assert.ok(position&&refreshPosition&&Math.abs(position.x-refreshPosition.x)<1&&Math.abs(position.y-refreshPosition.y)<1);};
await page.getByRole('tab',{name:/Oynatma Listesi/}).click();
check(await page.getByRole('combobox',{name:'Oynatma listesi seç'}).getByText('Opening (2)',{exact:true}).count()===1,'Custom playlist selection restored');
const playlistPanel=page.locator('#playlist-panel');
check(await page.locator('[data-control-card="playlist-list1"]').count()===0,'Prepared playlist stays off Control until added');
await playlistPanel.getByRole('button',{name:'Control’e ekle',exact:true}).click();
await playlistPanel.getByRole('button',{name:'Control’den çıkar',exact:true}).waitFor();
check(await page.locator('[data-control-card="playlist-list1"]').count()===1,'Playlist can be added to Control');
await playlistPanel.getByRole('button',{name:'Control’den çıkar',exact:true}).click();
await playlistPanel.getByRole('button',{name:'Control’e ekle',exact:true}).waitFor();
check(await page.locator('[data-control-card="playlist-list1"]').count()===0&&await playlistPanel.locator('.video-playlist-item').count()===2,'Removing a Control card preserves the playlist');
await playlistPanel.getByRole('button',{name:'Control’e ekle',exact:true}).click();
await playlistPanel.getByRole('button',{name:'Control’den çıkar',exact:true}).waitFor();
await checkRefreshPosition();
await refresh.click();
check(await page.evaluate(()=>window.__commands.some(m=>m.command==='refresh')),'Shared refresh sends the vMix command');
await page.setViewportSize({width:1366,height:768});
for(const {tab,panel,list,row} of [
  {tab:/Oynatma Listesi/,panel:'#playlist-panel',list:'.video-playlist',row:'.video-playlist-item'},
  {tab:/Altyazı/,panel:'#workspace-panel',list:'.table-scroll',row:'.slot-row'}
]){
  await page.getByRole('tab',{name:tab}).click();
  const card=page.locator(panel).locator('[data-slot="card"]');
  const scroller=card.locator(list);
  const before=await card.boundingBox();
  const toolbar=await page.locator('.broadcast-toolbar').boundingBox();
  check(before&&toolbar&&before.y-toolbar.y-toolbar.height<32,'Preparation panel starts below toolbar '+panel);
  await page.evaluate(panel=>{
    const state=window.__state;
    window.__smallPreparationList=panel==='#workspace-panel'?state.slots:state.playlist.items;
    const source=window.__smallPreparationList;
    const rows=Array.from({length:32},(_,i)=>({...source[i%source.length],number:i+1}));
    if(panel==='#workspace-panel')state.slots=rows;
    else state.playlist.items=rows;
    window.__emit();
  },panel);
  await scroller.locator(row).nth(31).waitFor({state:'attached'});
  const after=await card.boundingBox();
  check(after&&Math.abs(before.y-after.y)<1&&Math.abs(before.height-after.height)<1,'Adding rows preserves panel geometry '+panel);
  check(await scroller.evaluate(element=>{
    element.scrollTop=element.scrollHeight;
    return element.scrollTop>0&&element.scrollHeight-element.scrollTop-element.clientHeight<2;
  }),'Added rows scroll inside the panel '+panel);
  await page.evaluate(panel=>{
    if(panel==='#workspace-panel')window.__state.slots=window.__smallPreparationList;
    else window.__state.playlist.items=window.__smallPreparationList;
    window.__emit();
  },panel);
}
await page.setViewportSize({width:1920,height:1080});
await page.getByRole('tab',{name:/Oynatma Listesi/}).click();
await page.getByRole('button',{name:'Sırayla oynat',exact:true}).click();
await page.getByRole('tab',{name:/Control/}).click();await checkRefreshPosition();
const listCard=page.locator('.broadcast-scene').filter({hasText:'Opening'});
await listCard.getByText('1 / 2 · First video',{exact:true}).waitFor();
check(await listCard.getByRole('button',{name:'Sonraki',exact:true}).isEnabled(),'Control shows current playlist position and next video action');
for(const viewport of [{width:1920,height:1080},{width:1366,height:768},{width:1100,height:900}]){await page.setViewportSize(viewport);await page.waitForTimeout(150);const box=await page.locator('.camera-frame').boundingBox();check(Math.abs(box.width/box.height-16/9)<0.025,'Video ratio 16:9 at '+viewport.width);const preview=await page.locator('.subtitle-preview-card').boundingBox();if(viewport.width===1366){await page.screenshot({path:'test-output/compact.png'});}check(preview.y+preview.height<=viewport.height,'Subtitle preview fits viewport '+viewport.width);}await page.setViewportSize({width:1920,height:1080});
for(const name of ['Ayarlar','Profiller','SRT Editörü','Çeviri Sözlüğü','Çeviri','Ana Sayfa','Ayarlar']){await nav.getByRole('button',{name,exact:true}).click();check(await page.locator('header h1').innerText()===name||name==='Ana Sayfa','Navigation '+name);}
await page.getByRole('button',{name:'Çeviri',exact:true}).last().click();await page.getByRole('heading',{name:'Çeviri önbelleği',exact:true}).waitFor();check(await page.getByText(/12 kayıt/).count()>0,'Cache statistics shown');await page.getByRole('button',{name:'Önbelleği temizle',exact:true}).click();await page.getByText('Önbellek temizlendi. Profildeki SRT ve hazır çeviriler korundu.').waitFor();check(true,'Cache clear command and result shown');
await page.getByRole('button',{name:'Bağlantı ayarları',exact:true}).click();check(await page.getByRole('heading',{name:/vMix bağlantısı/i,exact:true}).count()===1&&await page.getByRole('heading',{name:/OBS Studio bağlantısı/i,exact:true}).count()===0,'Only chosen provider settings shown');
await page.evaluate(()=>{window.__state.outputMode='obs';window.__emit();});await page.getByRole('heading',{name:/OBS Studio bağlantısı/i,exact:true}).waitFor();check(await page.getByRole('heading',{name:/vMix bağlantısı/i,exact:true}).count()===0,'OBS mode hides vMix settings');
await page.evaluate(()=>window.__success());await page.getByRole('dialog',{name:'ChatGPT bağlantısı başarılı'}).waitFor();await page.evaluate(()=>window.__emit());await page.waitForTimeout(1100);check(await page.getByRole('dialog',{name:'ChatGPT bağlantısı başarılı'}).count()===1,'Welcome stays until user dismisses');await page.getByRole('button',{name:'Hadi başlayalım',exact:true}).click();
mkdirSync('test-output',{recursive:true});await page.screenshot({path:'test-output/verified-settings.png'});check(errors.length===0,'No JavaScript runtime errors');console.log(checks+' UI checks passed');
}finally{await browser.close();server.close();}
