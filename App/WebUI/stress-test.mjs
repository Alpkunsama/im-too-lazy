import {chromium} from '@playwright/test';
import {createServer} from 'node:http';
import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import assert from 'node:assert/strict';
const base=Function('return '+readFileSync('src/main.tsx','utf8').match(/const empty:State=(.*);/)[1])();
const state={...base,type:'state',appLanguage:'tr',startup:false,page:'broadcast',inputs:Array.from({length:100},(_,i)=>({key:'v'+i,label:'Video '+i})),slots:Array.from({length:100},(_,i)=>({number:i+1,input:'v'+i,inputName:'Video '+i,file:'test.srt',language:'Türkçe',sourceLanguage:'EN',ready:true,translating:false,status:'Hazır',preview:'Test altyazısı'})),profiles:Array.from({length:100},(_,i)=>({name:'Profil '+i,path:'test'+i,active:i===0})),playlist:{...base.playlist,lists:Array.from({length:30},(_,i)=>({id:'p'+i,name:'Liste '+i,count:100,available:true,videoLabels:Array.from({length:100},(_,j)=>'Video '+j)}))}};
state.controlCardOrder=[...state.slots.map(slot=>'slot-'+slot.number),...state.playlist.lists.map(list=>'playlist-'+list.id)];
 const server=createServer((req,res)=>{const file=req.url==='/'?'index.html':req.url.slice(1);if(!['index.html','app.js','app.css','brand-cat.png'].includes(file)){res.writeHead(404).end();return;}res.setHeader('Content-Type',file.endsWith('.js')?'application/javascript':file.endsWith('.css')?'text/css':file.endsWith('.png')?'image/png':'text/html');res.end(readFileSync('dist/'+file));});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const browser=await chromium.launch({headless:true,executablePath:['C:/Program Files/Google/Chrome/Application/chrome.exe','C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(existsSync)});
const errors=[],samples=[],latencies=[];let updates=0;
try{
 const page=await browser.newPage({viewport:{width:1920,height:1080}});page.on('pageerror',e=>errors.push(e.message));
 await page.addInitScript(state=>{let listener;window.__state=state;window.__emit=()=>listener?.({data:structuredClone(window.__state)});window.chrome=window.chrome||{};window.chrome.webview={addEventListener:(_,fn)=>listener=fn,postMessage:m=>{if(m.command==='page')window.__state.page=m.page;queueMicrotask(()=>{window.__emit();listener?.({data:{type:'result',id:m.id,ok:true}});});}};},state);
 await page.goto('http://127.0.0.1:'+server.address().port);await page.locator('.broadcast-scene').first().waitFor();assert.equal(await page.locator('.broadcast-scene').count(),130);
 await page.getByRole('button',{name:/Yeni profil oluştur/}).click();
 await page.getByRole('dialog',{name:'Profil seç'}).waitFor({state:'hidden'});
 const cdp=await page.context().newCDPSession(page);await cdp.send('Performance.enable');
 const start=Date.now();
 for(let cycle=0;cycle<12;cycle++){
  await page.evaluate(cycle=>{window.__state.page=['broadcast','profiles','settings','broadcast'][cycle%4];window.__emit();window.__stress=new Promise(resolve=>{let n=0;const timer=setInterval(()=>{const s=window.__state;s.vmix.connected=n%100<80;s.video={playing:true,positionMs:n*50,durationMs:200000,remainingMs:200000-n*50,source:'Video 1'};s.slots[0].status='ON AIR '+n;window.__emit();if(++n===200){clearInterval(timer);resolve(n);}},50);});},cycle);
  updates+=await page.evaluate(()=>window.__stress);
  const t=Date.now();await page.evaluate(()=>{window.__state.status='Heartbeat';window.__emit();});latencies.push(Date.now()-t);
  await cdp.send('HeapProfiler.collectGarbage');const metrics=(await cdp.send('Performance.getMetrics')).metrics;const heap=metrics.find(m=>m.name==='JSHeapUsedSize').value;const nodes=await page.locator('*').count();samples.push({cycle,heapMiB:Math.round(heap/1048576*100)/100,nodes});console.log(JSON.stringify(samples.at(-1)));
 }
 assert.deepEqual(errors,[]);assert.ok(samples.at(-1).heapMiB-samples[3].heapMiB<30,'Retained heap grew more than 30 MiB');
 const report={durationSeconds:Math.round((Date.now()-start)/1000),updates,slots:100,lists:30,videosPerList:100,errors,samples,maxBridgeLatencyMs:Math.max(...latencies),scope:'Simulated UI state, not live OBS/vMix/OMT transport or a full-length broadcast'};
 writeFileSync('stress-results.json',JSON.stringify(report,null,2));console.log('PASS '+JSON.stringify(report));
}finally{await browser.close();server.close();}
