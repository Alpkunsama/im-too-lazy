import {Select} from './Select';
import React,{useState} from 'react';
export type GlossaryEntry={source:string,target:string,language:string,presetId?:string|null,presetLanguage?:string|null};
const games=[{id:'cs2',name:'Counter-Strike 2',terms:['AWP','AK-47','M4A1-S','M4A4','Desert Eagle','Glock-18','USP-S','C4','Dust II','Mirage','Inferno','Nuke','Ancient','Anubis','clutch','eco round','force buy','entry frag','retake']},{id:'valorant',name:'Valorant',terms:['Spike','Vandal','Phantom','Operator','Sheriff','Judge','Spectre','Odin','Jett','Sova','Sage','Cypher','Omen','Killjoy','Brimstone','Raze','Reyna','clutch','retake']},{id:'lol',name:'League of Legends',terms:['Baron Nashor','Nexus','Summoner’s Rift','Rift Herald','Elder Dragon','Flash','Smite','Teleport','Ignite','Dragon','gank','last hit','teamfight','split push','AD carry','jungle','mid lane']},{id:'dota2',name:'Dota 2',terms:['Roshan','Aegis of the Immortal','Ancient','Radiant','Dire','Black King Bar','Blink Dagger','Divine Rapier','buyback','deny','last hit','courier','Glimmer Cape','Observer Ward','Sentry Ward']},{id:'rl',name:'Rocket League',terms:['Octane','Dominus','Fennec','flip reset','ceiling shot','air dribble','double tap','kickoff','boost','demo','pinch','musty flick','wave dash','aerial','half flip']}];
const extraTerms={"cs2":["Tec-9","Five-SeveN","P250","CZ75-Auto","Dual Berettas","SSG 08","SCAR-20","G3SG1","MP9","MAC-10","MP7","UMP-45","P90","Galil AR","FAMAS","AUG","SG 553","Nova","XM1014","MAG-7","Negev","Vertigo","Overpass","Train","Cache","Cobblestone","flashbang","smoke grenade","Molotov","incendiary grenade","HE grenade","defuse kit","bombsite","post-plant","pistol round","full buy","anti-eco","trade kill","crossfire","spray transfer","wallbang","one tap","peeking","counter-strafe","lurker","in-game leader","AWPer","utility","CT side","T side"],"valorant":["Ghost","Classic","Frenzy","Shorty","Bucky","Stinger","Bulldog","Guardian","Marshal","Ares","Chamber","Breach","Skye","Viper","Astra","KAY/O","Yoru","Neon","Fade","Harbor","Gekko","Deadlock","Iso","Ascent","Bind","Haven","Split","Icebox","Breeze","Fracture","Pearl","Lotus","Sunset","Duelist","Initiator","Controller","Sentinel","ultimate","orb","plant","defuse","post-plant","eco round","save round","entry","utility","lineup","site execute"],"lol":["top lane","bot lane","support","marksman","jungler","Blue Sentinel","Red Brambleback","Gromp","Krugs","Raptors","Wolves","Rift Scuttler","Voidgrubs","Ocean Drake","Mountain Drake","Infernal Drake","Cloud Drake","Hextech Drake","Chemtech Drake","dragon soul","inhibitor","turret","ward","Control Ward","Oracle Lens","recall","farming","CS","wave management","freeze","slow push","fast push","roam","dive","peel","poke","engage","disengage","frontline","backline","power spike","item spike","objective","Pentakill","shutdown","First Blood","ARAM"],"dota2":["Town Portal Scroll","Boots of Travel","Power Treads","Phase Boots","Manta Style","Butterfly","Monkey King Bar","Daedalus","Eye of Skadi","Satanic","Heart of Tarrasque","Linken’s Sphere","Refresher Orb","Aghanim’s Scepter","Aghanim’s Shard","Dust of Appearance","Smoke of Deceit","Gem of True Sight","Bottle","Magic Wand","Hand of Midas","Echo Sabre","Force Staff","Eul’s Scepter","Nullifier","Bounty Rune","Wisdom Rune","Power Rune","Tormentor","Outpost","Lotus Pool","barracks","high ground","low ground","stacking","pulling","safe lane","offlane","mid lane","hard support","soft support","carry","creep equilibrium","backdoor protection","spell immunity","teamfight","gank"],"rl":["Batmobile","Breakout","Merc","Takumi","Plank","hitbox","car control","ball control","ground dribble","bounce dribble","power shot","flick","bump","demolition","shadow defense","rotation","back post","backboard","backboard read","redirect","double jump","fast aerial","air roll","speed flip","diagonal flip","front flip","backflip","dash","recovery","powerslide","boost management","boost steal","small pad","full boost","50/50","challenge","fake challenge","fake kickoff","kickoff strategy","overtime","open net","own goal","infield pass","wall pass","wall shot","ceiling pinch","ground pinch","reset","stall","freestyle","save","clear","assist"]};
for(const game of games)game.terms=[...new Set([...game.terms,...extraTerms[game.id as keyof typeof extraTerms]])];
export default function GameGlossaryPresets({entries,languages,command}:{
  entries:GlossaryEntry[];
  languages:{key:string,label:string}[];
  command:(name:string,args?:Record<string,unknown>)=>Promise<any>;
}){
  const [game,setGame]=useState(''),[language,setLanguage]=useState('Türkçe');
  const [busy,setBusy]=useState(false),[status,setStatus]=useState('');
  const selected=games.find(item=>item.id===game);
  const applied=new Map<string,{id:string,language:string,count:number}>();
  for(const entry of entries){
    if(!entry.presetId||!entry.presetLanguage)continue;
    const key=JSON.stringify([entry.presetId,entry.presetLanguage]);
    const group=applied.get(key);
    if(group)group.count++;
    else applied.set(key,{id:entry.presetId,language:entry.presetLanguage,count:1});
  }

  async function add(){
    if(!selected)return;
    setBusy(true);setStatus('');
    try{
      const result=await command('glossaryAddPreset',{presetId:selected.id,language,terms:selected.terms});
      const count=result.value.added as number;
      setStatus(count?selected.name+' · '+count+' terim eklendi.':'Bu dilde presetin tüm terimleri zaten sözlükte var.');
    }catch(error){setStatus((error as Error).message);}
    finally{setBusy(false);}
  }

  async function remove(preset:{id:string,language:string}){
    setBusy(true);setStatus('');
    try{
      const result=await command('glossaryRemovePreset',{presetId:preset.id,language:preset.language});
      setStatus(result.value.removed+' preset terimi kaldırıldı.');
    }catch(error){setStatus((error as Error).message);}
    finally{setBusy(false);}
  }

  return <section className="card form-card game-glossary-presets">
    <h2>Oyun sözlüğü presetleri</h2>
    <div className="glossary-applied-presets">
      <h3>Eklenen presetler</h3>
      {applied.size===0&&<p>Bu profilde kayıtlı preset yok.</p>}
      {[...applied].map(([key,preset])=>{
        const name=games.find(item=>item.id===preset.id)?.name??preset.id;
        return <div className="glossary-preset-row" key={key}>
          <div><strong>{name}</strong><small>{preset.language} · {preset.count} terim</small></div>
          <button className="control" type="button" disabled={busy} aria-label={name+' · '+preset.language+' presetini kaldır'} onClick={()=>void remove(preset)}>Preseti kaldır</button>
        </div>;
      })}
    </div>
    <div className="button-row">
      <label>Oyun<Select value={game} disabled={busy} onChange={e=>{setGame(e.target.value);setStatus('');}}>
        <option value="">Preset seçin…</option>{games.map(item=><option key={item.id} value={item.id}>{item.name}</option>)}
      </Select></label>
      <label>Hedef dil<Select value={language} disabled={busy} onChange={e=>{setLanguage(e.target.value);setStatus('');}}>
        {languages.map(item=><option key={item.key} value={item.key}>{item.label}</option>)}
      </Select></label>
    </div>
    {selected&&<>
      <h3>{selected.name} · {selected.terms.length} terim</h3>
      <div className="game-term-preview">{selected.terms.map(term=><span key={term}>{term}</span>)}</div>
      <button className="control primary" type="button" disabled={busy} onClick={()=>void add()}>Terimleri sözlüğe ekle</button>
    </>}
    <p role="status">{status}</p>
  </section>;
}
