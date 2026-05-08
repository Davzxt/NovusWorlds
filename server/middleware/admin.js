function requireAdmin(req, res, next) {
  if (!req.session.user || !req.session.user.is_admin) return res.status(403).json({ error: 'admin_required' });
  next();
}

module.exports = { requireAdmin };
