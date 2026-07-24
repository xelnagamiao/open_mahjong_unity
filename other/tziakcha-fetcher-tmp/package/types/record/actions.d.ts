declare const ACTION_TYPES: Record<string, number>;

declare function decodeTziakchaAction(action: [number, number, number]): {
  playerIndex: number;
  type: number;
  typeName: string;
  data: number;
  time: number;
  detail: Record<string, unknown>;
};

declare const actionApi: {
  ACTION_TYPES: typeof ACTION_TYPES;
  decodeTziakchaAction: typeof decodeTziakchaAction;
};

export = actionApi;
