function parseHistory(raw) {
  if (Array.isArray(raw)) return raw.filter((item) => item && typeof item === 'object');
  if (typeof raw === 'string') {
    try {
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed) ? parsed.filter((item) => item && typeof item === 'object') : [];
    } catch {
      return [];
    }
  }
  return [];
}

function ensureHistory(row) {
  const existing = parseHistory(row?.remark_history);
  if (existing.length > 0) return existing;
  const items = [];
  const remark = String(row?.remark || '').trim();
  if (remark) {
    items.push({
      at: row.created_at || null,
      role: 'applicant',
      action: 'submit',
      text: remark,
    });
  }
  const review = String(row?.review_note || '').trim();
  if (review) {
    items.push({
      at: row.reviewed_at || row.updated_at || null,
      role: 'admin',
      action: row?.status === 'approved' ? 'approve' : 'reject',
      text: review,
    });
  }
  return items;
}

function appendRemark(history, { role, action, text }) {
  const next = Array.isArray(history) ? [...history] : [];
  const body = String(text || '').trim();
  if (!body) return next;
  next.push({
    at: new Date().toISOString(),
    role: role === 'admin' ? 'admin' : 'applicant',
    action: String(action || 'note'),
    text: body.slice(0, 2000),
  });
  return next;
}

function withRemarkHistory(row) {
  if (!row) return row;
  return { ...row, remark_history: ensureHistory(row) };
}

module.exports = { parseHistory, ensureHistory, appendRemark, withRemarkHistory };
