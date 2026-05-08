import * as THREE from '/vendor/three/build/three.module.js';
import { api, currentUser, esc } from './main.js';
import { blockR6 } from './r6-viewer.js';

const id = new URLSearchParams(location.search).get('id') || '1';
const { game } = await api('/api/games/' + id);
const user = await currentUser() || { id: Math.random().toString(36).slice(2), username: 'Guest', avatar_data: {} };
document.getElementById('gameTitle').textContent = game.title;
const scene = new THREE.Scene(); scene.background = new THREE.Color(game.map_data.skyColor || '#87CEEB');
const camera = new THREE.PerspectiveCamera(70, innerWidth / innerHeight, .1, 500);
const renderer = new THREE.WebGLRenderer({ antialias: true }); renderer.setSize(innerWidth, innerHeight); document.body.appendChild(renderer.domElement); renderer.domElement.className='game-canvas';
scene.add(new THREE.HemisphereLight(0xffffff, 0x333333, 1.8));
const local = blockR6(user.avatar_data); scene.add(local);
const spawn = game.map_data.spawnPoints?.[0] || { x: 0, y: 4, z: 0 }; local.position.set(spawn.x, spawn.y, spawn.z);
const colliders = [];
for (const o of game.map_data.objects || []) addPart(o);
function addPart(o){const m=new THREE.Mesh(new THREE.BoxGeometry(o.size.x,o.size.y,o.size.z),new THREE.MeshStandardMaterial({color:o.color,transparent:o.transparency>0,opacity:1-(o.transparency||0)}));m.position.set(o.position.x,o.position.y,o.position.z);m.rotation.set(o.rotation.x,o.rotation.y,o.rotation.z);scene.add(m);if(o.canCollide)colliders.push(m)}
const players = new Map(), keys = {};
addEventListener('keydown', e => keys[e.key.toLowerCase()] = true); addEventListener('keyup', e => keys[e.key.toLowerCase()] = false);
let velY = 0, yaw = 0, grounded = false;
addEventListener('mousemove', e => { if (document.pointerLockElement) yaw -= e.movementX * .003; });
renderer.domElement.onclick = () => renderer.domElement.requestPointerLock();
const ws = new WebSocket(`${location.protocol==='https:'?'wss':'ws'}://${location.host}/ws/game/${id}`);
ws.onopen = () => ws.send(JSON.stringify({ type:'join', gameId:id, userId:user.id, username:user.username, avatarData:user.avatar_data, position:local.position }));
ws.onmessage = ev => {const d=JSON.parse(ev.data); if(d.type==='world_state') sync(d.players); if(d.type==='player_leave'){players.get(d.playerId)?.removeFromParent();players.delete(d.playerId)} if(d.type==='chat_broadcast') addChat(d)};
setInterval(()=>{if(ws.readyState===1)ws.send(JSON.stringify({type:'move',position:local.position,rotation:{x:0,y:yaw,z:0},animation:moving()?'walk':'idle'}))},50);
document.getElementById('chatInput').addEventListener('keydown',e=>{if(e.key==='Enter'&&e.target.value.trim()){ws.send(JSON.stringify({type:'chat',message:e.target.value.trim()}));e.target.value=''}});
document.getElementById('leaveBtn').onclick=()=>location.href='/games.html';
function sync(list){document.getElementById('onlineCount').textContent=list.length;for(const p of list){if(String(p.id)===String(user.id))continue;let g=players.get(p.id);if(!g){g=blockR6(p.avatar);scene.add(g);players.set(p.id,g)}g.position.lerp(new THREE.Vector3(p.position.x,p.position.y,p.position.z),.35);g.rotation.y=p.rotation?.y||0}}
function moving(){return keys.w||keys.a||keys.s||keys.d}
function addChat(d){const log=document.getElementById('gameChatLog');log.insertAdjacentHTML('beforeend',`<div><b>${esc(d.from)}:</b> ${esc(d.message)}</div>`);log.scrollTop=log.scrollHeight}
function tick(){requestAnimationFrame(tick);const speed=keys.shift?0.16:0.09;const dir=new THREE.Vector3((keys.d?1:0)-(keys.a?1:0),0,(keys.s?1:0)-(keys.w?1:0));if(dir.length())dir.normalize().applyAxisAngle(new THREE.Vector3(0,1,0),yaw);local.position.addScaledVector(dir,speed);local.rotation.y=yaw;velY-=.018;if(keys[' ']&&grounded){velY=.28;grounded=false}local.position.y+=velY;if(local.position.y<spawn.y){local.position.y=spawn.y;velY=0;grounded=true}camera.position.set(local.position.x+Math.sin(yaw)*6,local.position.y+3,local.position.z+Math.cos(yaw)*6);camera.lookAt(local.position.x,local.position.y+1.4,local.position.z);renderer.render(scene,camera);document.querySelector('.loading')?.remove()}tick();
addEventListener('resize',()=>{camera.aspect=innerWidth/innerHeight;camera.updateProjectionMatrix();renderer.setSize(innerWidth,innerHeight)});
