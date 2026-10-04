const MIN_DISPLAY_LENGTH = 2;
const MAX_DISPLAY_LENGTH = 20;
const USERNAME_RULE_HINT = '用户名应当在2-20个字符之间，只能包含中文、数字及英文，中文计两个字符。';

function normalizeUsername(value) {
  return String(value ?? '').normalize('NFC').trim();
}

function isChineseUsernameCharacter(char) {
  return /\p{Script=Han}/u.test(char);
}

function isAllowedUsernameCharacter(char) {
  return /[A-Za-z0-9]/.test(char) || isChineseUsernameCharacter(char);
}

function usernameDisplayLength(username) {
  let length = 0;
  for (const char of username) {
    if (/\p{Mark}/u.test(char)) continue;
    length += isChineseUsernameCharacter(char) ? 2 : 1;
  }
  return length;
}

/** 与游戏服的用户名规则保持一致。 */
function validateUsername(username) {
  const name = normalizeUsername(username);
  if (!name) return '用户名不能为空';
  for (const char of name) {
    if (!isAllowedUsernameCharacter(char)) return USERNAME_RULE_HINT;
  }
  const length = usernameDisplayLength(name);
  if (length < MIN_DISPLAY_LENGTH || length > MAX_DISPLAY_LENGTH) {
    return USERNAME_RULE_HINT;
  }
  return null;
}

module.exports = {
  USERNAME_RULE_HINT,
  normalizeUsername,
  usernameDisplayLength,
  validateUsername,
};
