"use strict";
/* global self, window */

const root =
  (typeof global !== "undefined" && global) ||
  (typeof window !== "undefined" && window) ||
  (typeof self !== "undefined" && self) ||
  {};

function decodeBase64(input) {
  if (typeof Buffer !== "undefined") {
    return Uint8Array.from(Buffer.from(input, "base64"));
  }

  if (typeof root.atob !== "function") {
    throw new Error("当前环境不支持 base64 解码");
  }

  const binary = root.atob(input);
  const bytes = new Uint8Array(binary.length);

  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }

  return bytes;
}

async function decompressZlibBase64(input) {
  if (typeof root.DecompressionStream !== "function") {
    throw new Error('当前环境不支持 DecompressionStream("deflate")');
  }

  const stream = new root.Blob([decodeBase64(input)])
    .stream()
    .pipeThrough(new root.DecompressionStream("deflate"));
  const buffer = await new root.Response(stream).arrayBuffer();

  return new TextDecoder("utf-8").decode(buffer).replace(/\0/g, "");
}

module.exports = {
  decompressZlibBase64
};
