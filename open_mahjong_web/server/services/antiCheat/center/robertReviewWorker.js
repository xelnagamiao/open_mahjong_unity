"use strict";
const path = require("node:path");
require("dotenv").config({ path: path.resolve(__dirname, "../../../../.env") });
require("dotenv").config({
  path: path.resolve(__dirname, "../../../../.env.anti-cheat.local"),
});
const { createAntiCheatPool } = require("./antiCheatPool");
const pool = createAntiCheatPool({ max: 2, applicationName: "anti_cheat_robert_worker" });
const { closeAllWorkerPools, lowerPriority } = require("../pythonWorkerPool");
const { drainPersistentRobertReviews } = require("./robertReviews");

lowerPriority(process.pid);

drainPersistentRobertReviews(pool)
  .catch((e) => {
    console.error(e);
    process.exitCode = 1;
  })
  .finally(async () => {
    closeAllWorkerPools();
    await pool.end();
  });
