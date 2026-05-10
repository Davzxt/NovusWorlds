import { api, esc } from './main.js';

const gameId = new URLSearchParams(location.search).get('id') || '1';
const gameInfo = document.getElementById('gameInfo');
const launchBtn = document.getElementById('launchBtn');
const copyBtn = document.getElementById('copyBtn');
const status = document.getElementById('status');

async function init() {
  const { game } = await api('/api/games/' + gameId);
  gameInfo.innerHTML = `<b>${esc(game.title)}</b><br>Criador: ${esc(game.creator)}<br>${esc(game.description || '')}`;
  status.textContent = 'Pronto para abrir. O ticket sera criado no momento do clique.';
}

async function createLaunchTicket() {
  status.textContent = 'Criando ticket novo...';
  return api('/api/legacy/tickets', { method: 'POST', body: JSON.stringify({ gameId }) });
}

launchBtn.onclick = async () => {
  launchBtn.disabled = true;
  try {
    const launchData = await createLaunchTicket();
    location.href = launchData.protocolUrl;
    status.textContent = 'Tentando abrir o launcher local. Se nada abrir, o protocolo novus:// ainda nao esta instalado.';
  } catch (err) {
    status.textContent = err.message || 'Erro ao criar ticket.';
  } finally {
    setTimeout(() => { launchBtn.disabled = false; }, 2500);
  }
};

copyBtn.onclick = async () => {
  try {
    const launchData = await createLaunchTicket();
    await navigator.clipboard.writeText(launchData.protocolUrl);
    status.textContent = 'Protocolo novo copiado.';
  } catch (err) {
    status.textContent = err.message || 'Erro ao criar ticket.';
  }
};

init().catch(err => {
  status.textContent = err.message;
});
