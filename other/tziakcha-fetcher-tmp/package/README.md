# tziakcha-fetcher

获取 tziakcha 牌谱数据并提供浏览器安全的公共接入面。

## 安装

```sh
npm install tziakcha-fetcher
```

## 示例

```js
const { record, session, stats, url } = require("tziakcha-fetcher");

async function main() {
  const sessionId = url.parseTziakchaSessionId("https://tziakcha.net/?id=TszL5UsT");
  const rounds = await session.fetchRounds(sessionId);
  const summary = stats.summarizeSession(rounds);

  console.log(rounds.records.length);
  console.log(summary.totalRounds);
  console.log(record.extractWins(rounds).length);
}

main().catch(console.error);
```

## 文档

https://tziakcha-stats.github.io/tziakcha-fetcher/

## License

Apache-2.0 © [Choimoe](https://github.com/Choimoe)
