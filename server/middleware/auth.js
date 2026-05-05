function requireAuth(req, res, next) {
  if (!req.session.userId) {
    return res.status(401).json({ error: 'Authentication required' });
  }
  next();
}

function requireGuest(req, res, next) {
  if (req.session.userId) {
    return res.status(400).json({ error: 'Already authenticated' });
  }
  next();
}

module.exports = { requireAuth, requireGuest };