"use strict";

const zlib = require("zlib");

function decompressZlibBase64(input) {
  return Promise.resolve().then(() => {
    const compressed = Buffer.from(input, "base64");
    return zlib
      .inflateSync(compressed)
      .toString("utf8")
      .replace(/\0/g, "");
  });
}

module.exports = {
  decompressZlibBase64
};
