"use strict";
const path = require("path");
require("dotenv").config({ path: path.resolve(__dirname, "../../../../.env") });
require("dotenv").config({
  path: path.resolve(__dirname, "../../../../.env.anti-cheat.local"),
});
const { createAntiCheatPool } = require("./antiCheatPool");
const pool = createAntiCheatPool({ max: 3, applicationName: "anti_cheat_worker" });
const { closeAllWorkerPools, lowerPriority } = require("../pythonWorkerPool");
lowerPriority(process.pid);
require("./processor")
  .runUpdate(pool, process.argv[2])
  .catch((e) => {
    console.error(e);
    process.exitCode = 1;
  })
  .finally(async () => {
    closeAllWorkerPools();
    await pool.end();
  });
