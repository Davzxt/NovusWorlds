import * as THREE from '/vendor/three/build/three.module.js';
import { OrbitControls } from '/vendor/three/examples/jsm/controls/OrbitControls.js';
import { TransformControls } from '/vendor/three/examples/jsm/controls/TransformControls.js';
import { api, toast } from './main.js';

const canvasHost = document.getElementById('viewport');
const scene = new THREE.Scene(); scene.background = new THREE.Color('#87CEEB');
const camera = new THREE.PerspectiveCamera(60, canvasHost.clientWidth / canvasHost.clientHeight, .1, 1000); camera.position.set(8,8,10);
const renderer = new THREE.WebGLRenderer({ antialias:true, preserveDrawingBuffer:true }); renderer.setSize(canvasHost.clientWidth, canvasHost.clientHeight); canvasHost.appendChild(renderer.domElement);
const orbit = new OrbitControls(camera, renderer.domElement); orbit.target.set(0,0,0); orbit.update();
const transform = new TransformControls(camera, renderer.domElement); scene.add(transform); transform.addEventListener('dragging-changed', e => orbit.enabled = !e.value);
scene.add(new THREE.GridHelper(100,100), new THREE.HemisphereLight(0xffffff,0x333333,1.8));
const objects = [], history = []; let selected = null, gameId = new URLSearchParams(location.search).get('id');
function part(data){const mesh=new THREE.Mesh(new THREE.BoxGeometry(data.size.x,data.size.y,data.size.z),new THREE.MeshStandardMaterial({color:data.color}));mesh.userData=data;mesh.position.set(data.position.x,data.position.y,data.position.z);scene.add(mesh);objects.push(mesh);refreshExplorer();return mesh}
part({id:crypto.randomUUID(),type:'Part',name:'Baseplate',position:{x:0,y:-.5,z:0},rotation:{x:0,y:0,z:0},size:{x:80,y:1,z:80},color:'#6B8E23',material:'Grass',anchored:true,canCollide:true,transparency:0,children:[]});
document.querySelectorAll('[data-tool]').forEach(b=>b.onclick=()=>{if(b.dataset.tool==='part')part({id:crypto.randomUUID(),type:'Part',name:'Part',position:{x:0,y:2,z:0},rotation:{x:0,y:0,z:0},size:{x:2,y:2,z:2},color:'#c4281c',material:'Plastic',anchored:true,canCollide:true,transparency:0,children:[]});else transform.setMode(b.dataset.tool)});
renderer.domElement.addEventListener('click', e=>{const rect=renderer.domElement.getBoundingClientRect();const mouse=new THREE.Vector2((e.clientX-rect.left)/rect.width*2-1,-(e.clientY-rect.top)/rect.height*2+1);const ray=new THREE.Raycaster();ray.setFromCamera(mouse,camera);const hit=ray.intersectObjects(objects)[0];if(hit)select(hit.object)});
function select(o){selected=o;transform.attach(o);renderProps()}
function renderProps(){const p=document.getElementById('props');if(!selected){p.innerHTML='Selecione um objeto';return}const d=selected.userData;p.innerHTML=`<label>Nome<input id="pname" value="${d.name}"></label><label>Cor<input id="pcolor" type="color" value="${d.color}"></label><label>X<input id="px" type="number" value="${selected.position.x}"></label><label>Y<input id="py" type="number" value="${selected.position.y}"></label><label>Z<input id="pz" type="number" value="${selected.position.z}"></label><label>Ancorado<select id="pa"><option value="1">sim</option><option value="0">nao</option></select></label>`;p.querySelectorAll('input,select').forEach(i=>i.oninput=()=>{d.name=pname.value;d.color=pcolor.value;selected.material.color.set(d.color);selected.position.set(+px.value,+py.value,+pz.value);refreshExplorer()})}
function refreshExplorer(){document.getElementById('explorer').innerHTML=objects.map((o,i)=>`<div data-i="${i}">${o.userData.name}</div>`).join('')}
document.getElementById('explorer').onclick=e=>{if(e.target.dataset.i)select(objects[e.target.dataset.i])};
function mapData(){return{name:document.getElementById('title').value||'Novo Mundo',version:1,objects:objects.map(o=>({...o.userData,position:{x:o.position.x,y:o.position.y,z:o.position.z},rotation:{x:o.rotation.x,y:o.rotation.y,z:o.rotation.z}})),spawnPoints:[{x:0,y:4,z:0}],ambient:'#404040',skyColor:'#87CEEB'}}
document.getElementById('save').onclick=async()=>{const body={title:document.getElementById('title').value||'Novo Mundo',description:'Criado no Novus Studio',map_data:mapData(),thumbnail_url:renderer.domElement.toDataURL('image/png')};if(gameId)await api('/api/games/'+gameId,{method:'PUT',body:JSON.stringify(body)});else{const r=await api('/api/games',{method:'POST',body:JSON.stringify(body)});gameId=r.id}toast('Mapa salvo.')};
document.getElementById('test').onclick=()=>{if(gameId)open('/game.html?id='+gameId,'_blank');else toast('Salve antes de testar.')};
function loop(){requestAnimationFrame(loop);renderer.render(scene,camera)}loop();
