function requireAuth(req, res, next) {
  if (!req.session.user) return res.status(401).json({ error: 'login_required' });
  next();
}

module.exports = { requireAuth };
