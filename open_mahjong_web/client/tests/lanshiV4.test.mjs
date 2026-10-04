import test from "node:test";
import assert from "node:assert/strict";
import {
  hepaiCheckLanshi,
  tingpaiCheckLanshi,
  lanshiFanPoints,
} from "../src/game2d/calc/guobiao/lanshiV4.ts";

test("settlement labels use Lanshi values and preserve repeated counts", () => {
  for (const [name, points] of [["一般高*1", 2], ["一般高*2", 4], ["清龙", 6], ["清一色", 12], ["四同顺", 100], ["喜相逢*3", 3], ["错和", 0]]) {
    assert.equal(lanshiFanPoints(name), points);
  }
  for (const name of ["", "三色喜相逢", "一般高*0", "一般高*5", "清龙*2"]) assert.equal(lanshiFanPoints(name), null);
});

function tiles(text) {
  return [...text.matchAll(/([1-9]+)([mpsz])/g)].flatMap(([, digits, suit]) =>
    [...digits]
      .map(Number)
      .map((n) => n + { m: 10, p: 20, s: 30, z: 40 }[suit]),
  );
}
test("user hand counts both repeated p sequences and mixed double sequence", () => {
  const hand = tiles("11345m334455p234s");
  assert.deepEqual(hepaiCheckLanshi(hand, [], ["点和"], 34), {
    fan: 4,
    fanNames: ["一般高*1", "门前清", "喜相逢*1"],
  });
  assert.deepEqual(hepaiCheckLanshi(hand, [], ["自摸"], 34), {
    fan: 5,
    fanNames: ["一般高*1", "门前清", "喜相逢*1", "自摸"],
  });
});
test("full straight allows highest noncyclic duplicate sequence", () => {
  assert.deepEqual(
    hepaiCheckLanshi(tiles("44p456778899s"), ["s32"], ["自摸"], 24),
    { fan: 9, fanNames: ["清龙", "一般高*1", "自摸"] },
  );
});
test("reported sequence relations survive all open and closed arrangements", () => {
  for (let mask = 0; mask < 16; mask++) for (const selfDraw of [false, true]) {
    const hand = [11, 11], melds = [];
    [14, 24, 24, 33].forEach((tile, i) => {
      if (mask & (1 << i)) melds.push(`s${tile}`);
      else hand.push(tile - 1, tile, tile + 1);
    });
    const result = hepaiCheckLanshi(hand, melds, [selfDraw ? "自摸" : "点和"], 11);
    const names = ["一般高*1", "喜相逢*1"];
    if (!mask) names.push("门前清");
    if (mask === 15 && !selfDraw) names.push("全求人");
    if (selfDraw) names.push("自摸");
    assert.deepEqual([...result.fanNames].sort(), names.sort());
    assert.equal(result.fan, 3 + Number(!mask) + Number(selfDraw) + 2 * Number(mask === 15 && !selfDraw));
  }
});
test("occasional fans substitute only when ordinary score is below five", () => {
  const hand = tiles("11345m334455p234s");
  assert.deepEqual(hepaiCheckLanshi(hand, [], ["海底捞月"], 34), {
    fan: 5,
    fanNames: ["海底捞月"],
  });
  assert.deepEqual(hepaiCheckLanshi(hand, [], ["妙手回春"], 34), {
    fan: 5,
    fanNames: ["一般高*1", "门前清", "喜相逢*1", "自摸"],
  });
});
test("native waits exclude knitted straight and fifth copies", () => {
  assert.deepEqual(tingpaiCheckLanshi(tiles("147m258p369s1112z"), []), []);
  assert.deepEqual(tingpaiCheckLanshi([11], ["G11", "k22", "s35", "k43"]), []);
  assert.deepEqual(
    tingpaiCheckLanshi(tiles("1112345678999m"), []),
    [11, 12, 13, 14, 15, 16, 17, 18, 19],
  );
  assert.deepEqual(
    tingpaiCheckLanshi(tiles("19m19p19s1234567z"), []),
    [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47],
  );
});
test("strict concealed triplets distinguish winning triplet and winning pair", () => {
  const hand = tiles("222p333s44466z");
  const trip = hepaiCheckLanshi(hand, ["G11"], ["点和"], 22);
  const pair = hepaiCheckLanshi(hand, ["G11"], ["点和"], 46);
  assert(trip.fanNames.includes("三暗刻"));
  assert(!trip.fanNames.includes("四暗刻"));
  assert(pair.fanNames.includes("四暗刻"));
  assert(!pair.fanNames.includes("门前清"));
});
test("nine gates opening exception applies only to dealer starting hand", () => {
  const hand = tiles("11123455678999m");
  assert(
    !hepaiCheckLanshi(hand, [], ["天和"], 11).fanNames.includes("九莲宝灯"),
  );
  assert.deepEqual(hepaiCheckLanshi(hand, [], ["天和", "庄家起手"], 11), {
    fan: 100,
    fanNames: ["九莲宝灯"],
  });
});
test("invalid hands and caller mutation", () => {
  for (const [hand, melds, tile] of [
    [null, [], 11],
    [[11, 11], null, 11],
    [tiles("123456789m11122p"), [], 47],
    [tiles("123456789m22p"), ["g11"], 22],
  ]) {
    assert.deepEqual(hepaiCheckLanshi(hand, melds, [], tile), {
      fan: 0,
      fanNames: [],
    });
  }
  const hand = tiles("11345m334455p234s"),
    copy = [...hand],
    way = ["自摸"];
  hepaiCheckLanshi(hand, [], way, 34);
  assert.deepEqual(hand, copy);
  assert.deepEqual(way, ["自摸"]);
});

test("a zero-fan open hand remains a structural wait for the wrong-win flow", () => {
  const hand = tiles("234456p678s5z");
  assert.deepEqual(hepaiCheckLanshi([...hand, 45], ["s12"], ["点和"], 45), { fan: 0, fanNames: [] });
  assert.ok(tingpaiCheckLanshi(hand, ["s12"]).includes(45));
});
