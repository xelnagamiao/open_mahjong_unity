"use strict";

const { createRecordFetchApi } = require("./shared-fetch");
const { decompressZlibBase64 } = require("./decompress-node");

module.exports = createRecordFetchApi(decompressZlibBase64);
