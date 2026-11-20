"use strict";
const path = require("path");
require("dotenv").config({ path: path.resolve(__dirname, "../../../../.env") });
require("dotenv").config({
  path: path.resolve(__dirname, "../../../../.env.anti-cheat.local"),
});
const pool = require("../../../config/database");
require("./processor")
  .runUpdate(pool, process.argv[2])
  .catch((e) => {
    console.error(e);
    process.exitCode = 1;
  })
  .finally(() => pool.end());
