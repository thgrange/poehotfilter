const $ = id => document.getElementById(id);

// ---- Photino bridge ----
function send(type, payload){ window.external.sendMessage(JSON.stringify({type, payload})); }
window.external.receiveMessage(handleMessage);

// Photino/WebView2 (Windows) hands us C#->JS messages with UTF-8 bytes packed two-per-UTF-16-unit
// (low byte, then high byte), plus trailing buffer garbage. C# frames each message as
// "<utf8ByteLength>:<json>"; we unpack the raw bytes, read the length prefix, then UTF-8-decode
// exactly that many bytes of JSON. Robust to the trailing garbage.
function decodeFramed(raw){
  const s = String(raw);
  const bytes = new Uint8Array(s.length * 2);
  let n = 0;
  for(let i=0;i<s.length;i++){ const c=s.charCodeAt(i); bytes[n++]=c & 0xFF; bytes[n++]=(c>>8)&0xFF; }
  // read ASCII length prefix up to ':'
  let i=0, lenStr='';
  while(i<n && bytes[i]!==0x3A){ lenStr += String.fromCharCode(bytes[i]); i++; }
  i++; // skip ':'
  const len = parseInt(lenStr,10);
  if(!Number.isFinite(len)) throw new Error('bad frame');
  return new TextDecoder('utf-8').decode(bytes.subarray(i, i+len));
}

function handleMessage(raw){
  let msg;
  try { msg = JSON.parse(decodeFramed(raw)); } catch(e){ return; }
  switch(msg.type){
    case 'itemCaptured': openPopup(msg.payload); showView('capture'); break;
    case 'manualPick': openManualPopup(); showView('capture'); break;
    case 'itemStyle': applyCurrentStyle(msg.payload); break;
    case 'noActiveFilter': showNoFilter(msg.payload); break;
    case 'presets': renderPresets(msg.payload); break;
    case 'needInjection': askInjection(msg.payload.filter); break;
    case 'ruleAdded': closePopup(); break;
    case 'showRules': editingId=null; renderRules(msg.payload); showView('rules'); break;
    case 'rules': renderRules(msg.payload); break; // refresh in place after a delete
    case 'noItem': /* could flash a toast */ break;
  }
}

// ---- which panel is visible: 'capture' (add a rule) or 'rules' (manage list) ----
function showView(which){
  $('capturePanel').style.display  = which==='capture'  ? 'flex' : 'none';
  $('rulesPanel').style.display    = which==='rules'    ? 'flex' : 'none';
  $('noFilterPanel').style.display = which==='noFilter' ? 'flex' : 'none';
}
function rulesVisible(){ return $('rulesPanel').style.display !== 'none'; }
function noFilterVisible(){ return $('noFilterPanel').style.display !== 'none'; }

// No loot filter is active in PoE: either guide the player to select the one we created,
// or (if the PoE folder couldn't be found) show a plain error.
function showNoFilter(p){
  showView('noFilter');
  if(p && p.poeFolderFound){
    $('nfSub').textContent = 'PoE has no loot filter loaded';
    $('nfBody').innerHTML =
      `There's no item filter active in Path of Exile right now, so there's nothing to highlight into.<br><br>`+
      `I created <b>${p.filterName}</b> in your filter folder. In game:<br>`+
      `<b>Esc → Options → Game → Item Filter</b> — enable it and select <b>${p.filterName}</b> `+
      `(or any of your own filters), then press the hotkey again.<br><br>`+
      `<span style="color:var(--muted);font-size:11px;word-break:break-all;">${p.folder}</span>`;
  } else {
    $('nfSub').textContent = 'PoE folder not found';
    $('nfBody').innerHTML =
      `Couldn't find your Path of Exile folder (<i>Documents\\My Games\\Path of Exile</i>).<br><br>`+
      `Make sure Path of Exile has been launched at least once so the folder exists, then try again.`;
  }
}

function renderRules(list){
  const wrap=$('rulesList'); wrap.innerHTML='';
  const n=(list||[]).length;
  $('rulesCount').textContent = n ? `${n} filter${n>1?'s':''}` : '';
  $('rulesEmpty').style.display = n ? 'none' : 'block';
  (list||[]).forEach(r=>{
    const row=document.createElement('div'); row.className='ruleItem';

    // mini in-game-style label (same colours/icon the rule emits)
    let iconHtml='';
    if(r.iconShape && r.iconShape!=='None'){
      const st=iconStyle(r.iconShape, r.iconColor, 2, 14);
      iconHtml=`<span style="display:inline-block;vertical-align:middle;margin-right:5px;${st}"></span>`;
    }
    const bgA=(r.bg[3]??255)<40 ? 200 : (r.bg[3]??255);
    const mini=document.createElement('div'); mini.className='ruleMini';
    mini.style.color=`rgba(${r.text[0]},${r.text[1]},${r.text[2]},${(r.text[3]??255)/255})`;
    mini.style.background=`rgba(${r.bg[0]},${r.bg[1]},${r.bg[2]},${bgA/255})`;
    mini.style.border=`1px solid rgba(${r.border[0]},${r.border[1]},${r.border[2]},${(r.border[3]??255)/255})`;
    mini.innerHTML=`${iconHtml}<span style="font-family:'FontinSC','Fontin',Georgia,serif;">${r.label}</span>`;

    const parts=[];
    if(r.action==='Hide') parts.push('HIDDEN');
    if(r.rarity==='AnyNonUnique') parts.push('non-unique');
    else if(r.rarity!=='Any') parts.push(r.rarity);
    if(r.corrupted==='Yes') parts.push('corrupted');
    else if(r.corrupted==='No') parts.push('not corrupted');
    if(r.stackMin && r.stackMax) parts.push(`stack ${r.stackMin}–${r.stackMax}`);
    else if(r.stackMin) parts.push(`stack ≥ ${r.stackMin}`);
    else if(r.stackMax) parts.push(`stack ≤ ${r.stackMax}`);
    if(r.ilvlMode==='GreaterOrEqual') parts.push(`ilvl ≥ ${r.ilvlValue}`);
    else if(r.ilvlMode==='Exact')     parts.push(`ilvl = ${r.ilvlValue}`);
    if(r.gemLevelMode==='GreaterOrEqual') parts.push(`gem lvl ≥ ${r.gemLevelValue}`);
    else if(r.gemLevelMode==='Exact')     parts.push(`gem lvl = ${r.gemLevelValue}`);
    if(r.qualityMode==='GreaterOrEqual') parts.push(`Q ≥ ${r.qualityValue}%`);
    else if(r.qualityMode==='Exact')     parts.push(`Q = ${r.qualityValue}%`);
    if(r.enchantNode) parts.push(r.enchantNode);
    if(r.passiveNumMode==='GreaterOrEqual') parts.push(`≥ ${r.passiveNumValue} passives`);
    else if(r.passiveNumMode==='Exact')     parts.push(`${r.passiveNumValue} passives`);
    const meta=document.createElement('div'); meta.className='ruleMeta';
    meta.innerHTML=`<div>${r.baseType}</div><div class="sub">${parts.join(' · ')||'any ilvl'}</div>`;

    const del=document.createElement('button'); del.className='ruleDel'; del.textContent='×';
    del.title='Remove';
    del.onclick=(e)=>{ e.stopPropagation(); send('deleteRule',{id:r.id}); };

    // Click the row (anywhere but ×) to edit this filter.
    row.style.cursor='pointer';
    row.onclick=()=>openEdit(r);

    row.appendChild(mini); row.appendChild(meta); row.appendChild(del);
    wrap.appendChild(row);
  });
}

// ---- icon atlas (real game sprites, single atlas, 44px cells) ----
const ATLAS_SHAPES=['Circle','Diamond','Hexagon','Square','Star','Triangle','Cross','Moon','Raindrop','Kite','Pentagon','UpsideDownHouse'];
const ATLAS_COLORS=['Blue','Green','Brown','Red','White','Yellow','Cyan','Grey','Orange','Pink','Purple'];
const CELL=44;
// Filter keyword list mirrors the atlas rows 1:1 here.
const SHAPE_FILTER=ATLAS_SHAPES.slice();
function iconStyle(shape,color,sizeIdx,pxOverride){
  const row=ATLAS_SHAPES.indexOf(shape), col=ATLAS_COLORS.indexOf(color);
  if(row<0||col<0) return '';
  // Game sizes: 0 large, 1 medium, 2 small -> scale the same sprite.
  const scale = sizeIdx===0?1.0:sizeIdx===1?0.78:0.58;
  const px = pxOverride || Math.round(CELL*scale);
  const aw=ATLAS_COLORS.length*px, ah=ATLAS_SHAPES.length*px;
  return `width:${px}px;height:${px}px;background-image:url('icons/atlas.png');background-repeat:no-repeat;`+
         `background-size:${aw}px ${ah}px;background-position:-${col*px}px -${row*px}px;`;
}
const ICON_COLORS=ATLAS_COLORS.slice();
// Indicative labels (no official naming exists; only #6 is widely known = the Exalt/Mirror screech).
// These describe the synthesized fallback; the real game audio plays instead if you drop the files in.
const SOUND_NAMES=['Soft blip','Low chime','Bell','Double bell','Bright ping','Exalt alarm','Deep gong',
  'Glass tap','Coin','Crystal','Harp','Marimba','Bright bell','High ping','Sparkle','High chime'];
function fillSelects(){
  $('iconShape').innerHTML='<option value="None">None</option>'+SHAPE_FILTER.map(s=>`<option>${s}</option>`).join('');
  $('iconColor').innerHTML=ICON_COLORS.map(c=>`<option>${c}</option>`).join('');
  $('iconColor').value='White';
  $('alertSound').innerHTML='<option value="0">None</option>'+
    SOUND_NAMES.map((n,i)=>`<option value="${i+1}">${i+1} — ${n}</option>`).join('');
}

// ---- state + helpers ----
let current = { baseType:'', itemClass:'', name:'', rarity:null, ilvl:null, stackable:false, isGem:false };
let ilvlMode = 'GreaterOrEqual';
let qualityMode = 'Any';
let gemLevelMode = 'Any';
let action = 'Show';
let editingId = null; // null = adding from a captured item; a guid = editing an existing rule

function hexToRgb(h){h=h.replace('#','');return [parseInt(h.slice(0,2),16),parseInt(h.slice(2,4),16),parseInt(h.slice(4,6),16)];}
function toHex(c){return '#'+c.slice(0,3).map(x=>x.toString(16).padStart(2,'0')).join('');}
function setIlvlMode(m){ ilvlMode=m; [...$('ilvlSeg').children].forEach(b=>b.classList.toggle('active', b.dataset.mode===m)); }
function setQualityMode(m){ qualityMode=m; [...$('qualitySeg').children].forEach(b=>b.classList.toggle('active', b.dataset.mode===m)); }
function setGemLevelMode(m){ gemLevelMode=m; [...$('gemLevelSeg').children].forEach(b=>b.classList.toggle('active', b.dataset.mode===m)); }

// Show/hide the conditions that make sense for the item type:
//  - currency: no rarity/corrupted/ilvl/quality, only stack size
//  - gem: no rarity (rarity "Gem"), no ilvl; gem level + quality apply, corrupted stays
//  - everything else: rarity/corrupted/ilvl + quality
function applyTypeVisibility(isCur, isGem){
  $('rarityRow').style.display    = (isCur || isGem) ? 'none' : '';
  $('corruptedRow').style.display = isCur ? 'none' : '';
  $('targetSec').style.display    = isCur ? 'none' : '';   // both rows gone for currency
  $('ilvlSec').style.display      = (isCur || isGem) ? 'none' : '';
  $('gemLevelSec').style.display  = isGem ? '' : 'none';
  $('qualitySec').style.display   = isCur ? 'none' : '';
  $('stackSec').style.display     = isCur ? '' : 'none';
}
function setAction(a){
  action=a;
  [...$('actionSeg').children].forEach(b=>b.classList.toggle('active', b.dataset.action===a));
  $('capturePanel').classList.toggle('hide-mode', a==='Hide');
}
// Fallback stackability check from the item class (for rules saved before the flag existed).
function isStackableClass(cls){
  if(!cls) return false;
  cls = cls.toLowerCase();
  if(cls.includes('currency')) return true;
  return ['fragment','divination card','incubator','delirium orb','vial','splinter','catalyst','oil','essence','fossil','resonator','scarab','tattoo','omen'].some(k=>cls.includes(k));
}

// ---- cluster jewels: enchant (small passive node name) + number of passives ----
// Node names from RePoE (game data); these are the only values EnchantmentPassiveNode accepts.
const CLUSTER_NODES = {
  Large: ['Attack Damage','Attack Damage while Dual Wielding','Attack Damage while holding a Shield',
    'Axe and Sword Damage','Bow Damage','Chaos Damage','Cold Damage','Dagger and Claw Damage',
    'Damage with Two Handed Weapons','Elemental Damage','Fire Damage','Lightning Damage',
    'Mace and Staff Damage','Minion Damage','Physical Damage','Spell Damage','Wand Damage'],
  Medium: ['Area Damage','Aura Effect','Brand Damage','Channelling Skill Damage','Chaos Damage over Time',
    'Cold Damage over Time','Critical Chance','Curse Effect','Damage over Time',
    'Damage while you have a Herald','Effect of Non-Damaging Ailments','Exerted Attack Damage',
    'Fire Damage over Time','Flask Duration','Life and Mana recovery from Flasks',
    'Minion Damage while you have a Herald','Minion Life','Physical Damage over Time',
    'Projectile Damage','Totem Damage','Trap and Mine Damage'],
  Small: ['Armour','Chance to Block Attack Damage','Chance to Block Spell Damage',
    'Chance to Suppress Spell Damage','Chaos Resistance','Cold Resistance','Curse Effect',
    'Dexterity','Energy Shield','Evasion','Fire Resistance','Intelligence','Life',
    'Lightning Resistance','Mana','Reservation Efficiency','Strength']
};
const CLUSTER_DEFAULT_PASSIVES = { Small: 2, Medium: 4, Large: 8 };
let passiveNumMode = 'Any';
function setPassiveNumMode(m){ passiveNumMode=m; [...$('passiveSeg').children].forEach(b=>b.classList.toggle('active', b.dataset.mode===m)); }
function clusterSizeOf(baseType){
  const m=/(Small|Medium|Large)\s+Cluster\s+Jewel/i.exec(baseType||'');
  return m ? m[1][0].toUpperCase()+m[1].slice(1).toLowerCase() : null;
}
// Shows/hides the CLUSTER JEWEL section and fills the enchant list for the jewel's size.
function setClusterSection(baseType){
  const size=clusterSizeOf(baseType);
  $('clusterSec').style.display = size ? '' : 'none';
  if(!size) return;
  enchantOptions = CLUSTER_NODES[size];
  enchantValue = '';
  $('enchantSearch').value = 'Any';
  setPassiveNumMode('Any');
  $('passiveVal').value = CLUSTER_DEFAULT_PASSIVES[size];
}

// ---- enchant combobox (same pattern as the item picker, but plain strings) ----
let enchantOptions = [];   // node names for the current cluster size
let enchantValue = '';     // selected node name; '' = Any
let enchList=[], enchIdx=-1;

function buildEnchantCombo(){
  const inp=$('enchantSearch'), dd=$('enchantDrop');
  inp.addEventListener('input', ()=>openEnchDrop(inp.value));
  inp.addEventListener('focus', ()=>{ inp.select(); openEnchDrop(''); });
  inp.addEventListener('blur', ()=>{ closeEnchDrop(); inp.value = enchantValue || 'Any'; });
  inp.addEventListener('keydown', e=>{
    const open = dd.style.display!=='none';
    if(e.key==='ArrowDown' || e.key==='ArrowUp'){
      e.preventDefault();
      if(!open){ openEnchDrop(inp.value); return; }
      if(!enchList.length) return;
      enchIdx=(enchIdx+(e.key==='ArrowDown'?1:-1)+enchList.length)%enchList.length;
      renderEnchDrop();
    } else if(e.key==='Enter'){
      if(open && enchIdx>=0){ e.preventDefault(); pickEnch(enchIdx); }
    } else if(e.key==='Escape' && open){
      e.stopPropagation();   // Esc closes the dropdown, not the whole popup
      closeEnchDrop();
      inp.value = enchantValue || 'Any';
    }
  });
  dd.addEventListener('mousedown', e=>{
    const opt=e.target.closest('.combo-opt'); if(!opt) return;
    e.preventDefault();
    pickEnch(+opt.dataset.i);
  });
}
function openEnchDrop(q){
  const toks=(q||'').trim().toLowerCase().split(/\s+/).filter(Boolean);
  const all=['Any', ...enchantOptions];
  enchList = toks.length ? all.filter(s=>{ const h=s.toLowerCase(); return toks.every(t=>h.includes(t)); }) : all;
  enchIdx = enchList.length ? 0 : -1;
  renderEnchDrop();
}
function renderEnchDrop(){
  const dd=$('enchantDrop');
  dd.innerHTML = enchList.length
    ? enchList.map((s,i)=>`<div class="combo-opt${i===enchIdx?' sel':''}" data-i="${i}"><span>${escHtml(s)}</span></div>`).join('')
    : '<div class="combo-empty">No match.</div>';
  dd.style.display='block';
  const sel=dd.querySelector('.sel'); if(sel) sel.scrollIntoView({block:'nearest'});
}
function closeEnchDrop(){ $('enchantDrop').style.display='none'; enchList=[]; enchIdx=-1; }
function pickEnch(i){
  const s=enchList[i]; if(s==null) return;
  enchantValue = s==='Any' ? '' : s;
  $('enchantSearch').value = s;
  closeEnchDrop();
}

// ---- manual item picker (hotkey pressed on empty ground) ----
let manualMode = false;
function isGemClass(cls){ return /skill gem/i.test(cls||''); }

const ALL_ITEMS = [];        // flat {n, c, cat} index for the global search
let pickedItem = null;       // currently selected {n, c, cat}
let dropList = [];           // entries currently shown in the dropdown
let dropIdx = -1;            // keyboard-highlighted entry

const catLabel = c => c.replace('Armour — ','').replace('Weapons — ','');
const escHtml = s => s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');

function buildItemSelector(){
  const data = window.ITEM_DATA || {};
  const inp=$('itemSearch'), dd=$('itemDrop');
  Object.keys(data).forEach(cat=>data[cat].forEach(it=>ALL_ITEMS.push({n:it.n, c:it.c, cat})));

  inp.addEventListener('input', ()=>openDrop(inp.value));
  // focusing opens the full list; the text is pre-selected so typing replaces it
  inp.addEventListener('focus', ()=>{ inp.select(); openDrop(''); });
  inp.addEventListener('blur', ()=>{ closeDrop(); if(pickedItem) inp.value=pickedItem.n; });
  inp.addEventListener('keydown', e=>{
    const open = dd.style.display!=='none';
    if(e.key==='ArrowDown' || e.key==='ArrowUp'){
      e.preventDefault();
      if(!open){ openDrop(inp.value); return; }
      if(!dropList.length) return;
      dropIdx=(dropIdx+(e.key==='ArrowDown'?1:-1)+dropList.length)%dropList.length;
      renderDrop();
    } else if(e.key==='Enter'){
      if(open && dropIdx>=0){ e.preventDefault(); pickFromDrop(dropIdx); }
    } else if(e.key==='Escape' && open){
      e.stopPropagation();   // Esc closes the dropdown, not the whole popup
      closeDrop();
      inp.value = pickedItem ? pickedItem.n : '';
    }
  });
  // mousedown (not click) + preventDefault: select without blurring the input first
  dd.addEventListener('mousedown', e=>{
    const opt=e.target.closest('.combo-opt'); if(!opt) return;
    e.preventDefault();
    pickFromDrop(+opt.dataset.i);
  });
}

function openDrop(q){
  // every typed word must appear in the name or the category; empty query = full list
  const toks=(q||'').trim().toLowerCase().split(/\s+/).filter(Boolean);
  dropList = toks.length
    ? ALL_ITEMS.filter(it=>{
        const hay=(it.n+' '+catLabel(it.cat)).toLowerCase();
        return toks.every(t=>hay.includes(t));
      })
    : ALL_ITEMS;
  dropIdx = dropList.length ? 0 : -1;
  renderDrop();
}
function renderDrop(){
  const dd=$('itemDrop');
  dd.innerHTML = dropList.length
    ? dropList.map((it,i)=>`<div class="combo-opt${i===dropIdx?' sel':''}" data-i="${i}">`+
        `<span>${escHtml(it.n)}</span><span class="cat">${escHtml(catLabel(it.cat))}</span></div>`).join('')
    : '<div class="combo-empty">No match.</div>';
  dd.style.display='block';
  const sel=dd.querySelector('.sel'); if(sel) sel.scrollIntoView({block:'nearest'});
}
function closeDrop(){ $('itemDrop').style.display='none'; dropList=[]; dropIdx=-1; }
function pickFromDrop(i){
  const it=dropList[i]; if(!it) return;
  applyPickedItem(it);
  closeDrop();
}

function applyPickedItem(it){
  pickedItem=it;
  $('itemSearch').value=it.n;
  const isGem=isGemClass(it.c), isCur=!isGem && isStackableClass(it.c);
  current={ baseType:it.n, itemClass:it.c, name:it.n, rarity:null, ilvl:null, stackable:isCur, isGem };
  $('hTitle').textContent=it.n;
  $('hSub').textContent=`${it.n}  ·  ${it.c}`;
  applyTypeVisibility(isCur, isGem);
  if(isCur || isGem) setIlvlMode('Any');
  setClusterSection(it.n);
  // ask C# how this base currently looks under the active filter (prefills appearance)
  send('queryStyle', { baseType:it.n, itemClass:it.c, stackable:isCur, isGem });
  render();
}
function openManualPopup(){
  editingId = null;
  manualMode = true;
  $('pickSec').style.display = '';
  $('hDetText').textContent = 'MANUAL SELECTION';
  $('hHidden').style.display = 'none';
  $('rarity').value='Any'; $('corrupted').value='Any';
  setAction('Show');
  $('stackMin').value=''; $('stackMax').value='';
  setIlvlMode('Any'); $('ilvlVal').value=84;
  setQualityMode('Any'); $('qualityVal').value=20;
  setGemLevelMode('Any'); $('gemLevelVal').value=20;
  $('alertSound').value='0'; $('alertVolume').value=100;
  $('btnAdd').textContent='Add & reload';
  $('btnCancel').textContent='Cancel';
  if(!pickedItem){
    // first open: default to Divine Orb
    const div=ALL_ITEMS.find(it=>/^Divine Orb$/i.test(it.n))||ALL_ITEMS[0];
    if(div) applyPickedItem(div);
  } else {
    applyPickedItem(pickedItem); // re-apply visibility + refresh current style
  }
}
// Prefill the appearance controls with the item's current look (reply to queryStyle).
function applyCurrentStyle(c){
  if(!c || !manualMode) return;
  $('textColor').value=toHex(c.text);     $('textA').value=c.text[3]??255;
  $('borderColor').value=toHex(c.border); $('borderA').value=c.border[3]??255;
  const bg=c.bg ?? c.background;
  $('bgColor').value=toHex(bg);           $('bgA').value=bg[3]??255;
  $('fontSize').value=c.fontSize;
  $('iconShape').value=c.iconShape||'None';
  $('iconColor').value=c.iconColor||'White';
  $('iconSize').value=c.iconSize??1;
  $('hHidden').style.display = c.hidden ? '' : 'none';
  render();
}

function openPopup(item){
  editingId = null;
  manualMode = false;
  $('pickSec').style.display = 'none';
  current = item;
  $('hTitle').textContent = item.name || item.baseType;
  $('hSub').textContent = `${item.baseType}  ·  ${item.itemClass}`;
  $('hDetText').textContent = item.itemLevel!=null ? `DETECTED ILVL ${item.itemLevel}` : 'ilvl not detected';
  $('rarity').value = (['Normal','Magic','Rare','Unique'].includes(item.rarity)) ? item.rarity : 'Any';
  $('corrupted').value = 'Any';
  setAction('Show');
  $('stackMin').value = ''; $('stackMax').value = '';
  setIlvlMode('GreaterOrEqual');
  $('ilvlVal').value = item.itemLevel || 1;
  setQualityMode('Any');  $('qualityVal').value  = item.quality  || 20;
  setGemLevelMode('Any'); $('gemLevelVal').value = item.gemLevel || 20;
  $('alertSound').value = '0';
  $('alertVolume').value = 100;
  // Item type drives which conditions are offered (see applyTypeVisibility).
  const isCur = item.stackable || isStackableClass(item.itemClass);
  const isGem = !!item.isGem;
  applyTypeVisibility(isCur, isGem);
  if(isCur || isGem) setIlvlMode('Any');
  setClusterSection(item.baseType);
  $('btnAdd').textContent = 'Add & reload';
  $('btnCancel').textContent = 'Cancel';
  // Pre-fill the editor with the item's current look under the active filter (if provided).
  if(item.current){
    const c=item.current;
    $('textColor').value=toHex(c.text);   $('textA').value=c.text[3]??255;
    $('borderColor').value=toHex(c.border); $('borderA').value=c.border[3]??255;
    $('bgColor').value=toHex(c.bg ?? c.background); $('bgA').value=(c.bg ?? c.background)[3]??255;
    $('fontSize').value=c.fontSize;
    $('iconShape').value=c.iconShape||'None';
    $('iconColor').value=c.iconColor||'White';
    $('iconSize').value=c.iconSize??1;
  }
  $('hHidden').style.display = (item.current && item.current.hidden) ? '' : 'none';
  render();
}

// Open the edit form pre-filled with an existing rule (clicked from the Custom Filters list).
function openEdit(r){
  editingId = r.id;
  manualMode = false;
  $('pickSec').style.display = 'none';
  current = { baseType:r.baseType, itemClass:r.itemClass, name:r.label, stackable:r.stackable, isGem:r.isGem };
  $('hTitle').textContent = r.label || r.baseType;
  $('hSub').textContent = `${r.baseType}  ·  ${r.itemClass}`;
  $('hDetText').textContent = 'EDITING CUSTOM FILTER';
  $('hHidden').style.display = 'none';
  $('rarity').value = r.rarity;
  $('corrupted').value = r.corrupted;
  setAction(r.action || 'Show');
  $('stackMin').value = r.stackMin ? r.stackMin : '';
  $('stackMax').value = r.stackMax ? r.stackMax : '';
  setIlvlMode(r.ilvlMode);
  $('ilvlVal').value = r.ilvlValue || 1;
  setQualityMode(r.qualityMode || 'Any');   $('qualityVal').value  = r.qualityValue  || 20;
  setGemLevelMode(r.gemLevelMode || 'Any'); $('gemLevelVal').value = r.gemLevelValue || 20;
  const isCur = r.stackable || r.stackMin || r.stackMax || isStackableClass(r.itemClass);
  const isGem = !!r.isGem;
  applyTypeVisibility(isCur, isGem);
  if(isCur || isGem) setIlvlMode('Any');
  setClusterSection(r.baseType);
  if(clusterSizeOf(r.baseType)){
    enchantValue = r.enchantNode || '';
    $('enchantSearch').value = r.enchantNode || 'Any';
    setPassiveNumMode(r.passiveNumMode || 'Any');
    if(r.passiveNumValue) $('passiveVal').value = r.passiveNumValue;
  }
  $('textColor').value=toHex(r.text); $('borderColor').value=toHex(r.border); $('bgColor').value=toHex(r.bg);
  $('textA').value=r.text[3]??255; $('borderA').value=r.border[3]??255; $('bgA').value=r.bg[3]??255;
  $('fontSize').value=r.fontSize;
  $('iconShape').value=r.iconShape||'None';
  $('iconColor').value=r.iconColor||'White';
  $('iconSize').value=r.iconSize??1;
  $('alertSound').value=String(r.alertSound??0);
  $('alertVolume').value=r.alertVolume??100;
  $('btnAdd').textContent = 'Save';
  $('btnCancel').textContent = 'Back';
  showView('capture');
  render();
}
function closePopup(){ send('popupClosed', {}); }

function render(){
  const t=hexToRgb($('textColor').value), b=hexToRgb($('borderColor').value), g=hexToRgb($('bgColor').value);
  const tA=+$('textA').value, bA=+$('borderA').value, gA=+$('bgA').value, fs=+$('fontSize').value;
  $('textHex').textContent=$('textColor').value.toUpperCase();
  $('borderHex').textContent=$('borderColor').value.toUpperCase();
  $('bgHex').textContent=$('bgColor').value.toUpperCase();
  $('fsVal').textContent=fs;
  $('avVal').textContent=$('alertVolume').value;
  const lbl=$('poeLabel');
  $('poeText').textContent = current.name || current.baseType;
  // Text scales with SetFontSize; padding is FilterBlade's fixed 0.3125rem.
  const previewFs = Math.round(Math.min(fs,45)/45*22);
  lbl.style.fontSize=previewFs+'px';
  lbl.style.lineHeight=previewFs+'px';
  lbl.style.padding='0.3125rem';
  lbl.style.color=`rgba(${t[0]},${t[1]},${t[2]},${tA/255})`;
  lbl.style.background=`rgba(${g[0]},${g[1]},${g[2]},${(gA<40?200:gA)/255})`;
  lbl.style.border = bA>0 ? `${fs>=36?2:1}px solid rgba(${b[0]},${b[1]},${b[2]},${bA/255})` : 'none';
  // icon scales with the label (constant proportion), independent of the chosen 0/1/2 size.
  const shape=$('iconShape').value, color=$('iconColor').value;
  const holder=$('poeIcon');
  if(shape==='None'){ holder.style.display='none'; }
  else { holder.style.cssText='display:inline-block;flex-shrink:0;'+iconStyle(shape,color,2,Math.round(previewFs*0.95)); }
}

function renderPresets(list){
  const wrap=$('presetChips'); wrap.innerHTML='';
  (list||[]).forEach(p=>{
    const chip=document.createElement('span'); chip.className='preset';
    let iconHtml='';
    if(p.iconShape && p.iconShape!=='None'){
      const st=iconStyle(p.iconShape, p.iconColor, 2, 13);
      iconHtml=`<span style="display:inline-block;vertical-align:middle;margin-right:4px;${st}"></span>`;
    }
    // The chip itself is a mini in-game preview: name styled with the preset's colours.
    const effBgA = (p.bg[3]??255)<40 ? 200 : (p.bg[3]??255);
    chip.style.color=`rgba(${p.text[0]},${p.text[1]},${p.text[2]},${(p.text[3]??255)/255})`;
    chip.style.background=`rgba(${p.bg[0]},${p.bg[1]},${p.bg[2]},${effBgA/255})`;
    chip.style.border=`1px solid rgba(${p.border[0]},${p.border[1]},${p.border[2]},${(p.border[3]??255)/255})`;
    chip.innerHTML=`${iconHtml}<span style="font-family:'FontinSC','Fontin',Georgia,serif;">${p.name}</span>`;
    chip.onclick=()=>{
      $('textColor').value=toHex(p.text); $('borderColor').value=toHex(p.border); $('bgColor').value=toHex(p.bg);
      $('textA').value=p.text[3]??255; $('borderA').value=p.border[3]??255; $('bgA').value=p.bg[3]??255;
      $('fontSize').value=p.fontSize;
      $('iconShape').value=p.iconShape||'None';
      if(p.iconColor) $('iconColor').value=p.iconColor;
      if(p.iconSize!=null) $('iconSize').value=p.iconSize;
      render();
    };
    wrap.appendChild(chip);
  });
}

// ---- drop-sound preview ----
// Plays wwwroot/sounds/<id>.{mp3,wav,ogg} if present (drop the real GGG sounds there),
// otherwise falls back to a distinct synthesized blip so selection still gives feedback.
let _actx = null;
function previewVolume(){ return Math.min(1, (+$('alertVolume').value) / 100); }
function previewSound(id){
  id = +id;
  if(!id) return;
  tryPlayFile([`sounds/${id}.mp3`,`sounds/${id}.wav`,`sounds/${id}.ogg`], 0, id);
}
function tryPlayFile(urls, i, id){
  if(i >= urls.length){ synthBeep(id); return; }
  const a = new Audio(urls[i]);
  a.volume = previewVolume();
  a.play().catch(()=> tryPlayFile(urls, i+1, id));
}
function synthBeep(id){
  try{
    _actx = _actx || new (window.AudioContext||window.webkitAudioContext)();
    const t0=_actx.currentTime;
    const base = 300 * Math.pow(2, ((id-1)%12)/12) * (id>12 ? 2 : 1); // distinct pitch per id
    const master=_actx.createGain();
    master.gain.value = previewVolume()*0.6;
    master.connect(_actx.destination);
    // a bell-ish chime: a few decaying partials instead of a flat beep
    [[1,1.0],[2.01,0.5],[3.0,0.25]].forEach(([mult,amp])=>{
      const o=_actx.createOscillator(), g=_actx.createGain();
      o.type='sine'; o.frequency.value=base*mult;
      g.gain.setValueAtTime(amp, t0);
      g.gain.exponentialRampToValueAtTime(0.0001, t0+0.6);
      o.connect(g); g.connect(master);
      o.start(t0); o.stop(t0+0.62);
    });
  }catch(e){}
}

function askInjection(filter){
  // lightweight inline confirm (kept simple; could be a styled modal)
  const ok = confirm(`Add the Import line to "${filter}"?\n\nA single line is added at the top and a .phf-backup is created first. If a re-export removes it, you'll be asked again.`);
  send('confirmInjection', { approved: ok });
}

// ---- wiring ----
fillSelects();
buildItemSelector();
buildEnchantCombo();
['rarity','corrupted','ilvlVal','textColor','borderColor','bgColor','textA','borderA','bgA','fontSize','iconShape','iconColor','iconSize','alertSound','alertVolume']
  .forEach(id=>$(id).addEventListener('input', render));
$('ilvlSeg').addEventListener('click', e=>{ const b=e.target.closest('button'); if(!b)return; setIlvlMode(b.dataset.mode); });
$('qualitySeg').addEventListener('click', e=>{ const b=e.target.closest('button'); if(!b)return; setQualityMode(b.dataset.mode); });
$('gemLevelSeg').addEventListener('click', e=>{ const b=e.target.closest('button'); if(!b)return; setGemLevelMode(b.dataset.mode); });
$('passiveSeg').addEventListener('click', e=>{ const b=e.target.closest('button'); if(!b)return; setPassiveNumMode(b.dataset.mode); });
$('actionSeg').addEventListener('click', e=>{ const b=e.target.closest('button'); if(!b)return; setAction(b.dataset.action); });
$('alertSound').addEventListener('change', e=>previewSound(e.target.value));
$('btnPlaySound').onclick=()=>previewSound($('alertSound').value);
$('btnCancel').onclick=()=>{
  if(editingId){ editingId=null; showView('rules'); }   // back to the list, no changes
  else { send('cancel',{}); closePopup(); }
};
$('btnCloseRules').onclick=()=>{ send('closeRules',{}); };
$('btnNoFilterOk').onclick=()=>{ closePopup(); };
// (no scrim-click-to-cancel: the window IS the popup; closing happens via buttons/Esc)
document.addEventListener('keydown', e=>{
  if(e.key!=='Escape') return;
  if(editingId){ editingId=null; showView('rules'); }    // edit form: Esc returns to the list
  else if(rulesVisible()) send('closeRules',{});
  else { send('cancel',{}); closePopup(); }
});
$('btnAdd').onclick=()=>{
  const t=hexToRgb($('textColor').value), b=hexToRgb($('borderColor').value), g=hexToRgb($('bgColor').value);
  const payload = {
    baseType: current.baseType, itemClass: current.itemClass,
    stackable: !!current.stackable, isGem: !!current.isGem,
    rarity: $('rarity').value, corrupted: $('corrupted').value, action,
    ilvlMode, ilvlValue: +$('ilvlVal').value,
    qualityMode, qualityValue: +$('qualityVal').value,
    gemLevelMode, gemLevelValue: +$('gemLevelVal').value,
    stackMin: +$('stackMin').value||0, stackMax: +$('stackMax').value||0,
    enchantNode: clusterSizeOf(current.baseType) ? enchantValue : '',
    passiveNumMode: clusterSizeOf(current.baseType) ? passiveNumMode : 'Any',
    passiveNumValue: +$('passiveVal').value||0,
    textColor:[...t,+$('textA').value], borderColor:[...b,+$('borderA').value], backgroundColor:[...g,+$('bgA').value],
    fontSize:+$('fontSize').value,
    iconShape:$('iconShape').value, iconColor:$('iconColor').value, iconSize:+$('iconSize').value,
    alertSound:+$('alertSound').value, alertVolume:+$('alertVolume').value
  };
  if(editingId){ payload.id = editingId; send('updateRule', payload); }
  else send('addRule', payload);
};

send('ready', {});
