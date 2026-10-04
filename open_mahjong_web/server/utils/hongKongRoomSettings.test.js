const test = require('node:test');
const assert = require('node:assert/strict');
const { readRoomSettings, applyRoomSettingsChange } = require('./eventRoomSettings');

function save(sub_rule, detailed_config) {
  const doc = readRoomSettings({});
  return applyRoomSettingsChange(doc, { kind: 'manual', userId: 10001, eventStatus: 'active', body: {
    revision: doc.revision, room_rule: 'hongkong', room_config: { sub_rule, detailed_config },
  } }).settings.manual;
}

for (const sub of ['qingzhang', 'new13', 'new16']) {
  test(`HK ${sub}: canonical saved settings agree with server variant constraints`, () => {
    const detail = { flowers: false, new13_full_shoot: false };
    const result = save(`hongkong/${sub}`, detail);
    assert.equal(result.room_config.sub_rule, `hongkong/${sub}`);
    assert.equal(result.room_config.detailed_config.flowers, sub === 'new16');
    assert.equal(result.room_config.detailed_config.new13_full_shoot, false);
    assert.equal(result.room_config.detailed_config.new13_version, 'gametower');
    assert.deepEqual(detail, { flowers: false, new13_full_shoot: false });
  });
}
test('HK rejects invalid profiles, unknown keys, string booleans and new13 flowers', () => {
  for (const [sub, detail] of [['bad', {}], ['new13', { flowers: true }], ['qingzhang', { flowers: 'false' }], ['new16', { winner: 0 }], ['new16', null]]) {
    assert.throws(() => save(`hongkong/${sub}`, detail));
  }
});

test('恋绘色 defaults and every house-rule choice are saved without mutating input', () => {
  assert.equal(save('hongkong/new13',{new13_version:'lianhuise'}).room_config.detailed_config.flowers,true);
  for (const win_claim of ['default','head_bump','multiple']) for (const dealer_mode of ['default','rotate','win_or_draw']) {
    for (let bits=0;bits<32;bits++) {
      const input={new13_version:'lianhuise',win_claim,dealer_mode};
      ['flowers','self_draw_only','liability_twelve','liability_dragons','liability_kong'].forEach((key,i)=>input[key]=!!(bits&(1<<i)));
      const saved=save('hongkong/new13',input).room_config.detailed_config;
      for(const [key,value] of Object.entries(input)) assert.equal(saved[key],value);
    }
  }
});

test('all new switch and enum types are validated', () => {
  for(const key of ['new13_version','win_claim','dealer_mode','self_draw_only','liability_twelve','liability_dragons','liability_kong','liability_limit']) {
    for (const value of [null,1,[],{},'unsupported']) assert.throws(()=>save('hongkong/new13',{new13_version:'lianhuise',[key]:value}));
  }
});

test('explicit subrules determine their version and remix always has flowers', () => {
  for(const version of ['gametower','lianhuise']) for(const savedVersion of ['gametower','lianhuise']) {
    const room=save(`hongkong/new13_${version}`,{new13_version:savedVersion}).room_config;
    assert.equal(room.detailed_config.new13_version,version);
    assert.equal(room.detailed_config.flowers,version==='lianhuise');
  }
  for(const flower of [true,false]) for(const enabled of [true,false]) {
    const room=save('hongkong/qingzhang_lianhuise',{flowers:flower,liability_limit:enabled}).room_config;
    assert.equal(room.detailed_config.flowers,true);
    assert.equal(room.detailed_config.liability_limit,enabled);
  }
  assert.throws(()=>save('hongkong/new13_gametower',{flowers:true}));
  for(const version of ['gametower','lianhuise']) assert.throws(()=>save(`hongkong/new13_${version}`,{new13_version:'bad'}));
});
