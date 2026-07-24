declare const api: {
  core: typeof import("./core");
  record: typeof import("./record");
  session: typeof import("./session");
  stats: typeof import("./stats");
  url: typeof import("./url");
};

export = api;
