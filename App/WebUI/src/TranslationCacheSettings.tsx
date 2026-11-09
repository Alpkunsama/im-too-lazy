import React,{useState,useEffect} from 'react';
export default function TranslationCacheSettings({command}:{command:(name:string,args?:Record<string,unknown>)=>Promise<any>}){
 const [stats,setStats]=useState({count:0,bytes:0}),[busy,setBusy]=useState(false),[message,setMessage]=useState('');
 useEffect(()=>{let mounted=true;command('cacheStats').then(r=>{if(mounted)setStats(r.value);}).catch(e=>{if(mounted)setMessage(e.message);});return()=>{mounted=false;};},[]);
 return <section className="translation-presets"><h3>Çeviri önbelleği</h3><p>{stats.count} kayıt · {(stats.bytes/1024/1024).toFixed(1)} MB · En fazla yaklaşık 50 MB / 10.000 kayıt</p><button type="button" className="control" disabled={busy} onClick={async()=>{setBusy(true);setMessage('');try{const r=await command('cacheClear');setStats(r.value);setMessage('Önbellek temizlendi. Profildeki SRT ve hazır çeviriler korundu.');}catch(e){setMessage((e as Error).message);}finally{setBusy(false);}}}>{busy?'Temizleniyor…':'Önbelleği temizle'}</button><p role="status">{message}</p></section>;
}
