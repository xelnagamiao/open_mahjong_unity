function parseReplay(text) {
  return JSON.parse(text, (key, value, context) => {
    if (typeof value === 'number' && !Number.isSafeInteger(value) && /^-?\d+$/.test(context?.source || '')) {
      return JSON.rawJSON(context.source);
    }
    return value;
  });
}

module.exports = { parseReplay };
