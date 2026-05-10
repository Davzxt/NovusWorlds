import { api, esc } from './main.js';

const gameId = new URLSearchParams(location.search).get('id');
const projectInfo = document.getElementById('projectInfo');
const openStudioBtn = document.getElementById('openStudioBtn');
const copyBtn = document.getElementById('copyBtn');
const status = document.getElementById('status');
let launchData = null;

async function init() {
  let title = 'Novo Mundo';
  let description = 'Projeto novo';
  if (gameId) {
    const { game } = await api('/api/games/' + gameId);
    title = game.title;
    description = game.description || '';
  }
  projectInfo.innerHTML = `<b>${esc(title)}</b><br>${esc(description)}`;
  launchData = await api('/api/legacy/studio-tickets', { method: 'POST', body: JSON.stringify({ gameId }) });
  status.textContent = 'Ticket de Studio criado. Clique em Abrir Studio para iniciar o app Godot.';
}

openStudioBtn.onclick = () => {
  if (!launchData) return;
  location.href = launchData.protocolUrl;
  status.textContent = 'Tentando abrir o launcher local.';
};

copyBtn.onclick = async () => {
  if (!launchData) return;
  await navigator.clipboard.writeText(launchData.protocolUrl);
  status.textContent = 'Protocolo copiado.';
};

init().catch(err => {
  status.textContent = err.message;
});
