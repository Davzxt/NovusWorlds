import { api, toast } from './main.js';
import { createR6Viewer, applyHats } from './r6-viewer.js';

const state = { colors: { head: '#f5cd30', torso: '#0d69ac', arms: '#f5cd30', legs: '#1b2a35' }, hats: [] };
const data = await api('/api/avatar/me').catch(() => null);
if (data) Object.assign(state, data.avatar || {});
const viewer = createR6Viewer(document.getElementById('avatarPreview'), { avatar: state, spin: false });
function refreshPreview() {
  viewer.avatar.traverse((obj) => {
    if (!obj.isMesh || !obj.material?.color) return;
    const n = obj.name.toLowerCase();
    if (n.includes('head') || n.includes('pyramid')) obj.material.color.set(state.colors.head || '#f5cd30');
    else if (n.includes('arm')) obj.material.color.set(state.colors.arms || '#f5cd30');
    else if (n.includes('leg') || n === 'mesh') obj.material.color.set(state.colors.legs || '#1b2a35');
    else obj.material.color.set(state.colors.torso || '#0d69ac');
  });
  applyHats(viewer.avatar, state);
  avatarSummary.textContent = `Face: ${state.face || 'Classic Smile'} | Chapeus: ${(state.hats || []).length}/3`;
}
const palette = ['#f5cd30','#ffaf00','#d7c59a','#a3a2a5','#ffffff','#111111','#0d69ac','#0055bf','#4b974b','#287f47','#c4281c','#ff0000','#b480ff','#8e44ad','#ff66cc','#f2f3f3'];
document.getElementById('palette').innerHTML = ['head','torso','arms','legs'].map(part => `<h3>${part}</h3><div class="grid">${palette.map(c=>`<button class="swatch" data-part="${part}" data-color="${c}" style="background:${c};height:28px"></button>`).join('')}</div>`).join('');
document.getElementById('palette').onclick = (e) => {
  if (!e.target.dataset.part) return;
  state.colors[e.target.dataset.part] = e.target.dataset.color;
  refreshPreview();
};
const inv = document.getElementById('inventory');
const inventory = data?.inventory || [];
inv.innerHTML = inventory.filter(i => i.type !== 'face').map(i => `<div class="card"><b>${i.name}</b><p>${i.type}</p><button data-id="${i.id}">${state.hats?.includes(i.id) ? 'Remover' : 'Equipar'}</button></div>`).join('') || '<p>Nenhum item comprado ainda.</p>';
inv.onclick = (e) => {
const id = Number(e.target.dataset.id);
  if (!id) return;
  state.hats = state.hats || [];
  state.hats = state.hats.includes(id) ? state.hats.filter(x => x !== id) : [...state.hats, id].slice(0, 3);
  state.equippedItems = inventory.filter(i => state.hats.includes(i.id));
  e.target.textContent = state.hats.includes(id) ? 'Remover' : 'Equipar';
  refreshPreview();
};
const builtInFaces = ['Classic Smile', 'Serious Face', 'Chill Face'];
faces.innerHTML = builtInFaces.map(face => `<div class="card"><b>${face}</b><p>Face gratis</p><button data-face="${face}">${state.face === face ? 'Usando' : 'Usar'}</button></div>`).join('');
faces.onclick = (e) => {
  if (!e.target.dataset.face) return;
  state.face = e.target.dataset.face;
  faces.querySelectorAll('button').forEach(b => b.textContent = b.dataset.face === state.face ? 'Usando' : 'Usar');
  refreshPreview();
};
document.querySelectorAll('[data-tab]').forEach((btn) => btn.onclick = () => {
  document.querySelectorAll('[data-tab]').forEach(b => b.classList.toggle('active', b === btn));
  document.querySelectorAll('[data-pane]').forEach(p => p.classList.toggle('hidden', p.dataset.pane !== btn.dataset.tab));
});
document.getElementById('saveAvatar').onclick = async () => {
  await api('/api/avatar/save', { method: 'POST', body: JSON.stringify({ avatar: state }) });
  toast('Avatar salvo.');
};
refreshPreview();
