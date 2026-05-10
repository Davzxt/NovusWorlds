import { api, esc } from './main.js';

const gameId = new URLSearchParams(location.search).get('id') || '1';
const gameInfo = document.getElementById('gameInfo');
const launchBtn = document.getElementById('launchBtn');
const copyBtn = document.getElementById('copyBtn');
const status = document.getElementById('status');
let launchData = null;

async function init() {
  const { game } = await api('/api/games/' + gameId);
  gameInfo.innerHTML = `<b>${esc(game.title)}</b><br>Criador: ${esc(game.creator)}<br>${esc(game.description || '')}`;
  launchData = await api('/api/legacy/tickets', { method: 'POST', body: JSON.stringify({ gameId }) });
  status.textContent = 'Ticket criado. Instale o launcher para abrir o client antigo.';
}

launchBtn.onclick = async () => {
  if (!launchData) return;
  location.href = launchData.protocolUrl;
  status.textContent = 'Tentando abrir o launcher local. Se nada abrir, o protocolo novus:// ainda nao esta instalado.';
};

copyBtn.onclick = async () => {
  if (!launchData) return;
  await navigator.clipboard.writeText(launchData.protocolUrl);
  status.textContent = 'Protocolo copiado.';
};

init().catch(err => {
  status.textContent = err.message;
});
