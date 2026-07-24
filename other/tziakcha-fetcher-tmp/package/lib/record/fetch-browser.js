"use strict";

const { createRecordFetchApi } = require("./shared-fetch");
const { decompressZlibBase64 } = require("./decompress-browser");

module.exports = createRecordFetchApi(decompressZlibBase64);
