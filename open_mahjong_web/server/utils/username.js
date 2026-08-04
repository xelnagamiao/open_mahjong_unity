const MAX_CODE_POINTS = 16;
const MIN_DISPLAY_LENGTH = 2;
const MAX_DISPLAY_LENGTH = 20;

// Keep these ranges identical to the Python game server implementation.
const WIDE_RANGES = [
  [0x1100, 0x11ff], [0x2e80, 0x303f], [0x3040, 0x30ff],
  [0x3100, 0x318f], [0x31a0, 0x31bf], [0x31f0, 0x31ff],
  [0x3400, 0x4dbf], [0x4e00, 0x9fff], [0xa960, 0xa97f],
  [0xac00, 0xd7af], [0xd7b0, 0xd7ff], [0xf900, 0xfaff],
  [0xfe10, 0xfe6f], [0xff01, 0xff60], [0xff61, 0xff9f],
  [0xffe0, 0xffe6], [0x20000, 0x323af],
];

function normalizeUsername(value) {
  return String(value ?? '').normalize('NFC').trim();
}

function isWideUsernameCharacter(char) {
  const codePoint = char.codePointAt(0);
  return WIDE_RANGES.some(([start, end]) => codePoint >= start && codePoint <= end);
}

function usernameDisplayLength(username) {
  let length = 0;
  for (const char of username) {
    if (/\p{Mark}/u.test(char)) continue;
    length += isWideUsernameCharacter(char) ? 2 : 1;
  }
  return length;
}

/** 与游戏服的 Unicode 用户名规则保持一致。 */
function validateUsername(username) {
  const name = normalizeUsername(username);
  if (!name) return '用户名不能为空';
  if ([...name].length > MAX_CODE_POINTS) {
    return `用户名不能超过${MAX_CODE_POINTS}个字符`;
  }
  if (/[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]/u.test(name)) {
    return '用户名不能包含控制字符或不可见格式字符';
  }
  const length = usernameDisplayLength(name);
  if (length < MIN_DISPLAY_LENGTH) {
    return '用户名长度至少需要2（中日韩及全角字符=2，其他字符=1）';
  }
  if (length > MAX_DISPLAY_LENGTH) return '用户名显示长度不能超过20';
  return null;
}

module.exports = { normalizeUsername, usernameDisplayLength, validateUsername };
