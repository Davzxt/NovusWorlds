import { api, toast } from './main.js';

const login = document.getElementById('loginForm');
if (login) login.addEventListener('submit', async (e) => {
  e.preventDefault();
  try {
    await api('/api/auth/login', { method: 'POST', body: JSON.stringify(Object.fromEntries(new FormData(login))) });
    location.href = '/';
  } catch (err) { toast(err.message); }
});

const register = document.getElementById('registerForm');
if (register) register.addEventListener('submit', async (e) => {
  e.preventDefault();
  try {
    await api('/api/auth/register', { method: 'POST', body: JSON.stringify(Object.fromEntries(new FormData(register))) });
    location.href = '/avatar.html';
  } catch (err) { toast(err.message); }
});
