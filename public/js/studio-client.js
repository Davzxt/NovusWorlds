import * as THREE from '/vendor/three/build/three.module.js';
import { OrbitControls } from '/vendor/three/examples/jsm/controls/OrbitControls.js';
import { TransformControls } from '/vendor/three/examples/jsm/controls/TransformControls.js';
import { api, toast } from './main.js';

const canvasHost = document.getElementById('viewport');
const scene = new THREE.Scene();
scene.background = new THREE.Color('#87CEEB');
const camera = new THREE.PerspectiveCamera(60, canvasHost.clientWidth / canvasHost.clientHeight, .1, 1000);
camera.position.set(12, 10, 14);
const renderer = new THREE.WebGLRenderer({ antialias:true, preserveDrawingBuffer:true });
renderer.setSize(canvasHost.clientWidth, canvasHost.clientHeight);
canvasHost.appendChild(renderer.domElement);
const orbit = new OrbitControls(camera, renderer.domElement);
orbit.target.set(0, 0, 0);
orbit.update();
const transform = new TransformControls(camera, renderer.domElement);
scene.add(transform);
transform.addEventListener('dragging-changed', e => orbit.enabled = !e.value);
transform.addEventListener('objectChange', () => { syncSelectedData(); renderProps(); });
scene.add(new THREE.GridHelper(120,120), new THREE.HemisphereLight(0xffffff,0x333333,1.8));

const objects = [];
const scripts = [];
const spawnPoints = [];
let selected = null;
let gameId = new URLSearchParams(location.search).get('id');
let savedTitle = 'Novo Mundo';

if (gameId) await loadGame(gameId); else createDefaultWorld();

async function loadGame(id) {
  const { game } = await api('/api/games/' + id);
  savedTitle = game.title;
  document.getElementById('title').value = game.title;
  for (const obj of game.map_data.objects || []) addPart(obj, false);
  for (const sp of game.map_data.spawnPoints || []) addSpawn(sp, false);
  for (const script of game.map_data.scripts || []) addScript(script, false);
  refreshExplorer();
  log('Mapa carregado: ' + game.title);
}

function createDefaultWorld() {
  addPart({id:crypto.randomUUID(),type:'Part',name:'Baseplate',position:{x:0,y:-.5,z:0},rotation:{x:0,y:0,z:0},size:{x:80,y:1,z:80},color:'#6B8E23',material:'Grass',anchored:true,canCollide:true,transparency:0,children:[]});
  addSpawn({ x: 0, y: 3, z: 0 });
  addPart({id:crypto.randomUUID(),type:'Part',name:'Brick',position:{x:6,y:1,z:-4},rotation:{x:0,y:0,z:0},size:{x:4,y:2,z:4},color:'#c4281c',material:'Brick',anchored:true,canCollide:true,transparency:0,children:[]});
}

function addPart(data, selectIt = true) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(data.size.x,data.size.y,data.size.z), materialFor(data));
  mesh.userData = { ...data, editorType: 'part' };
  mesh.position.set(data.position.x,data.position.y,data.position.z);
  mesh.rotation.set(data.rotation.x || 0,data.rotation.y || 0,data.rotation.z || 0);
  scene.add(mesh);
  objects.push(mesh);
  if (selectIt) select(mesh);
  refreshExplorer();
  return mesh;
}

function addSpawn(pos = { x: 0, y: 3, z: 0 }, selectIt = true) {
  const group = new THREE.Group();
  const pad = new THREE.Mesh(new THREE.CylinderGeometry(1.2, 1.2, .25, 24), new THREE.MeshStandardMaterial({ color: '#00cc44' }));
  const arrow = new THREE.Mesh(new THREE.ConeGeometry(.45, 1.2, 4), new THREE.MeshStandardMaterial({ color: '#ffffff' }));
  arrow.position.y = 1;
  group.add(pad, arrow);
  group.position.set(pos.x, pos.y, pos.z);
  group.userData = { id: crypto.randomUUID(), editorType: 'spawn', name: 'SpawnPoint' };
  scene.add(group);
  spawnPoints.push(group);
  if (selectIt) select(group);
  refreshExplorer();
}

function addScript(data = {}, selectIt = true) {
  const script = { id: data.id || crypto.randomUUID(), name: data.name || 'Script', source: data.source || defaultLuau() };
  scripts.push(script);
  if (selectIt) {
    selected = { userData: { editorType: 'script', ...script } };
    transform.detach();
    renderProps();
  }
  refreshExplorer();
}

function materialFor(d) {
  return new THREE.MeshStandardMaterial({ color:d.color || '#c4281c', roughness:d.material === 'Metal' ? .25 : .85, metalness:d.material === 'Metal' ? .55 : 0, transparent:(d.transparency || 0) > 0, opacity:1 - (d.transparency || 0) });
}

document.querySelectorAll('[data-tool]').forEach(b => b.onclick = () => {
  const tool = b.dataset.tool;
  if (tool === 'part') return addPart(newPart('Part', 'Part', {x:2,y:2,z:2}, '#c4281c'));
  if (tool === 'spawn') return addSpawn({ x: 0, y: 3, z: 0 });
  if (tool === 'script') return addScript();
  transform.setMode(tool);
});

document.querySelectorAll('[data-shape]').forEach(b => b.onclick = () => {
  const shape = b.dataset.shape;
  const data = newPart(shape, shape, shape === 'Cylinder' ? {x:2,y:3,z:2} : {x:2,y:2,z:2}, shape === 'Sphere' ? '#2196f3' : '#c4281c');
  addPart(data);
});

renderer.domElement.addEventListener('click', e => {
  const rect = renderer.domElement.getBoundingClientRect();
  const mouse = new THREE.Vector2((e.clientX-rect.left)/rect.width*2-1,-(e.clientY-rect.top)/rect.height*2+1);
  const ray = new THREE.Raycaster();
  ray.setFromCamera(mouse,camera);
  const hit = ray.intersectObjects([...objects, ...spawnPoints], true)[0];
  if (hit) select(rootEditable(hit.object));
});

addEventListener('keydown', e => {
  if (document.activeElement?.tagName === 'TEXTAREA' || document.activeElement?.tagName === 'INPUT') return;
  if (e.key === 'Delete' && selected) removeSelected();
  if (e.ctrlKey && e.key.toLowerCase() === 'd' && selected?.userData.editorType === 'part') duplicateSelected();
  if (e.ctrlKey && e.key.toLowerCase() === 's') { e.preventDefault(); save(false); }
  if (e.key.toLowerCase() === 'f' && selected?.position) { orbit.target.copy(selected.position); orbit.update(); }
});

function rootEditable(obj) {
  let cur = obj;
  while (cur.parent && !cur.userData.editorType) cur = cur.parent;
  return cur.userData.editorType ? cur : obj;
}

function select(obj) {
  selected = obj;
  if (obj.position) transform.attach(obj); else transform.detach();
  renderProps();
}

function renderProps() {
  const p = document.getElementById('props');
  if (!selected) { p.innerHTML = 'Selecione um objeto'; return; }
  const d = selected.userData;
  if (d.editorType === 'script') {
    p.innerHTML = `<label>Nome<input id="pname" value="${d.name}"></label><label>Luau Script<textarea id="psource" rows="18">${d.source}</textarea></label><p class="muted">API: game.players, game.workspace, game.on(event, fn), player:teleport(x,y,z)</p>`;
    psource.oninput = () => {
      const s = scripts.find(x => x.id === d.id);
      s.source = psource.value; d.source = psource.value;
    };
    pname.oninput = () => { const s = scripts.find(x => x.id === d.id); s.name = pname.value; d.name = pname.value; refreshExplorer(); };
    return;
  }
  p.innerHTML = `<label>Nome<input id="pname" value="${d.name}"></label>
    <label>Pos X<input id="px" type="number" step=".25" value="${selected.position.x.toFixed(2)}"></label>
    <label>Pos Y<input id="py" type="number" step=".25" value="${selected.position.y.toFixed(2)}"></label>
    <label>Pos Z<input id="pz" type="number" step=".25" value="${selected.position.z.toFixed(2)}"></label>
    ${d.editorType === 'part' ? `<label>Tamanho X<input id="sx" type="number" step=".25" value="${d.size.x}"></label><label>Tamanho Y<input id="sy" type="number" step=".25" value="${d.size.y}"></label><label>Tamanho Z<input id="sz" type="number" step=".25" value="${d.size.z}"></label><label>Cor<input id="pcolor" type="color" value="${d.color}"></label><label>Material<select id="pmat"><option>Plastic</option><option>Metal</option><option>Wood</option><option>Stone</option><option>Grass</option><option>Brick</option></select></label><label><input id="pcollide" type="checkbox" ${d.canCollide ? 'checked' : ''}> Colisao</label><label><input id="panchor" type="checkbox" ${d.anchored ? 'checked' : ''}> Ancorado</label>` : ''}`;
  p.querySelectorAll('input,select').forEach(i => i.oninput = applyProps);
  if (p.querySelector('#pmat')) pmat.value = d.material || 'Plastic';
}

function applyProps() {
  const d = selected.userData;
  d.name = pname.value;
  selected.position.set(+px.value,+py.value,+pz.value);
  if (d.editorType === 'part') {
    d.size = { x:+sx.value, y:+sy.value, z:+sz.value };
    d.color = pcolor.value; d.material = pmat.value; d.canCollide = pcollide.checked; d.anchored = panchor.checked;
    selected.geometry.dispose();
    selected.geometry = new THREE.BoxGeometry(d.size.x, d.size.y, d.size.z);
    selected.material = materialFor(d);
  }
  refreshExplorer();
}

function syncSelectedData() {
  if (!selected?.userData || !selected.position) return;
  selected.userData.position = { x:selected.position.x, y:selected.position.y, z:selected.position.z };
  selected.userData.rotation = { x:selected.rotation.x, y:selected.rotation.y, z:selected.rotation.z };
}

function refreshExplorer() {
  document.getElementById('explorer').innerHTML = [
    '<b>Workspace</b>',
    ...objects.map((o,i)=>`<div data-kind="part" data-i="${i}">${o.userData.name}</div>`),
    '<b>SpawnPoints</b>',
    ...spawnPoints.map((o,i)=>`<div data-kind="spawn" data-i="${i}">${o.userData.name}</div>`),
    '<b>Scripts</b>',
    ...scripts.map((s,i)=>`<div data-kind="script" data-i="${i}">${s.name}</div>`)
  ].join('');
}

document.getElementById('explorer').onclick = e => {
  if (!e.target.dataset.kind) return;
  const i = Number(e.target.dataset.i);
  if (e.target.dataset.kind === 'part') select(objects[i]);
  if (e.target.dataset.kind === 'spawn') select(spawnPoints[i]);
  if (e.target.dataset.kind === 'script') { selected = { userData: { editorType:'script', ...scripts[i] } }; transform.detach(); renderProps(); }
};

function mapData() {
  objects.forEach(o => { o.userData.position = {x:o.position.x,y:o.position.y,z:o.position.z}; o.userData.rotation = {x:o.rotation.x,y:o.rotation.y,z:o.rotation.z}; });
  return {
    name: document.getElementById('title').value || 'Novo Mundo',
    version: 1,
    objects: objects.map(o => cleanPart(o.userData)),
    spawnPoints: spawnPoints.map(s => ({ x:s.position.x, y:s.position.y, z:s.position.z })),
    scripts,
    ambient:'#404040',
    skyColor:'#87CEEB'
  };
}

async function save(publish) {
  const body = { title:document.getElementById('title').value || 'Novo Mundo', description:document.getElementById('description').value || 'Criado no Novus Studio', map_data:mapData(), thumbnail_url:renderer.domElement.toDataURL('image/png') };
  if (gameId) await api('/api/games/'+gameId,{method:'PUT',body:JSON.stringify(body)});
  else { const r = await api('/api/games',{method:'POST',body:JSON.stringify(body)}); gameId = r.id; }
  log(publish ? 'Jogo publicado e salvo.' : 'Mapa salvo.');
  toast(publish ? 'Jogo publicado.' : 'Mapa salvo.');
}

document.getElementById('save').onclick = () => save(false);
document.getElementById('publish').onclick = () => save(true);
document.getElementById('test').onclick = async () => { if (!gameId) await save(false); open('/game.html?id='+gameId,'_blank'); };

function duplicateSelected() {
  const d = cleanPart(selected.userData);
  d.id = crypto.randomUUID(); d.name += ' Copy'; d.position.x += 2;
  addPart(d);
}
function removeSelected() {
  if (selected.userData.editorType === 'part') { objects.splice(objects.indexOf(selected),1); selected.removeFromParent(); }
  if (selected.userData.editorType === 'spawn') { spawnPoints.splice(spawnPoints.indexOf(selected),1); selected.removeFromParent(); }
  transform.detach(); selected = null; refreshExplorer(); renderProps();
}
function cleanPart(d) {
  const { editorType, ...rest } = d;
  return JSON.parse(JSON.stringify(rest));
}
function newPart(type, name, size, color) {
  return { id:crypto.randomUUID(), type, name, position:{x:0,y:2,z:0}, rotation:{x:0,y:0,z:0}, size, color, material:'Plastic', anchored:true, canCollide:true, transparency:0, children:[] };
}
function defaultLuau() {
  return `-- Luau simplificado do Novus Worlds\n-- Exemplo:\ngame.on("playerJoin", function(player)\n  player:teleport(0, 5, 0)\nend)\n`;
}
function log(msg) { document.querySelector('.output').textContent = 'Output: ' + msg; }
function loop(){ requestAnimationFrame(loop); renderer.render(scene,camera); }
loop();
addEventListener('resize',()=>{camera.aspect=canvasHost.clientWidth/canvasHost.clientHeight;camera.updateProjectionMatrix();renderer.setSize(canvasHost.clientWidth,canvasHost.clientHeight)});
