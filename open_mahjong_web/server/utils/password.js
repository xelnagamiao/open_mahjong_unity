const crypto = require('crypto');

const PBKDF2_ITERATIONS = 100_000;

/**
 * 与 Python db_manager.verify_password 一致：salt_hex:hash_hex + PBKDF2-SHA256
 */
function verifyPassword(password, storedHash) {
  if (!storedHash) {
    return false;
  }
  try {
    const [saltHex, storedHashHex] = storedHash.split(':', 2);
    const salt = Buffer.from(saltHex, 'hex');
    const computed = crypto
      .pbkdf2Sync(password, salt, PBKDF2_ITERATIONS, 32, 'sha256')
      .toString('hex');
    return crypto.timingSafeEqual(
      Buffer.from(computed, 'hex'),
      Buffer.from(storedHashHex, 'hex')
    );
  } catch {
    return false;
  }
}

function hashPassword(password) {
  const salt = crypto.randomBytes(16);
  const hash = crypto.pbkdf2Sync(password, salt, PBKDF2_ITERATIONS, 32, 'sha256');
  return `${salt.toString('hex')}:${hash.toString('hex')}`;
}

module.exports = { verifyPassword, hashPassword };
