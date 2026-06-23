/**
 * 与游戏服 validate_username 规则一致
 */
function validateUsername(username) {
  if (!username || !String(username).trim()) {
    return '用户名不能为空';
  }
  const name = String(username).trim();
  if (name.length > 16) {
    return '用户名不能超过16个字符';
  }
  let length = 0;
  for (const char of name) {
    const code = char.charCodeAt(0);
    if (code >= 0x4e00 && code <= 0x9fff) {
      length += 2;
    } else if (/[a-zA-Z]/.test(char)) {
      length += 1;
    } else if (/[0-9]/.test(char)) {
      length += 1;
    } else {
      return '用户名只能包含中文、英文或数字';
    }
  }
  if (length < 2) {
    return '用户名长度至少需要2（中文=2，数字=1，英文=1）';
  }
  if (length > 20) {
    return '用户名不能超过20';
  }
  return null;
}

module.exports = { validateUsername };
