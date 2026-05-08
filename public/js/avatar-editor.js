import { api, toast } from './main.js';
import { createR6Viewer } from './r6-viewer.js';

const state = { colors: { head: '#f5cd30', torso: '#0d69ac', arms: '#f5cd30', legs: '#1b2a35' }, hats: [] };
const data = await api('/api/avatar/me').catch(() => null);
if (data) Object.assign(state, data.avatar || {});
const viewer = createR6Viewer(document.getElementById('avatarPreview'), { avatar: state, spin: false });
const palette = ['#f5cd30','#ffaf00','#d7c59a','#a3a2a5','#ffffff','#111111','#0d69ac','#0055bf','#4b974b','#287f47','#c4281c','#ff0000','#b480ff','#8e44ad','#ff66cc','#f2f3f3'];
document.getElementById('palette').innerHTML = ['head','torso','arms','legs'].map(part => `<h3>${part}</h3><div class="grid">${palette.map(c=>`<button class="swatch" data-part="${part}" data-color="${c}" style="background:${c};height:28px"></button>`).join('')}</div>`).join('');
document.getElementById('palette').onclick = (e) => {
  if (!e.target.dataset.part) return;
  state.colors[e.target.dataset.part] = e.target.dataset.color;
  viewer.avatar.clear();
  import('./r6-viewer.js').then(m => viewer.avatar.add(m.blockR6(state)));
};
const inv = document.getElementById('inventory');
inv.innerHTML = (data?.inventory || []).map(i => `<div class="card"><b>${i.name}</b><p>${i.type}</p><button data-id="${i.id}">${state.hats?.includes(i.id) ? 'Remover' : 'Equipar'}</button></div>`).join('');
inv.onclick = (e) => {
  const id = Number(e.target.dataset.id);
  if (!id) return;
  state.hats = state.hats || [];
  state.hats = state.hats.includes(id) ? state.hats.filter(x => x !== id) : [...state.hats, id].slice(0, 3);
  e.target.textContent = state.hats.includes(id) ? 'Remover' : 'Equipar';
};
document.getElementById('saveAvatar').onclick = async () => {
  await api('/api/avatar/save', { method: 'POST', body: JSON.stringify({ avatar: state }) });
  toast('Avatar salvo.');
};
