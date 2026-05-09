import { api, toast } from '../main.js';
import { createBabylonR6Viewer } from '../babylon-r6-viewer.js';

const table = document.getElementById('items');
async function load() {
  const { items } = await api('/api/admin/catalog');
  table.innerHTML = `<table class="table"><tr><th>Thumb</th><th>Nome</th><th>Tipo</th><th>Criador</th><th>Preco</th><th>Vendas</th><th>Status</th><th>Acoes</th></tr>${items.map(i=>`<tr><td><img src="${i.thumbnail_url||'/assets/textures/item-default.svg'}" width="48"></td><td>${i.name}</td><td>${i.type}</td><td>${i.creator}</td><td>Ƀ ${i.price}</td><td>${i.sales_count}</td><td>${i.is_active?'Ativo':'Inativo'}</td><td><button data-action="toggle" data-id="${i.id}">Ativar/Desativar</button><button class="danger" data-action="delete" data-id="${i.id}">Deletar</button><a class="btn secondary" href="/item.html?id=${i.id}">Ver</a></td></tr>`).join('')}</table>`;
}
table.onclick = async e => { if (!e.target.dataset.id) return; await api(`/api/admin/catalog/${e.target.dataset.id}/action`, { method:'POST', body:JSON.stringify({ action:e.target.dataset.action }) }); load(); };
document.getElementById('openAdd').onclick = () => document.getElementById('modal').classList.remove('hidden');
document.getElementById('cancel').onclick = () => document.getElementById('modal').classList.add('hidden');
const viewer = await createBabylonR6Viewer(document.getElementById('hatPreview'), { spin:false });
const transform = { position:{x:0,y:3.38,z:0}, rotation:{x:0,y:0,z:0}, scale:{x:1,y:1,z:1} };
const itemType = document.getElementById('itemType');
const faceWrap = document.getElementById('faceEditorWrap');
const faceCanvas = document.getElementById('faceCanvas');
const faceCtx = faceCanvas.getContext('2d');
faceCtx.imageSmoothingEnabled = false;
faceCtx.clearRect(0, 0, 64, 64);
function syncTypePanels() {
  faceWrap.classList.toggle('hidden', itemType.value !== 'face');
  document.getElementById('controls').classList.toggle('hidden', itemType.value !== 'hat');
}
itemType.onchange = syncTypePanels;
document.getElementById('modelFile').onchange = e => {
  const file = e.target.files?.[0];
  if (!file) return viewer.setHatModelUrl(null);
  viewer.setHatModelUrl(URL.createObjectURL(file)).then(() => viewer.setHatTransform(transform));
};
let drawing = false;
function paintFace(e) {
  const r = faceCanvas.getBoundingClientRect();
  const x = Math.floor((e.clientX - r.left) / r.width * 64);
  const y = Math.floor((e.clientY - r.top) / r.height * 64);
  faceCtx.fillStyle = document.getElementById('faceColor').value;
  faceCtx.fillRect(x, y, 1, 1);
  viewer.setFaceTexture(faceCanvas.toDataURL('image/png'));
}
faceCanvas.onpointerdown = e => { drawing = true; paintFace(e); };
faceCanvas.onpointermove = e => { if (drawing) paintFace(e); };
addEventListener('pointerup', () => drawing = false);
document.getElementById('clearFace').onclick = () => { faceCtx.clearRect(0,0,64,64); viewer.setFaceTexture(faceCanvas.toDataURL('image/png')); };
for (const input of document.querySelectorAll('[data-t]')) input.oninput = () => { const [group,key]=input.dataset.t.split('.'); transform[group][key]=Number(input.value); document.querySelector(`[data-n="${input.dataset.t}"]`).value=input.value; viewer.setHatTransform(transform); };
for (const input of document.querySelectorAll('[data-n]')) input.oninput = () => { const slider=document.querySelector(`[data-t="${input.dataset.n}"]`); slider.value=input.value; slider.dispatchEvent(new Event('input')); };
document.getElementById('fitHead').onclick=()=>{Object.assign(transform.position,{x:0,y:3.38,z:0});Object.assign(transform.rotation,{x:0,y:0,z:0});Object.assign(transform.scale,{x:1,y:1,z:1});syncControls();viewer.setHatTransform(transform)};
document.getElementById('reset').onclick=document.getElementById('fitHead').onclick;
function syncControls(){for(const el of document.querySelectorAll('[data-t]')){const[g,k]=el.dataset.t.split('.');el.value=transform[g][k];document.querySelector(`[data-n="${el.dataset.t}"]`).value=el.value}}
document.getElementById('addForm').onsubmit = async e => {
  e.preventDefault();
  const fd = new FormData(e.target);
  fd.set('hat_transform', JSON.stringify(transform));
  if (itemType.value === 'face') fd.set('face_pixels', faceCanvas.toDataURL('image/png'));
  const res = await fetch('/api/admin/catalog/add', { method:'POST', body:fd });
  const data = await res.json();
  if (!res.ok) return toast(data.error || 'Erro ao salvar.');
  toast('Item salvo.');
  document.getElementById('modal').classList.add('hidden');
  load();
};
load(); syncControls(); syncTypePanels(); viewer.setHatTransform(transform);
