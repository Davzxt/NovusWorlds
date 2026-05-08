import { api } from '../main.js';
const { stats, activity, registrations, topGames } = await api('/api/admin/stats');
document.getElementById('stats').innerHTML = Object.entries(stats).map(([k,v])=>`<div class="stat"><b>${k}</b><h2>${v}</h2></div>`).join('');
document.getElementById('activity').innerHTML = activity.map(a=>`<li>${a.created_at} - ${a.message}</li>`).join('');
document.getElementById('charts').innerHTML = '<h3>Registros por dia</h3>'+registrations.map(r=>`<div>${r.day}<div style="background:#2196f3;height:14px;width:${Math.max(12,r.count*25)}px"></div></div>`).join('')+'<h3>Jogos mais jogados</h3>'+topGames.map(g=>`<div>${g.title}<div style="background:#4caf50;height:14px;width:${Math.max(12,g.visit_count*10)}px"></div></div>`).join('');
