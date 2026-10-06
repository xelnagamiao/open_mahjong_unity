const STORAGE_KEY = 'player_login_preferences'

export function loadLoginPreferences() {
  const defaults = { username: '', rememberPassword: false, keepLoggedIn: true }
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY))
    if (!saved || typeof saved !== 'object') return defaults
    const rememberPassword = saved.rememberPassword === true
    return {
      username: rememberPassword && typeof saved.username === 'string' ? saved.username : '',
      rememberPassword,
      keepLoggedIn: saved.keepLoggedIn !== false,
    }
  } catch {
    return defaults
  }
}

export function saveLoginPreferences({ username, rememberPassword, keepLoggedIn }) {
  try {
    // Passwords belong to the browser password manager, never to website storage.
    localStorage.setItem(STORAGE_KEY, JSON.stringify({
      username: rememberPassword ? username : '',
      rememberPassword: rememberPassword === true,
      keepLoggedIn: keepLoggedIn === true,
    }))
  } catch {
    // Login still works when browser storage is unavailable.
  }
}

export async function saveBrowserPassword(username, password) {
  try {
    if (typeof PasswordCredential === 'undefined' || !navigator.credentials?.store) return false
    await navigator.credentials.store(new PasswordCredential({ id: username, password }))
    return true
  } catch {
    // Declining a password-manager prompt must not turn a successful login into a failure.
    return false
  }
}

export async function getBrowserPassword() {
  try {
    if (typeof PasswordCredential === 'undefined' || !navigator.credentials?.get) return null
    const credential = await navigator.credentials.get({ password: true, mediation: 'optional' })
    return credential?.type === 'password' ? credential : null
  } catch {
    return null
  }
}
