import { api, toast } from '../main.js';
import { animateR6 } from '../babylon-r6.js';
import { createBabylonR6Viewer } from '../babylon-r6-viewer.js';

const form = document.getElementById('animForm');
const viewer = await createBabylonR6Viewer(document.getElementById('animPreview'), { spin: false });
const keys = ['idle', 'walk', 'jump', 'fall', 'climb'];
let active = 'walk';
let presets = {};

const labels = {
  idle: 'Parado',
  walk: 'Andar',
  jump: 'Pular',
  fall: 'Cair',
  climb: 'Escalar'
};

async function load() {
  const data = await api('/api/admin/animations');
  presets = Object.fromEntries(data.animations.map(a => [a.key, a.data]));
  renderForm();
}

function renderForm() {
  const p = presets[active] || {};
  form.innerHTML = `
    <div class="tabs">${keys.map(k => `<button type="button" class="tab ${k === active ? 'active' : ''}" data-key="${k}">${labels[k]}</button>`).join('')}</div>
    <label class="field"><span>Velocidade</span><input name="speed" type="range" min="0" max="14" step=".1" value="${p.speed ?? 1}"><input name="speedNumber" type="number" step=".1" value="${p.speed ?? 1}"></label>
    <label class="field"><span>Bracos</span><input name="arm" type="range" min="-1.4" max="1.4" step=".01" value="${p.arm ?? 0}"><input name="armNumber" type="number" step=".01" value="${p.arm ?? 0}"></label>
    <label class="field"><span>Pernas</span><input name="leg" type="range" min="-1.4" max="1.4" step=".01" value="${p.leg ?? 0}"><input name="legNumber" type="number" step=".01" value="${p.leg ?? 0}"></label>
    <label class="field"><span>Torso</span><input name="torso" type="range" min="-.5" max=".5" step=".01" value="${p.torso ?? 0}"><input name="torsoNumber" type="number" step=".01" value="${p.torso ?? 0}"></label>
    <p><button>Salvar ${labels[active]}</button></p>
  `;
  form.querySelectorAll('[data-key]').forEach(btn => btn.onclick = () => { active = btn.dataset.key; renderForm(); });
  for (const name of ['speed', 'arm', 'leg', 'torso']) {
    form[name].oninput = () => { form[`${name}Number`].value = form[name].value; presets[active][name] = Number(form[name].value); };
    form[`${name}Number`].oninput = () => { form[name].value = form[`${name}Number`].value; presets[active][name] = Number(form[name].value); };
  }
}

form.onsubmit = async e => {
  e.preventDefault();
  await api('/api/admin/animations/' + active, { method: 'POST', body: JSON.stringify(presets[active]) });
  toast('Animacao salva.');
};

function animatePreview() {
  requestAnimationFrame(animatePreview);
  const p = presets[active] || {};
  animateR6(viewer.avatar, active, performance.now() / 1000, { [active]: p });
}

load();
animatePreview();
