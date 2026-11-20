'use strict';

function selectPython(options = {}, env = process.env, platform = process.platform) {
  return options.python_binary || env.ANTI_CHEAT_PYTHON || (platform === 'win32' ? 'python' : 'python3');
}

module.exports = { selectPython };
