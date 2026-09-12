const zlib = require('zlib');

const SIG_EOCD = 0x06054b50;
const SIG_CD = 0x02014b50;
const SIG_LOCAL = 0x04034b50;

function httpError(message, status = 400) {
  const err = new Error(message);
  err.status = status;
  return err;
}

function isZip(buffer) {
  return Buffer.isBuffer(buffer) && buffer.length >= 4 && buffer[0] === 0x50 && buffer[1] === 0x4b;
}

function isUtf8(buf) {
  try {
    new TextDecoder('utf-8', { fatal: true }).decode(buf);
    return true;
  } catch {
    return false;
  }
}

function decodeName(buf, utf8Flag) {
  if (!buf.length) return '';
  if (utf8Flag || isUtf8(buf)) return buf.toString('utf8');
  try {
    return new TextDecoder('gb18030').decode(buf);
  } catch {
    return buf.toString('latin1');
  }
}

function findEocd(buf) {
  const min = Math.max(0, buf.length - 22 - 65535);
  for (let i = buf.length - 22; i >= min; i -= 1) {
    if (buf.readUInt32LE(i) !== SIG_EOCD) continue;
    const commentLen = buf.readUInt16LE(i + 20);
    if (i + 22 + commentLen === buf.length) return i;
  }
  throw httpError('不是有效的 zip 文件');
}

function inflate(method, compressed, usize) {
  if (method === 0) {
    if (compressed.length !== usize) {
      throw httpError('不是有效的 zip 文件');
    }
    return Buffer.from(compressed);
  }
  if (method === 8) {
    try {
      const out = zlib.inflateRawSync(compressed, { maxOutputLength: usize });
      if (out.length !== usize) {
        throw httpError('不是有效的 zip 文件');
      }
      return out;
    } catch (err) {
      if (err.status) throw err;
      throw httpError('不是有效的 zip 文件');
    }
  }
  throw httpError('不支持的压缩方式');
}

function readZip(buffer, { maxUncompressed = 20 * 1024 * 1024, maxEntries = 400 } = {}) {
  if (!isZip(buffer)) {
    throw httpError('不是有效的 zip 文件');
  }
  if (buffer.length > maxUncompressed) {
    throw httpError('压缩包过大（超过 20MB）');
  }

  const eocd = findEocd(buffer);
  const diskEntries = buffer.readUInt16LE(eocd + 8);
  const totalEntries = buffer.readUInt16LE(eocd + 10);
  const cdSize = buffer.readUInt32LE(eocd + 12);
  const cdOffset = buffer.readUInt32LE(eocd + 16);
  if (diskEntries !== totalEntries || totalEntries > maxEntries) {
    throw httpError('压缩包文件数量过多');
  }
  if (cdOffset + cdSize > buffer.length) {
    throw httpError('不是有效的 zip 文件');
  }

  const listings = [];
  let cursor = cdOffset;
  const cdEnd = cdOffset + cdSize;
  let uncompressed = 0;
  for (let i = 0; i < totalEntries; i += 1) {
    if (cursor + 46 > cdEnd || buffer.readUInt32LE(cursor) !== SIG_CD) {
      throw httpError('不是有效的 zip 文件');
    }
    const flag = buffer.readUInt16LE(cursor + 8);
    const method = buffer.readUInt16LE(cursor + 10);
    const crc = buffer.readUInt32LE(cursor + 16);
    const csize = buffer.readUInt32LE(cursor + 20);
    const usize = buffer.readUInt32LE(cursor + 24);
    const nameLen = buffer.readUInt16LE(cursor + 28);
    const extraLen = buffer.readUInt16LE(cursor + 30);
    const commentLen = buffer.readUInt16LE(cursor + 32);
    const localOffset = buffer.readUInt32LE(cursor + 42);
    const nameBuf = buffer.subarray(cursor + 46, cursor + 46 + nameLen);
    cursor += 46 + nameLen + extraLen + commentLen;
    if (cursor > cdEnd) {
      throw httpError('不是有效的 zip 文件');
    }
    if (flag & 0x0001) {
      throw httpError('不支持加密压缩包');
    }
    const name = decodeName(nameBuf, Boolean(flag & 0x0800)).replace(/\\/g, '/');
    if (!name || name.endsWith('/')) continue;
    uncompressed += usize;
    if (uncompressed > maxUncompressed) {
      throw httpError('解压后超过 20MB');
    }
    listings.push({ name, method, crc, csize, usize, localOffset });
  }

  const files = [];
  for (const item of listings) {
    const lower = item.name.replace(/\\/g, '/').toLowerCase();
    const base = lower.split('/').pop() || '';
    if (lower.startsWith('__macosx/') || base === '.ds_store' || base.startsWith('._')) continue;
    if (item.localOffset + 30 > buffer.length || buffer.readUInt32LE(item.localOffset) !== SIG_LOCAL) {
      throw httpError('不是有效的 zip 文件');
    }
    const localNameLen = buffer.readUInt16LE(item.localOffset + 26);
    const localExtraLen = buffer.readUInt16LE(item.localOffset + 28);
    const dataStart = item.localOffset + 30 + localNameLen + localExtraLen;
    if (dataStart + item.csize > buffer.length) {
      throw httpError('不是有效的 zip 文件');
    }
    const compressed = buffer.subarray(dataStart, dataStart + item.csize);
    const data = inflate(item.method, compressed, item.usize);
    files.push({ name: item.name.replace(/\\/g, '/'), data });
  }
  return files;
}

module.exports = { isZip, readZip };
