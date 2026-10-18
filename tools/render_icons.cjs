// SVG -> exact-size transparent PNG. Usage: node tools/render_icons.cjs
// Install sharp in your Node environment, or set NODE_PATH to the bundled modules.
const fs = require('node:fs/promises');
const path = require('node:path');
const sharp = require('sharp');
const root = path.resolve(__dirname, '..');
const source = path.join(root, 'assets/icons');
const target = path.join(root, 'src/BridGHe.Grasshopper/Resources/Icons');
const esc = s => s.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"','&quot;');
async function render() {
  await fs.mkdir(target, {recursive:true});
  const manifest = JSON.parse(await fs.readFile(path.join(source,'manifest.json'),'utf8'));
  const counts = `${manifest.icons.filter(i=>!i.key.endsWith('Param')).length} componenti + ${manifest.icons.filter(i=>i.key.endsWith('Param')).length} parametri`;
  const all = [...manifest.icons.map(i => i.key),'BridGHe','BridGHeChecks'];
  for (const name of all) {
    await sharp(path.join(source,name+'.svg'), {density:384}).resize(24,24).png().toFile(path.join(target,name+'.png'));
  }
  for (const name of ['BridGHe','BridGHeChecks']) {
    await sharp(path.join(source,name+'.svg'), {density:384}).resize(16,16).png().toFile(path.join(target,name+'Category.png'));
  }
  await sharp(path.join(source,'BridGHe.svg'),{density:1536}).resize(512,512).png().toFile(path.join(source,'BridGHe-512.png'));
  // SVG catalogue embeds original vectors, avoiding external font/image dependencies for each icon.
  const families = [...new Set(manifest.icons.map(i=>i.family))];
  const familyNames = {model:'MODELLAZIONE',material:'MATERIALI',section:'SEZIONI',load:'CARICHI',moving:'CARICHI MOBILI',solver:'STRAUS',check:'VERIFICHE · BRIDGE_CHECKS',result:'RISULTATI',tool:'STRUMENTI'};
  const rows=[];let y=140;
  for(const family of families) {
    const icons=manifest.icons.filter(i=>i.family===family);
    rows.push(`<text x="36" y="${y}" fill="#203b50" font-size="15" font-weight="600">${familyNames[family]}</text>`);
    y+=18;
    for(let n=0;n<icons.length;n++) {
      const icon=icons[n], x=36+(n%7)*172, row=y+Math.floor(n/7)*84;
      const body=(await fs.readFile(path.join(source,icon.key+'.svg'),'utf8')).replace(/<svg[^>]*>|<\/svg>/g,'');
      rows.push(`<g transform="translate(${x} ${row}) scale(1.5)">${body}</g>`);
      // One real 24 px rendering on a dark tile alongside the enlarged vector.
      rows.push(`<rect x="${x+48}" y="${row}" width="36" height="36" rx="5" fill="#303742"/><g transform="translate(${x+54} ${row+6})">${body}</g>`);
      const words=icon.name.split(' ');let line='';let lines=[];
      for(const word of words){ if((line+' '+word).length>24){lines.push(line);line=word;}else line+=(line?' ':'')+word; }
      if(line)lines.push(line);
      lines.slice(0,2).forEach((line,i)=>rows.push(`<text x="${x}" y="${row+53+i*13}" fill="#40566b" font-size="10.5">${esc(line)}</text>`));
    }
    y+=Math.ceil(icons.length/7)*84+28;
  }
  const logo=(await fs.readFile(path.join(source,'BridGHe.svg'),'utf8')).replace(/<svg[^>]*>|<\/svg>/g,'');
  const sheet=`<svg xmlns="http://www.w3.org/2000/svg" width="1264" height="${y+10}" viewBox="0 0 1264 ${y+10}" font-family="Segoe UI,Arial,sans-serif"><rect width="1264" height="${y+10}" fill="#f7f9fb"/><g transform="translate(36 30) scale(2.8)">${logo}</g><text x="124" y="63" fill="#17364b" font-size="34" font-weight="600">BridGHe</text><text x="126" y="88" fill="#536977" font-size="14">Sistema di icone · ${counts} · SVG / PNG 24 px</text>${rows.join('')}</svg>`;
  await fs.writeFile(path.join(source,'catalogue.svg'),sheet);
  await sharp(Buffer.from(sheet)).png().toFile(path.join(source,'catalogue.png'));
  const selections=[['BeamComponent','Beam'],['PlateComponent','Plate'],['MaterialSteelComponent','Materiali'],['StrausISectionComponent','Sezioni Straus'],['MovingVehicleComponent','Veicoli'],['MovingPathComponent','Percorsi mobili'],
    ['StrausR3ExportComponent','Export R3'],['StrausR3AnalysisComponent','Analisi R3'],['SuperElementCheckComponent','BridgeChecks'],['CreepAndShrinkageConcreteComponent','Viscosità e ritiro'],['BeamResultsComponent','Risultati'],['BridgeResultPlotComponent','Report']];
  const tiles=[];
  for(let i=0;i<selections.length;i++){
    const [key,label]=selections[i],x=42+(i%6)*170,y=205+Math.floor(i/6)*125;
    const body=(await fs.readFile(path.join(source,key+'.svg'),'utf8')).replace(/<svg[^>]*>|<\/svg>/g,'');
    tiles.push(`<rect x="${x}" y="${y}" width="156" height="112" rx="12" fill="white"/><g transform="translate(${x+24} ${y+16}) scale(2)">${body}</g><rect x="${x+87}" y="${y+22}" width="36" height="36" rx="6" fill="#303742"/><g transform="translate(${x+93} ${y+28})">${body}</g><text x="${x+78}" y="${y+90}" text-anchor="middle" fill="#203b50" font-size="13">${label}</text>`);
  }
  const preview=`<svg xmlns="http://www.w3.org/2000/svg" width="1090" height="493" viewBox="0 0 1090 493" font-family="Segoe UI,Arial,sans-serif"><rect width="1090" height="493" rx="16" fill="#f0f5f7"/><g transform="translate(42 36) scale(5)">${logo}</g><text x="191" y="92" fill="#17364b" font-size="46" font-weight="600">BridGHe</text><text x="194" y="123" fill="#526778" font-size="16">Modellazione · Straus · Verifiche</text><text x="194" y="150" fill="#526778" font-size="13">${counts} · sorgenti SVG · PNG nativi 24 px</text>${tiles.join('')}<text x="44" y="476" fill="#627889" font-size="11">A sinistra: dettaglio ingrandito. A destra: dimensione reale su fondo scuro.</text></svg>`;
  await fs.writeFile(path.join(source,'preview.svg'),preview);
  await sharp(Buffer.from(preview)).png().toFile(path.join(source,'preview.png'));
  console.log(`Rendered ${all.length} 24 px icons, 2 category icons at 16 px, catalogue and preview.`);
}
render().catch(e=>{console.error(e);process.exitCode=1;});
